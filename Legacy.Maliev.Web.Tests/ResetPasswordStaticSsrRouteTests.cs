using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ResetPasswordStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ResetPasswordStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheResetPasswordRouteAndRetainsItsServerPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "ResetPasswordPage.razor"));

        Assert.Contains("BlazorRouting:ResetPassword", program, StringComparison.Ordinal);
        Assert.Contains("\"/Account/ResetPassword\"", program, StringComparison.Ordinal);
        Assert.Contains("\"ResetPassword\": true", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomerAuthenticationClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("Password { get; set; }", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledResetPasswordRoute_UsesTheRetainedRazorFallback()
    {
        const string token = "abcdefghijklmnopqrstuvwxyz123456";
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:ResetPassword", "false"));
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync($"/account/resetpassword?culture=en&email=user%40example.com&token={token}");

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
