using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class EmailConfirmationStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public EmailConfirmationStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
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
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:EmailConfirmation", "false"));
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync(
            "/account/emailconfirmation?email=user%40example.com&token=invalid-token&culture=en");

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
}
