using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class KnowledgeCncMachiningStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public KnowledgeCncMachiningStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheCncMachiningSpecificationPageAndKeepsTheRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Knowledges", "Specifications", "CncMachiningSpecificationPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(routePath);
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Knowledges", "Specifications", "CncMachiningContent.razor"));
        var razorFallback = File.ReadAllText(Path.Combine(web, "Areas", "Knowledges", "Pages", "Specifications", "CNC-Machining.cshtml"));

        Assert.Contains("BlazorRouting:KnowledgesSpecificationsCncMachining", program, StringComparison.Ordinal);
        Assert.Contains("\"/Specifications/CNC-Machining\"", program, StringComparison.Ordinal);
        Assert.Contains("model.Selectors.Clear()", program, StringComparison.Ordinal);
        Assert.Contains("@page \"/Knowledges/Specifications/CNC-Machining\"", route, StringComparison.Ordinal);
        Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("@page", razorFallback, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(CncMachiningContent)\"", razorFallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "CNC machining specifications | MALIEV",
        "A solid 3D model defines nominal geometry; a drawing communicates requirements that geometry alone cannot describe.",
        "Files and requirements for CNC machining",
        "CNC machining preparation",
        "Provide the model",
        "STEP or another solid CAD format is preferred. Include separate files for each part and identify the revision.",
        "Add the drawing",
        "Call out tolerances, datums, threads, fits, surface finish, heat treatment, coatings, deburring, and inspection requirements.",
        "Confirm production details",
        "Specify material grade, quantity, repeat-order expectations, supplied components, packaging, and target date.",
        "View CNC machining service")]
    [InlineData(
        "th",
        "ข้อแนะนำงาน CNC | MALIEV",
        "โมเดล solid 3 มิติกำหนดรูปทรง ส่วนแบบงานใช้สื่อสารข้อกำหนดที่รูปทรงอย่างเดียวระบุไม่ได้",
        "ไฟล์และข้อกำหนดสำหรับงาน CNC",
        "การเตรียมงาน CNC",
        "ส่งโมเดล",
        "ควรใช้ STEP หรือ solid CAD แยกไฟล์แต่ละชิ้นและระบุรีวิชัน",
        "แนบแบบงาน",
        "ระบุค่าความคลาดเคลื่อน datum เกลียว fit ผิว การอบชุบ การเคลือบ ลบคม และการตรวจสอบ",
        "ยืนยันรายละเอียดการผลิต",
        "ระบุเกรดวัสดุ จำนวน งานซ้ำ ชิ้นส่วนที่จัดหา บรรจุภัณฑ์ และวันที่ต้องการ",
        "ดูบริการ CNC")]
    public async Task CncMachiningSpecificationRoute_RendersCompleteLocalizedAccessibleDocument(
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
        using var response = await client.GetAsync($"/knowledges/specifications/cnc-machining?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
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
        Assert.Contains("href=\"/services/cnc-machining\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"knowledge-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", source, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Escape'", source, StringComparison.Ordinal);
        Assert.Contains("openButton.focus()", source, StringComparison.Ordinal);
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
    [InlineData("en", "https://www.maliev.com/knowledges/specifications/cnc-machining?culture=en", "https://www.maliev.com/knowledges/specifications/cnc-machining?culture=en", "https://www.maliev.com/knowledges/specifications/cnc-machining")]
    [InlineData("th", "https://www.maliev.com/knowledges/specifications/cnc-machining", "https://www.maliev.com/knowledges/specifications/cnc-machining?culture=en", "https://www.maliev.com/knowledges/specifications/cnc-machining")]
    public async Task CncMachiningSpecificationRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/knowledges/specifications/cnc-machining?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "CNC machining specifications | MALIEV", "Knowledge center", "Service specifications", "CNC machining specifications")]
    [InlineData("th", "ข้อแนะนำงาน CNC | MALIEV", "ศูนย์ความรู้", "ข้อแนะนำเฉพาะบริการ", "ข้อแนะนำงาน CNC")]
    public async Task CncMachiningSpecificationRoute_EmitsLocalizedWebPageAndFourLevelBreadcrumbSchema(
        string culture,
        string pageName,
        string knowledgeCenterName,
        string specificationsName,
        string machiningName)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/knowledges/specifications/cnc-machining?culture={culture}"));
        var documents = StructuredDataRegex().Matches(source)
            .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
            .ToArray();

        using var webPage = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "WebPage");
        using var breadcrumb = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "BreadcrumbList");
        Assert.Equal(pageName, webPage.RootElement.GetProperty("name").GetString());
        Assert.Equal(culture, webPage.RootElement.GetProperty("inLanguage").GetString());
        var items = breadcrumb.RootElement.GetProperty("itemListElement");
        Assert.Equal(4, items.GetArrayLength());
        Assert.Equal(knowledgeCenterName, items[1].GetProperty("name").GetString());
        Assert.Equal(specificationsName, items[2].GetProperty("name").GetString());
        Assert.Equal(machiningName, items[3].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheCncMachiningSpecificationRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/knowledges/specifications/cnc-machining?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/knowledges/specifications/cnc-machining?culture=en");
        request.Headers.Add("Cookie", consentCookie.Split(';', 2)[0]);
        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("var consentState = 'granted';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("https://www.googletagmanager.com/ns.html?id=GTM-KHDDLVRR", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledCncMachiningSpecificationRoute_UsesTheRetainedRazorFallback()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:KnowledgesSpecificationsCncMachining", "false"));
        using var client = CreateClient(fallbackFactory);
        using var response = await client.GetAsync("/knowledges/specifications/cnc-machining?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>CNC machining specifications | MALIEV</title>", source, StringComparison.Ordinal);
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

    [GeneratedRegex("<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex StructuredDataRegex();

    [GeneratedRegex("data-cookie-string=\"(?<cookie>[^\"]+)\"")]
    private static partial Regex ConsentCookieRegex();
}
