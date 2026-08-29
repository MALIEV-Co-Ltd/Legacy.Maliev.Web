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
            .Where(path => !new[]
            {
                "ThreeDimensionalDesignPage.razor",
                "SiliconeCastingPage.razor",
                "LowVolumeInjectionMoldingPage.razor"
            }.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["AboutPage.razor", "AccessDeniedPage.razor", "AccountIndexPage.razor", "CareerDetailPage.razor", "CareerIndexPage.razor", "ChangeEmailConfirmationPage.razor", "CncMachiningPage.razor", "CncMachiningSpecificationPage.razor", "ContactPage.razor", "CustomManufacturingPage.razor", "EmailConfirmationPage.razor", "ErrorPage.razor", "FinishingAndColorPage.razor", "ForgotPasswordPage.razor", "GuidelinesPage.razor", "HomePage.razor", "InstantQuotationPage.razor", "KnowledgeIndexPage.razor", "LegalPage.razor", "LoginPage.razor", "LogoutPage.razor", "MemberAccountIndexPage.razor", "MemberAddressPage.razor", "MemberChangeEmailPage.razor", "MemberChangePasswordPage.razor", "MemberCreatePasswordPage.razor", "MemberOrderCreationPage.razor", "MemberOrderDetailPage.razor", "MemberOrderHistoryPage.razor", "MemberOrdersIndexPage.razor", "MemberOverviewPage.razor", "MemberProfilePage.razor", "MemberQuotationDetailPage.razor", "MemberQuotationsIndexPage.razor", "NonDisclosureAgreementPage.razor", "PrivacyPolicyPage.razor", "QuotationPage.razor", "ResetPasswordPage.razor", "ServicesPage.razor", "SignupPage.razor", "SocialMediaPage.razor", "SpecificationsIndexPage.razor", "TermsConditionsPage.razor", "ThreeDimensionalPrintingPage.razor", "ThreeDimensionalPrintingSpecificationPage.razor", "ThreeDimensionalScanningPage.razor", "ThreeDimensionalScanningSpecificationPage.razor", "WorkflowPage.razor"],
            routedPages);
    }

    [Theory]
    [InlineData(
        "en",
        "CNC Machining Services Across Thailand | One-Off and Production Parts",
        "CNC milling and turning for customers across Thailand, from one-off parts and prototypes to jigs and repeat production. Send CAD and drawings for a quote.",
        "Precision CNC Machining for One-Off and Production Parts",
        "CNC machining Thailand, CNC aluminum, CNC one piece, machine shop Bangkok, CNC Nonthaburi")]
    [InlineData(
        "th",
        "รับงาน CNC ตามแบบทั่วประเทศไทย | อะลูมิเนียมชิ้นเดียวถึงงานผลิต",
        "MALIEV รับ CNC อะลูมิเนียมชิ้นเดียวและผลิตชิ้นงานตามไฟล์ CAD และแบบงาน สำหรับลูกค้าทั่วประเทศไทย ส่ง CAD แบบ 2D วัสดุ และจำนวนเพื่อให้ตรวจสอบและเสนอราคา",
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
        Assert.Contains("data-migration-component=\"service-pricing\"", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "เริ่มต้นประมาณ 2,500 บาท" : "Starts at approximately THB 2,500", source, StringComparison.Ordinal);
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
    [InlineData("th", "บริการรับงาน CNC ตามแบบ", "รับ CNC อะลูมิเนียมชิ้นเดียวได้หรือไม่?")]
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
        Assert.Equal(7, faq.RootElement.GetProperty("mainEntity").GetArrayLength());
        Assert.Equal(faqQuestion, faq.RootElement.GetProperty("mainEntity")[0].GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(
        "en",
        "What general tolerance applies to CNC parts?",
        "Unless the drawing or quotation specifies otherwise, ISO 2768-1 tolerance class m is the default and applies only to linear and angular dimensions without individual tolerance indications.",
        "Drawing-specific requirements override the general tolerance.",
        "Critical or tighter tolerances require engineering review and explicit confirmation in the quotation before production.")]
    [InlineData(
        "th",
        "งาน CNC ใช้ค่าคลาดเคลื่อนทั่วไปใด?",
        "หากแบบงานหรือใบเสนอราคาไม่ได้ระบุเป็นอย่างอื่น ISO 2768-1 ระดับ m เป็นค่าคลาดเคลื่อนทั่วไปเริ่มต้น และใช้เฉพาะขนาดเชิงเส้นและเชิงมุมที่ไม่ได้กำหนดค่าคลาดเคลื่อนเป็นรายจุด",
        "ข้อกำหนดเฉพาะในแบบงานมีผลเหนือกว่าค่าคลาดเคลื่อนทั่วไป",
        "ค่าคลาดเคลื่อนสำคัญหรือแคบกว่าต้องผ่านการตรวจสอบทางวิศวกรรมและยืนยันอย่างชัดเจนในใบเสนอราคาก่อนผลิต")]
    public async Task Route_PublishesTheReviewedGeneralToleranceContract(
        string culture,
        string question,
        string scope,
        string precedence,
        string confirmation)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/cnc-machining?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(question, source, StringComparison.Ordinal);
        Assert.Contains(scope, source, StringComparison.Ordinal);
        Assert.Contains(precedence, source, StringComparison.Ordinal);
        Assert.Contains(confirmation, source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISO 2763", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISO 9001", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ISO 13485", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AS9100", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IATF", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceVisibleCncCopyAndPricingRemainExact()
    {
        var root = FindRepositoryRoot();
        var content = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "CncMachiningContent.razor"));
        var pricing = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Shared", "ServicePricing.razor"));

        Assert.Contains("We accept one-off parts when the geometry, material, and project scope are feasible.", content, StringComparison.Ordinal);
        Assert.Contains("รับงาน CNC อะลูมิเนียมชิ้นเดียวเมื่อรูปทรง วัสดุ และขอบเขตงานผลิตได้จริง", content, StringComparison.Ordinal);
        Assert.Contains("รวมงานอะลูมิเนียมชิ้นเดียว", content, StringComparison.Ordinal);
        Assert.Contains("ส่ง CAD แบบ 2D ระบุวัสดุและจำนวนเพื่อให้ตรวจสอบก่อนเสนอราคา", content, StringComparison.Ordinal);
        Assert.Contains("CNC machining starts at THB 2,500.", content, StringComparison.Ordinal);
        Assert.Contains("งาน CNC เริ่มต้น 2,500 บาท", content, StringComparison.Ordinal);
        Assert.Contains("What file formats should I send?", content, StringComparison.Ordinal);
        Assert.Contains("แนะนำไฟล์ STEP หรือไฟล์ solid CAD พร้อมแบบ PDF", content, StringComparison.Ordinal);
        Assert.Contains("Black oxide for steel", pricing, StringComparison.Ordinal);
        Assert.Contains("Starts at approximately THB 1,500 per batch", pricing, StringComparison.Ordinal);
        Assert.Contains("รมดำสำหรับชิ้นงานเหล็ก", pricing, StringComparison.Ordinal);
        Assert.Contains("เริ่มต้นประมาณ 1,500 บาทต่อชุดงาน", pricing, StringComparison.Ordinal);
        Assert.Contains("Standard aluminium anodizing", pricing, StringComparison.Ordinal);
        Assert.Contains("Starts at approximately THB 2,500 per lot; depends on part size and processing complexity", pricing, StringComparison.Ordinal);
        Assert.Contains("อะโนไดซ์อะลูมิเนียมมาตรฐาน", pricing, StringComparison.Ordinal);
        Assert.Contains("เริ่มต้นประมาณ 2,500 บาทต่อชุด ขึ้นอยู่กับขนาดชิ้นงานและความยากในการชุบ", pricing, StringComparison.Ordinal);
        Assert.Contains("Hard or custom-colour anodizing", pricing, StringComparison.Ordinal);
        Assert.Contains("Plan approximately THB 6,000–15,000+ per lot", pricing, StringComparison.Ordinal);
        Assert.Contains("ฮาร์ดอะโนไดซ์หรือสีพิเศษ", pricing, StringComparison.Ordinal);
        Assert.Contains("วางแผนประมาณ 6,000–15,000+ บาทต่อชุดงาน", pricing, StringComparison.Ordinal);
        Assert.Contains("total coated area", pricing, StringComparison.Ordinal);
        Assert.Contains("coating thickness", pricing, StringComparison.Ordinal);
        Assert.Contains("surface preparation", pricing, StringComparison.Ordinal);
        Assert.DoesNotContain("Production quantities", pricing, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier", pricing, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("โรงงานชุบ", pricing, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Starts at approximately THB 1,500 per batch", "Starts at approximately THB 2,500 per lot; depends on part size and processing complexity", "Plan approximately THB 6,000–15,000+ per lot")]
    [InlineData("th", "เริ่มต้นประมาณ 1,500 บาทต่อชุดงาน", "เริ่มต้นประมาณ 2,500 บาทต่อชุด ขึ้นอยู่กับขนาดชิ้นงานและความยากในการชุบ", "วางแผนประมาณ 6,000–15,000+ บาทต่อชุดงาน")]
    public async Task Route_RendersExactLocalizedCncFinishingEstimates(
        string culture,
        string blackOxide,
        string standardAnodizing,
        string customAnodizing)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/cnc-machining?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(blackOxide, source, StringComparison.Ordinal);
        Assert.Contains(standardAnodizing, source, StringComparison.Ordinal);
        Assert.Contains(customAnodizing, source, StringComparison.Ordinal);
        Assert.DoesNotContain("THB 4,500–8,000", source, StringComparison.Ordinal);
        Assert.DoesNotContain("4,500–8,000 บาท", source, StringComparison.Ordinal);
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
        Assert.Contains("<title>CNC Machining Services Across Thailand | One-Off and Production Parts</title>", source, StringComparison.Ordinal);
        Assert.Contains("ISO 2768-1 tolerance class m", source, StringComparison.Ordinal);
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
