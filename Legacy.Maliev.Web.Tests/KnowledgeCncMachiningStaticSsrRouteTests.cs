using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class KnowledgeCncMachiningStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public KnowledgeCncMachiningStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
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
        "Design decisions that make CNC parts easier to manufacture",
        "CNC machining design guide",
        "Files and quotation checklist",
        "Supply a solid CAD model such as STEP and a readable dimensioned drawing for each part. Identify units, part number and revision consistently across both files. A model describes nominal shape; it does not reliably communicate every thread, fit, cosmetic requirement or inspection condition. If model and drawing disagree, identify which requirement governs before machining begins.",
        "Thin walls and floors: consider support as well as thickness",
        "Wall height, thickness, unsupported length and material all influence how a feature responds during cutting. A tall isolated wall behaves differently from a short wall tied into surrounding ribs. Thin floors can also bend under cutting or clamping loads. There is no single minimum wall thickness that establishes whether every material and geometry can be machined successfully.",
        "Internal radii and pockets: leave room for the cutter",
        "A rotating end mill leaves a radius in an internal vertical corner. Making that radius smaller usually changes the required tool diameter, reach or number of operations. When the cutter nearly fills a corner, engagement increases and the cut can become less stable. Give internal corners the largest radius that still provides clearance for the mating component and preserves required strength.",
        "Continue reading")]
    [InlineData(
        "th",
        "ข้อแนะนำการออกแบบชิ้นงาน CNC | MALIEV",
        "โมเดล solid 3 มิติกำหนดรูปทรงและขนาดพิกัด ส่วนแบบงานสื่อสารข้อกำหนดที่รูปทรงเพียงอย่างเดียวระบุไม่ได้",
        "ออกแบบชิ้นงาน CNC ให้ผลิตได้ตรงความต้องการ",
        "คู่มือออกแบบชิ้นงานสำหรับการผลิตด้วย CNC",
        "ไฟล์และรายการข้อมูลสำหรับขอราคา",
        "ส่งโมเดล solid CAD เช่น STEP พร้อมแบบระบุขนาดที่อ่านได้สำหรับชิ้นงานแต่ละรายการ ระบุหน่วย หมายเลขชิ้นงาน และรีวิชันให้ตรงกันทั้งสองไฟล์ โมเดลบอกรูปทรงตามแบบ แต่ไม่สามารถสื่อสารข้อกำหนดเกลียว พิกัดสวม ผิวที่ต้องการความสวยงาม หรือสภาวะการตรวจสอบได้ครบถ้วน หากโมเดลกับแบบไม่ตรงกัน ต้องตกลงว่าจะยึดข้อกำหนดใดก่อนเริ่มผลิต",
        "ผนังและพื้นบาง: พิจารณาจุดรองรับร่วมกับความหนา",
        "ความสูง ความหนา ความยาวช่วงที่ไม่มีตัวรองรับ และวัสดุ ล้วนมีผลต่อการตอบสนองของฟีเจอร์ขณะตัด ผนังสูงที่อยู่เดี่ยวมีพฤติกรรมต่างจากผนังเตี้ยที่เชื่อมกับครีบรอบข้าง พื้นบางอาจแอ่นจากแรงตัดหรือแรงจับยึดได้เช่นกัน จึงไม่มีค่าความหนาผนังต่ำสุดค่าเดียวที่ใช้ตัดสินการผลิตได้กับทุกวัสดุและทุกรูปทรง",
        "รัศมีมุมในและโพรง: เผื่อพื้นที่ให้เครื่องมือตัด",
        "ดอกเอ็นมิลหมุนตัดจะทิ้งรัศมีไว้ที่มุมในแนวตั้ง การลดรัศมีมักทำให้ต้องเปลี่ยนขนาดดอก ระยะยื่น หรือจำนวนขั้นตอน เมื่อดอกเกือบเต็มมุม พื้นที่สัมผัสตัดจะเพิ่มขึ้นและอาจทำให้การตัดไม่เสถียร ควรกำหนดรัศมีมุมในให้ใหญ่ที่สุดเท่าที่ชิ้นส่วนคู่ประกอบยังสวมได้และยังคงความแข็งแรงตามต้องการ",
        "อ่านต่อ")]
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
        Assert.Contains($">{serviceLink}<", source, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(source, "<section\\b[^>]*class=\"docs-section\"", RegexOptions.CultureInvariant).Count);
        Assert.Contains("id=\"thin-walls\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"radii-pockets\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/knowledges/cnc/geometry-and-workholding\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/knowledges/cnc/holes-and-tolerances\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/knowledges/cnc/machining-processes\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/knowledges/finishing/surface-finishes\"", source, StringComparison.Ordinal);

        // The source-final overview links to the detailed article. Preserve the
        // former wall and corner contract against its actual new HTTP route.
        using var detailResponse = await client.GetAsync($"/knowledges/cnc/geometry-and-workholding?culture={culture}");
        var geometry = WebUtility.HtmlDecode(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Contains($">{secondStep}<", geometry, StringComparison.Ordinal);
        Assert.Contains($">{secondStepDescription}<", geometry, StringComparison.Ordinal);
        Assert.Contains($">{thirdStep}<", geometry, StringComparison.Ordinal);
        Assert.Contains($">{thirdStepDescription}<", geometry, StringComparison.Ordinal);
        Assert.Contains("id=\"thin-walls\"", geometry, StringComparison.Ordinal);
        Assert.Contains("id=\"radii-pockets\"", geometry, StringComparison.Ordinal);
        Assert.Contains("src=\"/src/images/knowledge/cnc-corner-clearance.svg\"", geometry, StringComparison.Ordinal);
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
        Assert.Contains("route-knowledge.js", source, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Escape'", File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "knowledge-navigation.js")), StringComparison.Ordinal);
        Assert.Contains("route-knowledge.js", source, StringComparison.Ordinal);
        Assert.Contains("openButton.focus()", File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "knowledge-navigation.js")), StringComparison.Ordinal);
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
    [InlineData(
        "en",
        "Finishing and anodizing: design the final condition",
        "Anodic coating thickness includes both converted base material and outward growth. The Aluminum Anodizers Council gives approximately half penetration and half buildup as generally accepted hardcoat guidance; actual pretreatment and coating behaviour need confirmation with the finisher. Do not simply add the full coating thickness to every dimension. Agree whether fits will be masked, compensated before coating or finished afterward, including the consequences for exposed material and protection.")]
    [InlineData(
        "th",
        "การเก็บผิวและอโนไดซ์: ออกแบบตามสภาพสำเร็จ",
        "ความหนาชั้นอโนไดซ์รวมทั้งเนื้อวัสดุที่เปลี่ยนเป็นออกไซด์และส่วนที่โตออกจากผิวเดิม Aluminum Anodizers Council ให้แนวทางทั่วไปของฮาร์ดโค้ตว่าแทรกเข้าเนื้อประมาณครึ่งหนึ่งและโตออกประมาณครึ่งหนึ่ง แต่ต้องยืนยันผลการเตรียมผิวและการเคลือบจริงกับผู้ทำผิว ไม่ควรบวกความหนาเต็มกับทุกมิติโดยตรง ต้องตกลงว่าจะปิดบังผิวสวม ชดเชยขนาดก่อนเคลือบ หรือเก็บขนาดภายหลัง รวมถึงผลต่อเนื้อโลหะที่เปิดออกและการป้องกันผิว")]
    public async Task SourceFinalSurfaceFinishesRoute_PreservesTheRelocatedAnodizingDetail(
        string culture,
        string heading,
        string coatingGuidance)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/finishing/surface-finishes?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{coatingGuidance}<", source, StringComparison.Ordinal);
        Assert.Contains("id=\"finishing\"", source, StringComparison.Ordinal);
        Assert.Contains("src=\"/src/images/knowledge/anodize-dimensional-growth.svg\"", source, StringComparison.Ordinal);
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
