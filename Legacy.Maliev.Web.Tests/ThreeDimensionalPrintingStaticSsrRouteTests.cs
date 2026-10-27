using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class ThreeDimensionalPrintingStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ThreeDimensionalPrintingStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
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
            .Where(path => !new[]
            {
                "ThreeDimensionalDesignPage.razor",
                "SiliconeCastingPage.razor",
                "LowVolumeInjectionMoldingPage.razor"
            }.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .LegacyPagesAfterVerifyingDocuments()
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "AboutPage.razor",
                "AccessDeniedPage.razor",
                "AccountIndexPage.razor",
                "CareerDetailPage.razor",
                "CareerIndexPage.razor",
                "ChangeEmailConfirmationPage.razor",
                "CncCuttingToolsPage.razor",
                "CncFeedsAndSpeedsPage.razor",
                "CncGeometryAndWorkholdingPage.razor",
                "CncHolesAndTolerancesPage.razor",
                "CncMachiningPage.razor",
                "CncMachiningProcessesPage.razor",
                "CncMachiningSpecificationPage.razor",
                "ContactPage.razor",
                "CustomManufacturingPage.razor",
                "EmailConfirmationPage.razor",
                "ErrorPage.razor",
                "FinishingAndColorPage.razor",
                "FinishingHeatTreatmentPage.razor",
                "FinishingSurfaceFinishesPage.razor",
                "ForgotPasswordPage.razor",
                "GuidelinesPage.razor",
                "HomePage.razor",
                "InspectionMeasuringPartsPage.razor",
                "InstantQuotationPage.razor",
                "KnowledgeIndexPage.razor",
                "LegalPage.razor",
                "LineContactPage.razor",
                "LoginPage.razor",
                "LogoutPage.razor",
                "ManufacturingProcessSelectionPage.razor",
                "MaterialsEngineeringPlasticsPage.razor",
                "MaterialsMetalsPage.razor",
                "MaterialsSelectionPage.razor",
                "MemberAccountIndexPage.razor",
                "MemberAddressPage.razor",
                "MemberChangeEmailPage.razor",
                "MemberChangePasswordPage.razor",
                "MemberCreatePasswordPage.razor",
                "MemberOrderCreationPage.razor",
                "MemberOrderDetailPage.razor",
                "MemberOrderHistoryPage.razor",
                "MemberOrdersIndexPage.razor",
                "MemberOverviewPage.razor",
                "MemberProfilePage.razor",
                "MemberQuotationDetailPage.razor",
                "MemberQuotationsIndexPage.razor",
                "NoWeaponsPage.razor",
                "NonDisclosureAgreementPage.razor",
                "PrivacyPolicyPage.razor",
                "QuotationPage.razor",
                "ResetPasswordPage.razor",
                "ServicesPage.razor",
                "SignupPage.razor",
                "SocialMediaPage.razor",
                "SpecificationsIndexPage.razor",
                "TermsConditionsPage.razor",
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
        "3D Printing Service Thailand | Instant FDM & Resin Pricing | MALIEV",
        "3D printing service in Thailand for prototypes and production parts. Upload a file for instant FDM and resin pricing or request an engineering review.",
        "3D Printing Service in Thailand for Prototypes and Production Parts",
        "MALIEV provides 3D printing services across Thailand. Upload FDM or resin parts for an instant estimate, or request an engineering review for MJF, SLS, SLM, DMLS, and safety-critical parts so process, material, finishing, and inspection can be confirmed per project.",
        "3D printing service Thailand, 3D print price, order 3D print Bangkok, FDM printing, resin printing, supported 3D file formats")]
    [InlineData(
        "th",
        "รับปริ้น 3D รับพิมพ์ 3 มิติ | ประเมินราคาออนไลน์ | MALIEV",
        "รับปริ้น 3D และรับพิมพ์ 3 มิติ ตั้งแต่ 1 ชิ้น อัปโหลด STL, STEP, OBJ หรือ 3MF ประเมินราคา FDM และเรซิ่นออนไลน์ ดูตัวอย่างชิ้นงานผลิตจริงจาก MALIEV",
        "รับปริ้น 3D และรับพิมพ์ 3 มิติ อัปโหลดไฟล์ประเมินราคา",
        "MALIEV รับพิมพ์ 3D ด้วยระบบ FDM และเรซิ่น พร้อมประเมินราคาออนไลน์ สำหรับงาน MJF, SLS, SLM, DMLS หรือชิ้นงานที่มีข้อกำหนดสำคัญ ให้ส่งไฟล์เพื่อให้วิศวกรยืนยันกระบวนการ วัสดุ การเก็บผิว และการตรวจสอบเป็นรายโปรเจ็ค",
        "รับพิมพ์ 3D และรับปริ้น 3D กรุงเทพและนนทบุรี, รับพิมพ์งาน 3D, รับปริ้น 3D ราคา, ร้านปริ้น 3D, สั่งพิมพ์ 3 มิติ, พิมพ์เรซิ่น")]
    public async Task Route_RendersCompleteLocalizedStaticDocument(
        string culture,
        string title,
        string description,
        string heading,
        string lead,
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
        Assert.Contains($">{lead}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"three-dimensional-printing-content\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"service-pricing\"", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "เริ่มต้นประมาณ 300 บาท" : "Starts at approximately THB 300", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "เริ่มต้นประมาณ 500 บาท" : "Starts at approximately THB 500", source, StringComparison.Ordinal);
        Assert.Contains("id=\"printing-engineering-review\"", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ประเมินงานพิมพ์อุตสาหกรรมและโลหะ" : "Industrial & metal engineering review", source, StringComparison.Ordinal);
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
    [InlineData("th", "บริการรับพิมพ์ 3D", "รับพิมพ์ 3D ราคาเท่าไร และประเมินอย่างไร?")]
    public async Task Route_PreservesServiceFaqAndBreadcrumbStructuredData(
        string culture,
        string serviceName,
        string faqQuestion)
    {
        var expectedSchemaAnswer = culture == "th"
            ? "ราคาเริ่มต้นและราคาสุดท้ายขึ้นกับวัสดุ ปริมาตร เวลา ซัพพอร์ต ทิศทาง ความเสี่ยง การเก็บงาน และจำนวน อัปโหลดไฟล์จริง เลือกวัสดุหรือกระบวนการ และแจ้งจำนวนในหน้า Instant Quotation เพื่อประเมินราคา หากยังไม่มีไฟล์ให้ขอวิศวกรช่วยเลือกกระบวนการ"
            : "Cost depends on material, occupied volume, print time, support, orientation, risk, finishing, and quantity. Upload the model to MALIEV Instant Quotation for an estimate.";
        var expectedVisibleAnswer = culture == "th"
            ? "ราคาเริ่มต้นของงาน FDM และเรซิ่นแสดงอยู่ในส่วนราคาเริ่มต้นของหน้านี้ ราคาสุดท้ายขึ้นกับวัสดุ ปริมาตร เวลา ซัพพอร์ต ทิศทาง ความเสี่ยง การเก็บงาน และจำนวน อัปโหลดไฟล์จริง เลือกวัสดุหรือกระบวนการ และแจ้งจำนวนในหน้า Instant Quotation เพื่อประเมินราคา หากยังไม่มีไฟล์ให้ขอวิศวกรช่วยเลือกกระบวนการ"
            : "Indicative starting prices for FDM and resin are listed in the pricing section on this page. The final price depends on material, occupied volume, print time, support, orientation, risk, finishing, and quantity. Upload the actual model to Instant Quotation for an estimate.";

        foreach (var useBlazorRoute in new[] { true, false })
        {
            using var routeFactory = factory.WithWebHostBuilder(builder =>
                builder.UseSetting("BlazorRouting:Services", useBlazorRoute.ToString()));
            using var client = CreateClient(routeFactory);
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
            Assert.Equal(8, faq.RootElement.GetProperty("mainEntity").GetArrayLength());
            Assert.Equal(faqQuestion, faq.RootElement.GetProperty("mainEntity")[0].GetProperty("name").GetString());
            Assert.Equal(expectedSchemaAnswer, faq.RootElement.GetProperty("mainEntity")[0].GetProperty("acceptedAnswer").GetProperty("text").GetString());
            Assert.Matches($"<summary>\\s*{Regex.Escape(faqQuestion)}\\s*</summary>\\s*<p>\\s*{Regex.Escape(expectedVisibleAnswer)}\\s*</p>", source);
        }
    }

    [Fact]
    public void SourceVisiblePrintingCopyAndPricingRemainExact()
    {
        var root = FindRepositoryRoot();
        var content = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor"));
        var pricing = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Shared", "ServicePricing.razor"));

        Assert.Contains("Compare typical starting ranges for a well-designed part. These are not guarantees; send the drawing so we can confirm the critical dimensions before production.", content, StringComparison.Ordinal);
        Assert.Contains("เปรียบเทียบค่าความคลาดเคลื่อนเริ่มต้นโดยทั่วไปสำหรับชิ้นงานที่ออกแบบมาเหมาะสม ค่านี้ไม่ใช่การรับประกัน กรุณาส่งแบบเพื่อให้เรายืนยันขนาดสำคัญก่อนผลิต", content, StringComparison.Ordinal);
        Assert.Contains("drawing — we can ream, tap, or machine", content, StringComparison.Ordinal);
        Assert.Contains("Indicative starting prices for FDM and resin are listed in the pricing section on this page.", content, StringComparison.Ordinal);
        Assert.Contains("ราคาเริ่มต้นของงาน FDM และเรซิ่นแสดงอยู่ในส่วนราคาเริ่มต้นของหน้านี้", content, StringComparison.Ordinal);
        Assert.Contains("Which material is strongest?", content, StringComparison.Ordinal);
        Assert.Contains("วัสดุใดแข็งแรงที่สุด?", content, StringComparison.Ordinal);
        Assert.Contains("There is no single strongest choice for every condition.", content, StringComparison.Ordinal);
        Assert.Contains("Can you print only one part?", content, StringComparison.Ordinal);
        Assert.Contains("Multiple identical parts may use the machine more efficiently", content, StringComparison.Ordinal);
        Assert.Contains("Practical prototypes, fixtures, and larger functional parts", pricing, StringComparison.Ordinal);
        Assert.Contains("เหมาะกับต้นแบบ จิ๊ก และชิ้นงานใช้งานจริงขนาดใหญ่", pricing, StringComparison.Ordinal);
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
        Assert.Contains("<title>3D Printing Service Thailand | Instant FDM &amp; Resin Pricing | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("3D Printing Service in Thailand for Prototypes and Production Parts", source, StringComparison.Ordinal);
        Assert.Contains("id=\"printing-engineering-review\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"three-dimensional-printing-content\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("\"@type\":\"FAQPage\"", WebUtility.HtmlDecode(source), StringComparison.Ordinal);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(false, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "th")]
    public async Task Route_PreservesSourceFinishingWorkflowLinkAcrossRenderers(bool useBlazorRoute, string culture)
    {
        await using var application = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Services", useBlazorRoute.ToString()));
        using var client = CreateClient(application);
        using var response = await client.GetAsync($"/services/3d-printing?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var main = Assert.Single(Regex.Matches(html,
            "<main\\b(?=[^>]*data-migration-component=\"three-dimensional-printing-content\")[^>]*>(?<body>[\\s\\S]*?)</main>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Equal(useBlazorRoute, main.Value.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var workflow = Assert.Single(Regex.Matches(main.Groups["body"].Value,
            "<section\\b(?=[^>]*id=\"printing-workflow\")[^>]*>(?<body>[\\s\\S]*?)</section>",
            RegexOptions.CultureInvariant).Cast<Match>());
        var steps = Regex.Matches(workflow.Groups["body"].Value, "<li>(?<body>[\\s\\S]*?)</li>", RegexOptions.CultureInvariant)
            .Cast<Match>().ToArray();
        Assert.Equal(4, steps.Length);
        var finishStep = steps[3].Groups["body"].Value;
        Assert.Contains(culture == "th" ? "<h3>พิมพ์ ประกอบ เก็บผิว และจัดส่ง</h3>" : "<h3>Print, join, finish, and deliver</h3>",
            finishStep, StringComparison.Ordinal);
        var link = Assert.Single(Regex.Matches(finishStep,
            "<a href=\"(?<href>/services/finishing-and-color)\">(?<copy>[^<]+)</a>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Equal(new Uri(client.BaseAddress!, "/services/finishing-and-color"), new Uri(client.BaseAddress!, link.Groups["href"].Value));
        Assert.Equal(culture == "th" ? "วิธีตกลงเรื่องสี ความเงา และรอยต่อ" : "How we agree colour, sheen, and seams",
            link.Groups["copy"].Value);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task FinishingRoute_PreservesSourcePricingActionAndImageDescriptions(string culture)
    {
        // Finishing has a component route only; retained printing is covered separately.
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/services/finishing-and-color?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var main = Assert.Single(Regex.Matches(html,
            "<main\\b(?=[^>]*data-migration-component=\"finishing-and-color-content\")[^>]*>(?<body>[\\s\\S]*?)</main>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", main.Value, StringComparison.Ordinal);
        var hero = Assert.Single(Regex.Matches(main.Groups["body"].Value,
            "<section\\b(?=[^>]*aria-labelledby=\"finishing-title\")[^>]*>(?<body>[\\s\\S]*?)</section>",
            RegexOptions.CultureInvariant).Cast<Match>());
        var pricingLink = Assert.Single(Regex.Matches(hero.Groups["body"].Value,
            "<a\\b(?=[^>]*class=\"service-button service-button-secondary\")(?=[^>]*href=\"(?<href>[^\"]+)\")[^>]*>(?<copy>[^<]+)</a>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Equal(new Uri(client.BaseAddress!, "/InstantQuotation/3D-Printing"), new Uri(client.BaseAddress!, pricingLink.Groups["href"].Value));
        Assert.Equal(culture == "th" ? "ประเมินราคาพิมพ์ 3D ทันที" : "Get instant 3D pricing", pricingLink.Groups["copy"].Value);

        var standards = Assert.Single(Regex.Matches(main.Groups["body"].Value,
            "<section\\b(?=[^>]*id=\"finishing-standards\")[^>]*>(?<body>[\\s\\S]*?)</section>",
            RegexOptions.CultureInvariant).Cast<Match>());
        foreach (var section in new[] { hero, standards })
        {
            var image = Assert.Single(Regex.Matches(section.Groups["body"].Value,
                "<img\\b(?=[^>]*src=\"/src/images/services/printing/printing-finish-color-approval.webp\")(?=[^>]*alt=\"(?<alt>[^\"]*)\")[^>]*>",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Equal(culture == "th" ? "ช่างกำลังเทียบสีชิ้นงานพิมพ์ 3 มิติกับแผ่นเทียบสีและภาพอ้างอิง" : "Technician comparing a 3D-printed part with color swatches and a reference image",
                image.Groups["alt"].Value);
        }
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
