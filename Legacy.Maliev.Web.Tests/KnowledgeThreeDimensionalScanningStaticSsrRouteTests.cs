using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class KnowledgeThreeDimensionalScanningStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public KnowledgeThreeDimensionalScanningStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheThreeDimensionalScanningSpecificationPageAndKeepsTheRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Knowledges", "Specifications", "ThreeDimensionalScanningSpecificationPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(routePath);
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Knowledges", "Specifications", "ThreeDimensionalScanningContent.razor"));
        var razorFallback = File.ReadAllText(Path.Combine(web, "Areas", "Knowledges", "Pages", "Specifications", "3D-Scanning.cshtml"));
        var stylesheet = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "application-shell.css"));

        Assert.Contains("BlazorRouting:KnowledgesSpecifications3DScanning", program, StringComparison.Ordinal);
        Assert.Contains("AddAreaPageRouteModelConvention", program, StringComparison.Ordinal);
        Assert.Contains("\"Knowledges\"", program, StringComparison.Ordinal);
        Assert.Contains("\"/Specifications/3D-Scanning\"", program, StringComparison.Ordinal);
        Assert.Contains("model.Selectors.Clear()", program, StringComparison.Ordinal);
        Assert.Contains("@page \"/Knowledges/Specifications/3D-Scanning\"", route, StringComparison.Ordinal);
        Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("@page", razorFallback, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ThreeDimensionalScanningContent)\"", razorFallback, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", stylesheet, StringComparison.Ordinal);
        Assert.Contains("transition-duration: .01ms !important", stylesheet, StringComparison.Ordinal);

        var routedPages = Directory.EnumerateFiles(
                Path.Combine(web, "Components"),
                "*.razor",
                SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Any(line => line.TrimStart().StartsWith("@page ", StringComparison.Ordinal)))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "AboutPage.razor",
                "CncMachiningPage.razor",
                "CncMachiningSpecificationPage.razor",
                "CustomManufacturingPage.razor",
                "GuidelinesPage.razor",
                "HomePage.razor",
                "KnowledgeIndexPage.razor",
                "ServicesPage.razor",
                "SpecificationsIndexPage.razor",
                "ThreeDimensionalPrintingPage.razor",
                "ThreeDimensionalPrintingSpecificationPage.razor",
                "ThreeDimensionalScanningPage.razor",
                "ThreeDimensionalScanningSpecificationPage.razor",
                "WorkflowPage.razor"
            ],
            routedPages);
    }

    [Theory]
    [InlineData(
        "en",
        "3D scanning specifications | MALIEV",
        "Bring the best-condition part available and tell us whether you need raw scan data, reverse-engineered CAD, or a deviation report.",
        "Prepare a part for 3D scanning",
        "3D scanning preparation",
        "Clean and identify the part",
        "Remove loose dirt and temporary items. Mark features that must remain untouched and disclose damaged or missing areas.",
        "Explain the deliverable",
        "Raw scans preserve measured surface data; reverse engineering creates editable CAD; deviation analysis compares a scan with reference geometry.",
        "Plan access and surface treatment",
        "Deep internal features, occluded surfaces, reflective or transparent materials may need repositioning, temporary scanning spray, or another measurement method.",
        "View 3D scanning service")]
    [InlineData(
        "th",
        "ข้อแนะนำงานสแกน 3 มิติ | MALIEV",
        "นำชิ้นงานสภาพดีที่สุดมา และระบุว่าต้องการไฟล์สแกนดิบ CAD reverse engineering หรือรายงาน deviation",
        "เตรียมชิ้นงานสำหรับสแกน 3 มิติ",
        "การเตรียมงานสแกน 3 มิติ",
        "ทำความสะอาดและระบุชิ้นงาน",
        "กำจัดฝุ่นและสิ่งของชั่วคราว ระบุจุดที่ห้ามแก้ไขและส่วนที่เสียหายหรือขาด",
        "ระบุไฟล์ส่งมอบ",
        "ไฟล์สแกนดิบเก็บข้อมูลผิวที่วัดได้ reverse engineering สร้าง CAD แก้ไขได้ และ deviation analysis เปรียบเทียบสแกนกับแบบอ้างอิง",
        "วางแผนการเข้าถึงและเตรียมผิว",
        "ร่องลึก ผิวที่บัง วัสดุสะท้อนหรือโปร่งใส อาจต้องจัดท่า ใช้สเปรย์ชั่วคราว หรือใช้วิธีวัดอื่น",
        "ดูบริการสแกน 3 มิติ")]
    public async Task ThreeDimensionalScanningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument(
        string culture,
        string title,
        string description,
        string heading,
        string eyebrow,
        string firstStep,
        string firstStepDescription,
        string secondStep,
        string secondStepDescription,
        string thirdStep,
        string thirdStepDescription,
        string serviceLink)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/specifications/3d-scanning?culture={culture}&tracking=excluded");
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
        Assert.Contains($">{firstStep}<", source, StringComparison.Ordinal);
        Assert.Contains($">{firstStepDescription}<", source, StringComparison.Ordinal);
        Assert.Contains($">{secondStep}<", source, StringComparison.Ordinal);
        Assert.Contains($">{secondStepDescription}<", source, StringComparison.Ordinal);
        Assert.Contains($">{thirdStep}<", source, StringComparison.Ordinal);
        Assert.Contains($">{thirdStepDescription}<", source, StringComparison.Ordinal);
        Assert.Contains($">{serviceLink}<", source, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(source, "<section><span>[123]</span>", RegexOptions.CultureInvariant).Count);
        Assert.Contains("href=\"/services/3d-scanning\"", source, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("aria-controls=\"knowledge-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", source, StringComparison.Ordinal);
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
    [InlineData("en", "https://www.maliev.com/knowledges/specifications/3d-scanning?culture=en", "https://www.maliev.com/knowledges/specifications/3d-scanning?culture=en", "https://www.maliev.com/knowledges/specifications/3d-scanning")]
    [InlineData("th", "https://www.maliev.com/knowledges/specifications/3d-scanning", "https://www.maliev.com/knowledges/specifications/3d-scanning?culture=en", "https://www.maliev.com/knowledges/specifications/3d-scanning")]
    public async Task ThreeDimensionalScanningSpecificationRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/specifications/3d-scanning?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "3D scanning specifications | MALIEV", "Knowledge center", "Service specifications", "3D scanning specifications")]
    [InlineData("th", "ข้อแนะนำงานสแกน 3 มิติ | MALIEV", "ศูนย์ความรู้", "ข้อแนะนำเฉพาะบริการ", "ข้อแนะนำงานสแกน 3 มิติ")]
    public async Task ThreeDimensionalScanningSpecificationRoute_EmitsLocalizedWebPageAndFourLevelBreadcrumbSchema(
        string culture,
        string pageName,
        string knowledgeCenterName,
        string specificationsName,
        string scanningName)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/specifications/3d-scanning?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var documents = StructuredDataRegex().Matches(source)
            .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var webPage = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "WebPage");
        using var breadcrumb = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "BreadcrumbList");
        Assert.Equal(pageName, webPage.RootElement.GetProperty("name").GetString());
        Assert.Equal(culture, webPage.RootElement.GetProperty("inLanguage").GetString());
        var items = breadcrumb.RootElement.GetProperty("itemListElement");
        Assert.Equal(4, items.GetArrayLength());
        Assert.Equal(knowledgeCenterName, items[1].GetProperty("name").GetString());
        Assert.Equal(specificationsName, items[2].GetProperty("name").GetString());
        Assert.Equal(scanningName, items[3].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheThreeDimensionalScanningSpecificationRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/knowledges/specifications/3d-scanning?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/knowledges/specifications/3d-scanning?culture=en");
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
    public async Task DisabledThreeDimensionalScanningSpecificationRoute_UsesTheRetainedRazorFallback()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:KnowledgesSpecifications3DScanning", "false"));
        using var client = CreateClient(fallbackFactory);
        using var response = await client.GetAsync("/knowledges/specifications/3d-scanning?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>3D scanning specifications | MALIEV</title>", source, StringComparison.Ordinal);
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
