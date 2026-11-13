using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ForgotPasswordStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForgotPasswordStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThaiDescription_PreservesSourceCopyOnActiveAndFallbackRoutes(bool rendererEnabled)
    {
        await using var routeFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:ForgotPassword", rendererEnabled ? "true" : "false"));
        using var client = routeFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/account/forgotpassword?culture=th");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<meta name=\"description\" content=\"กู้คืนรหัสผ่านบัญชี MALIEV ด้วยลิงก์ที่ปลอดภัยเพื่อกลับไปจัดการใบเสนอราคาและโปรเจ็คผลิตของคุณ\"", source, StringComparison.Ordinal);
        if (rendererEnabled)
        {
            Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
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
