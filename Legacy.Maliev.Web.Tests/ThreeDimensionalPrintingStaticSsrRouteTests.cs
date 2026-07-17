using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class ThreeDimensionalPrintingStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ThreeDimensionalPrintingStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheThreeDimensionalPrintingRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "ThreeDimensionalPrintingPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(routePath);
        var content = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "ThreeDimensionalPrintingContent.razor"));
        var razorFallback = File.ReadAllText(Path.Combine(web, "Pages", "Services", "3D-Printing.cshtml"));

        Assert.Contains("BlazorRouting:Services", program, StringComparison.Ordinal);
        Assert.Contains("/Services/3D-Printing", program, StringComparison.Ordinal);
        Assert.Contains("@page \"/services/3d-printing\"", route, StringComparison.Ordinal);
        Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
        Assert.Contains("<PublicServiceStructuredData", route, StringComparison.Ordinal);
        Assert.Contains("FAQPage", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("@page", razorFallback, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ThreeDimensionalPrintingContent)\"", razorFallback, StringComparison.Ordinal);

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
                "CncMachiningPage.razor",
                "CustomManufacturingPage.razor",
                "GuidelinesPage.razor",
                "KnowledgeIndexPage.razor",
                "ServicesPage.razor",
                "SpecificationsIndexPage.razor",
                "ThreeDimensionalPrintingPage.razor",
                "ThreeDimensionalPrintingSpecificationPage.razor",
                "ThreeDimensionalScanningPage.razor",
                "WorkflowPage.razor"
            ],
            routedPages);
    }

    [Theory]
    [InlineData(
        "en",
        "3D Printing Services Bangkok & Nonthaburi | Instant Online Quote",
        "Order FDM and resin 3D printed parts in engineering materials. Compare material uses, prepare files, and upload CAD for instant 3D printing pricing.",
        "Professional 3D Printing for Prototypes and Functional Parts",
        "3D printing service Thailand, 3D print price, order 3D print Bangkok, FDM printing, resin printing")]
    [InlineData(
        "th",
        "รับพิมพ์ 3D และรับปริ้น 3D กรุงเทพและนนทบุรี | ประเมินราคาออนไลน์",
        "MALIEV รับพิมพ์ 3D ด้วยระบบ FDM และเรซินสำหรับต้นแบบและชิ้นงานใช้งานจริง เลือกวัสดุ อัปโหลดไฟล์ และประเมินราคาออนไลน์",
        "รับพิมพ์ 3D และรับปริ้น 3D สำหรับต้นแบบและชิ้นงานใช้งานจริง",
        "รับปริ้น 3D, ปริ้น 3D ราคา, ร้านปริ้น 3D, สั่งพิมพ์ 3 มิติ, พิมพ์เรซิน")]
    public async Task Route_RendersCompleteLocalizedStaticDocument(
        string culture,
        string title,
        string description,
        string heading,
        string keywords)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/3d-printing?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"keywords\" content=\"{keywords}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"preload\" as=\"image\" href=\"/src/images/services/printing/printing-hero.webp\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"three-dimensional-printing-content\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/InstantQuotation/3D-Printing\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Quotation?item=3D-Printing\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/services/3d-printing?culture=en", "https://www.maliev.com/services/3d-printing?culture=en", "https://www.maliev.com/services/3d-printing")]
    [InlineData("th", "https://www.maliev.com/services/3d-printing", "https://www.maliev.com/services/3d-printing?culture=en", "https://www.maliev.com/services/3d-printing")]
    public async Task Route_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/3d-printing?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "3D Printing Services", "How much does 3D printing cost?")]
    [InlineData("th", "บริการรับพิมพ์ 3D", "พิมพ์ 3D ราคาเท่าไร?")]
    public async Task Route_PreservesServiceFaqAndBreadcrumbStructuredData(
        string culture,
        string serviceName,
        string faqQuestion)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/3d-printing?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var documents = StructuredDataRegex().Matches(source)
            .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var service = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "Service");
        using var faq = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "FAQPage");
        Assert.Contains(documents, document => document.RootElement.GetProperty("@type").GetString() == "BreadcrumbList");
        Assert.Equal(serviceName, service.RootElement.GetProperty("name").GetString());
        Assert.Equal("3D Printing", service.RootElement.GetProperty("serviceType").GetString());
        Assert.Equal(3, faq.RootElement.GetProperty("mainEntity").GetArrayLength());
        Assert.Equal(faqQuestion, faq.RootElement.GetProperty("mainEntity")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/services/3d-printing?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/services/3d-printing?culture=en");
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
        using var response = await client.GetAsync("/services/3d-printing?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>3D Printing Services Bangkok &amp; Nonthaburi | Instant Online Quote</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"three-dimensional-printing-content\"", source, StringComparison.Ordinal);
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
