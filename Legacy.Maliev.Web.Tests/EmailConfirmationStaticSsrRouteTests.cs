using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class EmailConfirmationStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public EmailConfirmationStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresTheRateLimitedEmailConfirmationRouteAndRetainsRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "EmailConfirmationPage.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Account", "EmailConfirmation.cshtml"));

        Assert.Contains("BlazorRouting:EmailConfirmation", program, StringComparison.Ordinal);
        Assert.Contains("\"EmailConfirmation\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("EnableRateLimiting(\"account\")", route, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(EmailConfirmationContent)\"", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"@Token\"", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledEmailConfirmationRoute_UsesTheRetainedRazorFallback()
    {
        using var authRequests = new ForbiddenAuthRequests();
        await using var fallbackFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:EmailConfirmation", "false");
            builder.UseSetting("ServiceAuthentication:ClientSecret", string.Empty);
            builder.ConfigureServices(services => services.AddHttpClient("auth")
                .ConfigurePrimaryHttpMessageHandler(() => authRequests));
        });
        Assert.Equal(string.Empty, fallbackFactory.Services
            .GetRequiredService<IOptions<ServiceAuthenticationOptions>>().Value.ClientSecret);
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync(
            "/account/emailconfirmation?email=user%40example.com&token=invalid-token&culture=en");

        Assert.Equal(0, authRequests.Calls);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private sealed class ForbiddenAuthRequests : HttpMessageHandler
    {
        private int calls;

        internal int Calls => Volatile.Read(ref calls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("This fallback renderer fixture must not contact authentication services.");
        }
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
}
