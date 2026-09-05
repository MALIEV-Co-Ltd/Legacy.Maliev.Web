using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ErrorStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ErrorStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
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
    [InlineData("en", 404, "404 — Page not found | MALIEV", "Page not found", "The page may have moved, or the address may be incorrect.")]
    [InlineData("th", 404, "404 — ไม่พบหน้าที่ต้องการ | MALIEV", "ไม่พบหน้าที่ต้องการ", "หน้านี้อาจถูกย้าย หรือลิงก์ไม่ถูกต้อง")]
    [InlineData("en", 500, "Error | MALIEV", "Sorry. Something did not work properly.", "Please include this incident ID when contacting support.")]
    [InlineData("th", 500, "ข้อผิดพลาด | MALIEV", "ขออภัย ระบบทำงานผิดพลาด", "โปรดแจ้งรหัสเหตุขัดข้องนี้เมื่อติดต่อฝ่ายช่วยเหลือ")]
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
        Assert.Contains(
            statusCode == StatusCodes.Status404NotFound
                ? "<meta name=\"robots\" content=\"noindex, follow\""
                : "<meta name=\"robots\" content=\"noindex,nofollow\"",
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{description}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.False(response.Headers.Contains("X-Incident-Id"));
        if (statusCode == StatusCodes.Status404NotFound)
        {
            Assert.Contains("class=\"space-error\"", source, StringComparison.Ordinal);
            Assert.Contains("earth-horizon-mobile.webp", source, StringComparison.Ordinal);
            Assert.Contains("earth-horizon.webp", source, StringComparison.Ordinal);
            Assert.Contains("astronaut.webp", source, StringComparison.Ordinal);
            Assert.Contains("route-error.css", source, StringComparison.Ordinal);
            Assert.Contains("route-error.js", source, StringComparison.Ordinal);
            Assert.DoesNotContain("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
            Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
            Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
            Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("customer@example.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-access-token", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-refresh-token", source, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/Member", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action=\"/Account/Logout\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("id=\"incident-id\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"incident-occurred-at\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mailto:support@maliev.com", source, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("astronaut.webp", "6865B1987023101F3616EB2D85403B4F5A47CACAB0AF03A78BED29C3E5D41DED")]
    [InlineData("earth-horizon-mobile.webp", "D6F2D6A03B23CFB4A6321739B6FD39BA7FFA0DD9AE81C337C11773EDFF85F2EB")]
    [InlineData("earth-horizon.webp", "F5E7EA7B938E38D53C53D0D4BAE80FA5DBEC3D869AD66F154267150A0331B18C")]
    public void NotFoundAssets_MatchTheAuthoritativeSource(string fileName, string expectedSha256)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "images",
            "error",
            fileName);

        Assert.True(File.Exists(path), $"Expected 404 asset '{path}'.");
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
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
