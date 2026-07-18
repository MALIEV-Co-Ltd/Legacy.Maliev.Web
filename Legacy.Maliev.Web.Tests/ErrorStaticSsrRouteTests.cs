using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ErrorStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ErrorStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheErrorRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "ErrorPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var app = File.ReadAllText(Path.Combine(web, "Components", "App.razor"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "ErrorContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Error.cshtml"));

        Assert.Contains("BlazorRouting:Error", program, StringComparison.Ordinal);
        Assert.Contains("\"/Error\"", program, StringComparison.Ordinal);
        Assert.Contains("\"Error\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("suppressIdentityNavigation", app, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ErrorContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", 404, "Error | MALIEV", "Page not found", "The page may have moved, or the address may be incorrect.")]
    [InlineData("th", 404, "ข้อผิดพลาด | MALIEV", "ไม่พบหน้าที่ต้องการ", "หน้านี้อาจถูกย้าย หรือลิงก์ไม่ถูกต้อง")]
    [InlineData("en", 500, "Error | MALIEV", "Sorry. Something did not work properly.", "Please try again. If the problem continues, contact support and include the request ID below.")]
    [InlineData("th", 500, "ข้อผิดพลาด | MALIEV", "ขออภัย ระบบทำงานผิดพลาด", "โปรดลองอีกครั้ง หากยังพบปัญหา โปรดติดต่อฝ่ายช่วยเหลือพร้อมแจ้งรหัสคำขอด้านล่าง")]
    public async Task ErrorRoute_RendersLocalizedSafeStaticSsrAndPreservesResponseContract(
        string culture,
        int statusCode,
        string title,
        string heading,
        string description)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/error?code={statusCode}&culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,nofollow\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{description}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("customer@example.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-access-token", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-refresh-token", source, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/Member", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action=\"/Account/Logout\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledErrorRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Error", "false"));
        using var client = CreateClient(fallbackFactory);
        using var response = await client.GetAsync("/error?code=404&culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

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
