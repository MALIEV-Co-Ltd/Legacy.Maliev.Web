using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class CustomManufacturingStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public CustomManufacturingStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheCustomManufacturingRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "CustomManufacturingPage.razor"));
        var content = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "CustomManufacturingContent.razor"));
        var razorFallback = File.ReadAllText(Path.Combine(web, "Pages", "Services", "Custom-Manufacturing.cshtml"));

        Assert.Contains("BlazorRouting:Services", program, StringComparison.Ordinal);
        Assert.Contains("/Services/Custom-Manufacturing", program, StringComparison.Ordinal);
        Assert.Contains("@page \"/services/custom-manufacturing\"", route, StringComparison.Ordinal);
        Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
        Assert.Contains("<PublicServiceStructuredData", route, StringComparison.Ordinal);
        Assert.Contains("FAQPage", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("@page", razorFallback, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(CustomManufacturingContent)\"", razorFallback, StringComparison.Ordinal);

        var routedPages = Directory.EnumerateFiles(
                Path.Combine(web, "Components"),
                "*.razor",
                SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Any(line => line.TrimStart().StartsWith("@page ", StringComparison.Ordinal)))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["AboutPage.razor", "AccessDeniedPage.razor", "AccountIndexPage.razor", "CareerDetailPage.razor", "CareerIndexPage.razor", "ChangeEmailConfirmationPage.razor", "CncMachiningPage.razor", "CncMachiningSpecificationPage.razor", "ContactPage.razor", "CustomManufacturingPage.razor", "EmailConfirmationPage.razor", "ErrorPage.razor", "ForgotPasswordPage.razor", "GuidelinesPage.razor", "HomePage.razor", "InstantQuotationPage.razor", "KnowledgeIndexPage.razor", "LegalPage.razor", "LoginPage.razor", "LogoutPage.razor", "MemberAccountIndexPage.razor", "MemberAddressPage.razor", "MemberChangeEmailPage.razor", "MemberChangePasswordPage.razor", "MemberOrderDetailPage.razor", "MemberOrderHistoryPage.razor", "MemberOrdersIndexPage.razor", "MemberOverviewPage.razor", "MemberProfilePage.razor", "MemberQuotationDetailPage.razor", "MemberQuotationsIndexPage.razor", "NonDisclosureAgreementPage.razor", "PrivacyPolicyPage.razor", "QuotationPage.razor", "ResetPasswordPage.razor", "ServicesPage.razor", "SignupPage.razor", "SocialMediaPage.razor", "SpecificationsIndexPage.razor", "TermsConditionsPage.razor", "ThreeDimensionalPrintingPage.razor", "ThreeDimensionalPrintingSpecificationPage.razor", "ThreeDimensionalScanningPage.razor", "ThreeDimensionalScanningSpecificationPage.razor", "WorkflowPage.razor"],
            routedPages);
    }

    [Theory]
    [InlineData(
        "en",
        "Custom Part Manufacturing with CNC and 3D Printing | MALIEV",
        "Not sure whether CNC machining, 3D printing, or 3D scanning fits your part? Send the drawing or sample, material, quantity, critical features, and intended use for a manufacturing review.",
        "Custom Part Manufacturing: Start with Your Drawing, Material, Quantity, and Use",
        "custom part manufacturing Thailand, made to drawing, CNC or 3D printing, reverse engineering")]
    [InlineData(
        "th",
        "รับผลิตชิ้นงานตามแบบด้วย CNC และ 3D Printing | MALIEV",
        "หากยังไม่แน่ใจว่าควรใช้ CNC, 3D Printing หรือ 3D Scanning ส่งแบบหรือตัวอย่าง วัสดุ จำนวน จุดสำคัญ และการใช้งาน เพื่อให้ MALIEV ตรวจสอบเส้นทางประเมินที่เหมาะสม",
        "รับผลิตชิ้นงานตามแบบ: เริ่มจากแบบ วัสดุ จำนวน และการใช้งาน",
        "รับผลิตชิ้นงานตามแบบ, ผลิตชิ้นส่วนตามแบบ, รับทำชิ้นงาน, CNC หรือ 3D Printing")]
    public async Task Route_RendersCompleteLocalizedStaticDocument(
        string culture,
        string title,
        string description,
        string heading,
        string keywords)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/custom-manufacturing?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"keywords\" content=\"{keywords}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"custom-manufacturing-content\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Quotation?item=custom-manufacturing\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services/CNC-Machining\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services/3D-Printing\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services/3D-Scanning\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/services/custom-manufacturing?culture=en", "https://www.maliev.com/services/custom-manufacturing?culture=en", "https://www.maliev.com/services/custom-manufacturing")]
    [InlineData("th", "https://www.maliev.com/services/custom-manufacturing", "https://www.maliev.com/services/custom-manufacturing?culture=en", "https://www.maliev.com/services/custom-manufacturing")]
    public async Task Route_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/custom-manufacturing?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Custom Part Manufacturing", "Which manufacturing process should I choose?")]
    [InlineData("th", "รับผลิตชิ้นงานตามแบบ", "ควรเลือกกระบวนการผลิตแบบใด?")]
    public async Task Route_PreservesServiceAndFaqStructuredData(
        string culture,
        string serviceName,
        string faqQuestion)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/custom-manufacturing?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var documents = StructuredDataRegex().Matches(source)
            .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var service = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "Service");
        using var faq = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "FAQPage");
        Assert.Equal(serviceName, service.RootElement.GetProperty("name").GetString());
        Assert.Equal("Custom Manufacturing", service.RootElement.GetProperty("serviceType").GetString());
        Assert.Equal(4, faq.RootElement.GetProperty("mainEntity").GetArrayLength());
        Assert.Equal(faqQuestion, faq.RootElement.GetProperty("mainEntity")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/services/custom-manufacturing?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/services/custom-manufacturing?culture=en");
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
        using var response = await client.GetAsync("/services/custom-manufacturing?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Custom Part Manufacturing with CNC and 3D Printing | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"custom-manufacturing-content\"", source, StringComparison.Ordinal);
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
