using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class CncMachiningStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public CncMachiningStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheCncMachiningRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Services", "CncMachiningPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(routePath);
        var content = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "CncMachiningContent.razor"));
        var razorFallback = File.ReadAllText(Path.Combine(web, "Pages", "Services", "CNC-Machining.cshtml"));

        Assert.Contains("BlazorRouting:Services", program, StringComparison.Ordinal);
        Assert.Contains("/Services/CNC-Machining", program, StringComparison.Ordinal);
        Assert.Contains("@page \"/services/cnc-machining\"", route, StringComparison.Ordinal);
        Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
        Assert.Contains("<PublicServiceStructuredData", route, StringComparison.Ordinal);
        Assert.Contains("FAQPage", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("@page", razorFallback, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(CncMachiningContent)\"", razorFallback, StringComparison.Ordinal);

        var routedPages = Directory.EnumerateFiles(
                Path.Combine(web, "Components"),
                "*.razor",
                SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Any(line => line.TrimStart().StartsWith("@page ", StringComparison.Ordinal)))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["AboutPage.razor", "AccessDeniedPage.razor", "CareerDetailPage.razor", "CareerIndexPage.razor", "CncMachiningPage.razor", "CncMachiningSpecificationPage.razor", "CustomManufacturingPage.razor", "GuidelinesPage.razor", "HomePage.razor", "KnowledgeIndexPage.razor", "LegalPage.razor", "NonDisclosureAgreementPage.razor", "PrivacyPolicyPage.razor", "ServicesPage.razor", "SocialMediaPage.razor", "SpecificationsIndexPage.razor", "TermsConditionsPage.razor", "ThreeDimensionalPrintingPage.razor", "ThreeDimensionalPrintingSpecificationPage.razor", "ThreeDimensionalScanningPage.razor", "ThreeDimensionalScanningSpecificationPage.razor", "WorkflowPage.razor"],
            routedPages);
    }

    [Theory]
    [InlineData(
        "en",
        "CNC Machining Services in Bangkok & Nonthaburi | One-Off and Production Parts",
        "CNC milling and turning for one-off parts, prototypes, jigs and production. Common JIS metals and engineering plastics. Send CAD and drawings for a quote.",
        "Precision CNC Machining for One-Off and Production Parts",
        "CNC machining Thailand, CNC aluminum, CNC one piece, machine shop Bangkok, CNC Nonthaburi")]
    [InlineData(
        "th",
        "รับงาน CNC ตามแบบ กรุงเทพและนนทบุรี | งานชิ้นเดียวถึงงานผลิต",
        "MALIEV รับผลิตชิ้นงาน CNC ตามไฟล์ CAD และแบบงาน ตั้งแต่งานชิ้นเดียว ต้นแบบ จิ๊ก ไปจนถึงงานผลิตซ้ำ รองรับโลหะและพลาสติกวิศวกรรม",
        "รับงาน CNC ตามแบบ ตั้งแต่งานชิ้นเดียวถึงงานผลิต",
        "รับ CNC อลูมิเนียม, รับกลึง CNC, โรงกลึง นนทบุรี, CNC งานชิ้นเดียว, โรงงาน CNC")]
    public async Task Route_RendersCompleteLocalizedStaticDocument(
        string culture,
        string title,
        string description,
        string heading,
        string keywords)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/cnc-machining?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"keywords\" content=\"{keywords}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"preload\" as=\"image\" href=\"/src/images/services/cnc/cnc-hero.webp\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"cnc-machining-content\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Quotation?item=CNC-Machining\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Contact\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/services/cnc-machining?culture=en", "https://www.maliev.com/services/cnc-machining?culture=en", "https://www.maliev.com/services/cnc-machining")]
    [InlineData("th", "https://www.maliev.com/services/cnc-machining", "https://www.maliev.com/services/cnc-machining?culture=en", "https://www.maliev.com/services/cnc-machining")]
    public async Task Route_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/cnc-machining?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "CNC Machining Services", "Can you machine only one piece?")]
    [InlineData("th", "บริการรับงาน CNC ตามแบบ", "รับทำ CNC เพียง 1 ชิ้นหรือไม่?")]
    public async Task Route_PreservesServiceAndFaqStructuredData(
        string culture,
        string serviceName,
        string faqQuestion)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/cnc-machining?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var documents = StructuredDataRegex().Matches(source)
            .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var service = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "Service");
        using var faq = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "FAQPage");
        Assert.Equal(serviceName, service.RootElement.GetProperty("name").GetString());
        Assert.Equal("CNC Machining", service.RootElement.GetProperty("serviceType").GetString());
        Assert.Equal(3, faq.RootElement.GetProperty("mainEntity").GetArrayLength());
        Assert.Equal(faqQuestion, faq.RootElement.GetProperty("mainEntity")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/services/cnc-machining?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/services/cnc-machining?culture=en");
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
    public async Task DisabledServicesRoutes_UsesTheRetainedRazorFallbackAtTheCanonicalUrl()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Services", "false"));
        using var client = CreateClient(fallbackFactory);
        using var response = await client.GetAsync("/services/cnc-machining?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>CNC Machining Services in Bangkok &amp; Nonthaburi | One-Off and Production Parts</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"cnc-machining-content\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("\"@type\":\"FAQPage\"", WebUtility.HtmlDecode(source), StringComparison.Ordinal);
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

    [GeneratedRegex("data-cookie-string=\"(?<cookie>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ConsentCookieRegex();
}
