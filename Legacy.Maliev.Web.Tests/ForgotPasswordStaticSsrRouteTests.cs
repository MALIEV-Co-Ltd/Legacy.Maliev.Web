using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ForgotPasswordStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForgotPasswordStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheForgotPasswordRouteAndRetainsItsServerPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Account", "ForgotPassword.cshtml"));
        var routePath = Path.Combine(web, "Components", "Pages", "Account", "ForgotPasswordPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");
        Assert.Contains("BlazorRouting:ForgotPassword", program, StringComparison.Ordinal);
        Assert.Contains("\"/Account/ForgotPassword\"", program, StringComparison.Ordinal);
        Assert.Contains("\"ForgotPassword\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ForgotPasswordContent)\"", fallback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledForgotPasswordRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:ForgotPassword", "false"));
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync("/account/forgotpassword?culture=en");

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
