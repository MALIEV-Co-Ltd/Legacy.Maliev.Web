using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class LogoutStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public LogoutStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheLogoutRouteAndRetainsItsAuthorizedPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Logout.cshtml.cs"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "LogoutPage.razor"));

        Assert.Contains("BlazorRouting:Logout", program, StringComparison.Ordinal);
        Assert.Contains("\"/Account/Logout\"", program, StringComparison.Ordinal);
        Assert.Contains("\"Logout\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("[Authorize]", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("IAccountSessionManager", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledLogoutRoute_UsesTheRetainedAuthorizedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Logout", "false"));
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/account/logout");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
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
