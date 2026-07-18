using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class LegalIndexStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] LegalDestinations =
    [
        "/legal/nondisclosureagreement",
        "/legal/privacypolicy",
        "/legal/termsconditions",
    ];

    private readonly WebApplicationFactory<Program> factory;

    public LegalIndexStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheLegalRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Legal", "LegalPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Legal", "LegalContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Legal", "Index.cshtml"));

        Assert.Contains("BlazorRouting:Legal", program, StringComparison.Ordinal);
        Assert.Contains("\"/Legal/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("\"Legal\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(LegalContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Legal information | MALIEV",
        "Review MALIEV policies for website use, privacy, and confidential manufacturing project information.",
        "Legal information")]
    [InlineData(
        "th",
        "ข้อมูลทางกฎหมาย | MALIEV",
        "ตรวจสอบนโยบายของ MALIEV เกี่ยวกับการใช้เว็บไซต์ ความเป็นส่วนตัว และข้อมูลงานผลิตที่เป็นความลับ",
        "ข้อมูลทางกฎหมาย")]
    public async Task LegalRoute_RendersLocalizedStaticSsrWithSeoAnalyticsAndDocumentParity(
        string culture,
        string title,
        string description,
        string heading)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/legal?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Equal(LegalDestinations.Length, Regex.Matches(source, "class=\"maliev-panel docs-link-card\"", RegexOptions.CultureInvariant).Count);
        foreach (var destination in LegalDestinations)
        {
            Assert.Contains($"href=\"{destination}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wowjs", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/legal?culture=en", "https://www.maliev.com/legal?culture=en", "https://www.maliev.com/legal")]
    [InlineData("th", "https://www.maliev.com/legal", "https://www.maliev.com/legal?culture=en", "https://www.maliev.com/legal")]
    public async Task LegalRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/legal?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledLegalRoute_UsesTheRetainedRazorFallback()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Legal", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/legal?culture=en"));

        Assert.Contains("<title>Legal information | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static int CountLink(string source, string relation, string url) =>
        Regex.Matches(source, $"<link(?=[^>]*rel=\"{Regex.Escape(relation)}\")(?=[^>]*href=\"{Regex.Escape(url)}\")[^>]*>", RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        Regex.Matches(source, $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{Regex.Escape(url)}\")(?=[^>]*hreflang=\"{Regex.Escape(language)}\")[^>]*>", RegexOptions.CultureInvariant).Count;

    private static string ExtractDocumentLinks(string source) => string.Join(
        Environment.NewLine,
        Regex.Matches(source, "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => match.Value));

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
