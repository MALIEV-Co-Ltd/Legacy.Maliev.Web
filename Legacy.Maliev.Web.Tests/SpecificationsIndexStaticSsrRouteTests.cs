using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class SpecificationsIndexStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public SpecificationsIndexStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheSpecificationsIndexStaticSsrPageAndKeepsTheRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Knowledges", "Specifications", "SpecificationsIndexPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(routePath);
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Knowledges", "Specifications", "SpecificationsIndexContent.razor"));
        var razorFallback = File.ReadAllText(Path.Combine(web, "Areas", "Knowledges", "Pages", "Specifications", "Index.cshtml"));

        Assert.Contains("BlazorRouting:KnowledgesSpecifications", program, StringComparison.Ordinal);
        Assert.Contains("AddAreaPageRouteModelConvention", program, StringComparison.Ordinal);
        Assert.Contains("\"Knowledges\"", program, StringComparison.Ordinal);
        Assert.Contains("\"/Specifications/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("model.Selectors.Clear()", program, StringComparison.Ordinal);
        Assert.Contains("@page \"/Knowledges/Specifications\"", route, StringComparison.Ordinal);
        Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("@page", razorFallback, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(SpecificationsIndexContent)\"", razorFallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Service specifications | MALIEV",
        "Choose a process for concise preparation guidance, then use the service page for current materials, pricing, lead-time, and FAQ information.",
        "Service specifications",
        "Technical guidance",
        "CNC machining",
        "3D printing",
        "3D scanning")]
    [InlineData(
        "th",
        "ข้อแนะนำเฉพาะบริการ | MALIEV",
        "เลือกกระบวนการเพื่อดูแนวทางเตรียมงาน และดูหน้าบริการสำหรับวัสดุ ราคา ระยะเวลา และคำถามที่พบบ่อยล่าสุด",
        "ข้อแนะนำเฉพาะบริการ",
        "คำแนะนำทางเทคนิค",
        "งาน CNC",
        "งานพิมพ์ 3 มิติ",
        "งานสแกน 3 มิติ")]
    public async Task SpecificationsIndexRoute_RendersCompleteLocalizedStaticDocument(
        string culture,
        string title,
        string description,
        string heading,
        string eyebrow,
        string cnc,
        string printing,
        string scanning)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/specifications?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{eyebrow}<", source, StringComparison.Ordinal);
        Assert.Contains($">{cnc}<", source, StringComparison.Ordinal);
        Assert.Contains($">{printing}<", source, StringComparison.Ordinal);
        Assert.Contains($">{scanning}<", source, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(source, "<a[^>]*class=\"maliev-panel docs-link-card\"", RegexOptions.CultureInvariant).Count);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-business-structured-data\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"knowledge-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"knowledge-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", source, StringComparison.Ordinal);
        Assert.Contains("data-workspace-open", source, StringComparison.Ordinal);
        Assert.Contains("data-workspace-close", source, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Escape'", source, StringComparison.Ordinal);
        Assert.Contains("openButton.focus()", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/knowledges/guidelines\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/knowledges/workflow\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/knowledges/specifications/cnc-machining\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/knowledges/specifications/3d-printing\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/knowledges/specifications/3d-scanning\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new WOW", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"wow", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/knowledges/specifications?culture=en", "https://www.maliev.com/knowledges/specifications?culture=en", "https://www.maliev.com/knowledges/specifications")]
    [InlineData("th", "https://www.maliev.com/knowledges/specifications", "https://www.maliev.com/knowledges/specifications?culture=en", "https://www.maliev.com/knowledges/specifications")]
    public async Task SpecificationsIndexRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/specifications?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Service specifications | MALIEV", "Knowledge center", "Service specifications")]
    [InlineData("th", "ข้อแนะนำเฉพาะบริการ | MALIEV", "ศูนย์ความรู้", "ข้อแนะนำเฉพาะบริการ")]
    public async Task SpecificationsIndexRoute_EmitsWebPageAndBreadcrumbStructuredData(
        string culture,
        string pageName,
        string knowledgeCenterName,
        string specificationsName)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/specifications?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var documents = StructuredDataRegex().Matches(source)
            .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var webPage = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "WebPage");
        using var breadcrumb = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "BreadcrumbList");
        Assert.Equal(pageName, webPage.RootElement.GetProperty("name").GetString());
        Assert.Equal(culture, webPage.RootElement.GetProperty("inLanguage").GetString());
        Assert.Equal(knowledgeCenterName, breadcrumb.RootElement.GetProperty("itemListElement")[1].GetProperty("name").GetString());
        Assert.Equal(specificationsName, breadcrumb.RootElement.GetProperty("itemListElement")[2].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheSpecificationsIndexRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/knowledges/specifications?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/knowledges/specifications?culture=en");
        request.Headers.Add("Cookie", consentCookie.Split(';', 2)[0]);
        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'granted';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("https://www.googletagmanager.com/ns.html?id=GTM-KHDDLVRR", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledSpecificationsIndexRoute_UsesTheRetainedRazorFallbackAtTheCanonicalUrl()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:KnowledgesSpecifications", "false"));
        using var client = CreateClient(fallbackFactory);
        using var response = await client.GetAsync("/knowledges/specifications?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Service specifications | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\"", source, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) =>
        sourceFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static int CountLink(string source, string relation, string url) =>
        Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"{Regex.Escape(relation)}\")(?=[^>]*href=\"{Regex.Escape(url)}\")[^>]*>",
            RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{Regex.Escape(url)}\")(?=[^>]*hreflang=\"{Regex.Escape(language)}\")[^>]*>",
            RegexOptions.CultureInvariant).Count;

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

    [GeneratedRegex("<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex StructuredDataRegex();

    [GeneratedRegex("data-cookie-string=\"(?<cookie>[^\"]+)\"")]
    private static partial Regex ConsentCookieRegex();
}
