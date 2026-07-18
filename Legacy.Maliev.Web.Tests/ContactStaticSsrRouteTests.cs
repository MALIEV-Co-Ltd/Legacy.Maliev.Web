using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ContactStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresContactGetAndRetainsTheServerPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactPage.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Contact", "Index.cshtml"));

        Assert.Contains("BlazorRouting:Contact", program, StringComparison.Ordinal);
        Assert.Contains("\"/Contact/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("\"Contact\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ContactFormFields)\"", fallback, StringComparison.Ordinal);
        Assert.Contains("ICountryClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IContactClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("INotificationClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IAntiBotVerifier", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledContactRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Contact", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync("/contact?culture=en");

        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
                true));
    }
}
