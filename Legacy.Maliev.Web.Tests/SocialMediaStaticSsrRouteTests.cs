using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SocialMediaStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] SocialDestinations =
    [
        "https://www.youtube.com/channel/UCCosquPSUed6UPlMcRCq0Ig",
        "https://www.tiktok.com/@maliev.marketing",
        "https://www.instagram.com/maliev.manufacturing/",
        "https://www.threads.com/@maliev.manufacturing",
        "https://line.me/ti/p/@maliev",
    ];

    private readonly WebApplicationFactory<Program> factory;

    public SocialMediaStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheSocialMediaRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "About", "SocialMediaPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "About", "SocialMediaContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "About", "SocialMedia.cshtml"));

        Assert.Contains("BlazorRouting:SocialMedia", program, StringComparison.Ordinal);
        Assert.Contains("\"/About/SocialMedia\"", program, StringComparison.Ordinal);
        Assert.Contains("\"SocialMedia\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(SocialMediaContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Social media | MALIEV",
        "Follow MALIEV for manufacturing examples, process updates, and company news.",
        "MALIEV on social media")]
    [InlineData(
        "th",
        "โซเชียลมีเดีย | MALIEV",
        "ติดตามตัวอย่างงานผลิต ข่าวกระบวนการ และข่าวสารจาก MALIEV",
        "MALIEV บนโซเชียลมีเดีย")]
    public async Task SocialMediaRoute_RendersLocalizedStaticSsrWithSeoAnalyticsAndLinkParity(
        string culture,
        string title,
        string description,
        string heading)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/about/socialmedia?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Equal(SocialDestinations.Length, Regex.Matches(source, "class=\"maliev-panel social-link-card\"", RegexOptions.CultureInvariant).Count);
        foreach (var destination in SocialDestinations)
        {
            Assert.Contains($"href=\"{destination}\" target=\"_blank\" rel=\"noopener\"", source, StringComparison.Ordinal);
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
    [InlineData("en", "https://www.maliev.com/about/socialmedia?culture=en", "https://www.maliev.com/about/socialmedia?culture=en", "https://www.maliev.com/about/socialmedia")]
    [InlineData("th", "https://www.maliev.com/about/socialmedia", "https://www.maliev.com/about/socialmedia?culture=en", "https://www.maliev.com/about/socialmedia")]
    public async Task SocialMediaRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/about/socialmedia?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledSocialMediaRoute_UsesTheRetainedRazorFallback()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:SocialMedia", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/about/socialmedia?culture=en"));

        Assert.Contains("<title>Social media | MALIEV</title>", source, StringComparison.Ordinal);
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
