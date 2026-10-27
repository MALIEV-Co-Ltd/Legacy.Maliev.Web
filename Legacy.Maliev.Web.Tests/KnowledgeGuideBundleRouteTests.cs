using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real public active/retained route reads; no manufacturing or credential provider fixture.</summary>
public sealed class KnowledgeGuideBundleRouteTests
{
    public static IEnumerable<object[]> SourceDocuments()
    {
        yield return new object[] { "/knowledges", "BlazorRouting:KnowledgesIndex", "Manufacturing knowledge library", "คลังความรู้ด้านการผลิต", "cnc-guides|project-guides|before-sending|knowledge-next" };
        yield return new object[] { "/knowledges/guidelines", "BlazorRouting:KnowledgesGuidelines", "Quotation request guidelines", "แนวทางขอใบเสนอราคา", "files|requirements|confidentiality" };
        yield return new object[] { "/knowledges/workflow", "BlazorRouting:KnowledgesWorkflow", "What happens after you request a quote", "เกิดอะไรขึ้นหลังขอใบเสนอราคา", "review|approval|delivery" };
        yield return new object[] { "/knowledges/specifications", "BlazorRouting:KnowledgesSpecifications", "Manufacturing preparation guides", "คู่มือเตรียมงานผลิต", "compare-inputs|other-processes" };
        yield return new object[] { "/knowledges/specifications/3d-printing", "BlazorRouting:KnowledgesSpecifications3DPrinting", "Prepare a model for 3D printing", "เตรียมโมเดลสำหรับพิมพ์ 3 มิติ", "geometry|process|fit|finish" };
        yield return new object[] { "/knowledges/specifications/3d-scanning", "BlazorRouting:KnowledgesSpecifications3DScanning", "Prepare a part for 3D scanning", "เตรียมชิ้นงานสำหรับสแกน 3 มิติ", "deliverables|measurement|preparation|request" };
        yield return new object[] { "/knowledges/specifications/cnc-machining", "BlazorRouting:KnowledgesSpecificationsCncMachining", "Design decisions that make CNC parts easier to manufacture", "ออกแบบชิ้นงาน CNC ให้ผลิตได้ตรงความต้องการ", "files|thin-walls|radii-pockets|size-fixturing|warpage|chatter|holes-threads|tolerances|axes|edm|grinding|finishing" };
        yield return new object[] { "/knowledges/cnc/cutting-tools", "BlazorRouting:KnowledgesIndex", "CNC cutting tools: select the whole cutting system", "เครื่องมือตัด CNC: เลือกให้เหมาะทั้งระบบการตัด", "selection|substrate|geometry|flutes|holding|ballnose|direction|coolant-wear" };
        yield return new object[] { "/knowledges/cnc/feeds-and-speeds", "BlazorRouting:KnowledgesIndex", "CNC feeds and speeds: calculate, then verify", "อัตราป้อนและความเร็วตัด CNC: คำนวณแล้วตรวจสอบ", "starting-data|units|milling|engagement|drilling|turning|trial" };
        yield return new object[] { "/knowledges/cnc/geometry-and-workholding", "BlazorRouting:KnowledgesIndex", "Geometry, rigidity and workholding", "รูปทรง ความแข็งแกร่ง และการจับยึด", "thin-walls|radii-pockets|size-fixturing|warpage|chatter" };
        yield return new object[] { "/knowledges/cnc/holes-and-tolerances", "BlazorRouting:KnowledgesIndex", "Holes, threads and tolerances", "รู เกลียว และค่าความคลาดเคลื่อน", "holes-threads|tolerances" };
        yield return new object[] { "/knowledges/cnc/machining-processes", "BlazorRouting:KnowledgesIndex", "3-axis, 5-axis, EDM and grinding", "3 แกน 5 แกน EDM และการเจียร", "axes|edm|grinding" };
        yield return new object[] { "/knowledges/finishing/heat-treatment", "BlazorRouting:KnowledgesIndex", "Plan heat treatment before final machining", "วางแผนอบชุบก่อนตัดเฉือนขั้นสุดท้าย", "purpose|surface|sequence|acceptance" };
        yield return new object[] { "/knowledges/finishing/surface-finishes", "BlazorRouting:KnowledgesIndex", "Surface finishes and coatings", "ผิวงานและการเคลือบ", "finishing" };
        yield return new object[] { "/knowledges/inspection/measuring-parts", "BlazorRouting:KnowledgesIndex", "Measure the requirement, not just the number", "วัดให้ตรงข้อกำหนด ไม่ใช่แค่อ่านตัวเลข", "tools|uncertainty|setup|temperature|report" };
        yield return new object[] { "/knowledges/manufacturing/process-selection", "BlazorRouting:KnowledgesIndex", "Choose the process around the part", "เลือกกระบวนการให้เหมาะกับชิ้นงาน", "requirements|routes|economics|example|handoff" };
        yield return new object[] { "/knowledges/materials/engineering-plastics", "BlazorRouting:KnowledgesIndex", "Engineering plastics: match the grade to the environment", "พลาสติกวิศวกรรม: เลือกเกรดให้ตรงสภาพใช้งาน", "pom-pa|peek-ptfe|pc-abs|comparison|route" };
        yield return new object[] { "/knowledges/materials/metals", "BlazorRouting:KnowledgesIndex", "Metals: choose the grade and its condition", "โลหะ: เลือกทั้งเกรดและสภาพวัสดุ", "aluminium|stainless|steels|comparison" };
        yield return new object[] { "/knowledges/materials/selection", "BlazorRouting:KnowledgesIndex", "Choose a material around the job it must do", "เลือกวัสดุจากหน้าที่ที่ชิ้นส่วนต้องทำ", "requirements|environment|specification|worksheet|release" };
    }

    public static IEnumerable<object[]> Documents =>
        from row in SourceDocuments()
        from culture in new[] { "en", "th" }
        from retained in new[] { false, true }
        select row.Concat(new object[] { culture, retained }).ToArray();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ActiveAndRetainedGuidesKeepLocalizedContentAnchorsAndSeo(
        string route, string feature, string englishHeading, string thaiHeading, string anchors,
        string culture, bool retained)
    {
        await using var parent = new TestingWebApplicationFactory();
        await using var application = parent.WithWebHostBuilder(builder =>
            builder.UseSetting(feature, retained ? "false" : "true"));
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync($"{route}?culture={culture}&tracking=excluded");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Contains($">{(culture == "th" ? thaiHeading : englishHeading)}<", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<h1\\b", RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains("route-knowledge.js", html, StringComparison.Ordinal);
        foreach (var anchor in anchors.Split('|', StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains($"id=\"{anchor}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Lorem ipsum", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("still under development", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.OrdinalIgnoreCase);
        var canonical = "https://www.maliev.com" + route + (culture == "en" ? "?culture=en" : "");
        Assert.Contains($"rel=\"canonical\" href=\"{canonical}\"", html, StringComparison.Ordinal);
        Assert.Contains($"rel=\"alternate\" href=\"https://www.maliev.com{route}?culture=en\" hreflang=\"en\"", html, StringComparison.Ordinal);
        Assert.Contains($"rel=\"alternate\" href=\"https://www.maliev.com{route}\" hreflang=\"th\"", html, StringComparison.Ordinal);
        Assert.Contains($"rel=\"alternate\" href=\"https://www.maliev.com{route}\" hreflang=\"x-default\"", html, StringComparison.Ordinal);
        foreach (var link in Regex.Matches(html, "<link[^>]*(?:canonical|hreflang)[^>]*>").Cast<Match>())
            Assert.DoesNotContain("tracking=excluded", link.Value, StringComparison.Ordinal);
        if (retained)
        {
            var navigation = Regex.Match(html, "<aside[^>]*id=\"knowledge-navigation\"[^>]*>.*?</aside>",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            Assert.True(navigation.Success);
            Assert.Single(Regex.Matches(navigation.Value, "aria-current=\"page\"").Cast<Match>());
        }
        if (route == "/knowledges/guidelines")
        {
            foreach (var alias in new[] { "guideline-files", "guideline-result", "guideline-requirements", "guideline-confidentiality" })
                Assert.Contains($"id=\"{alias}\"", html, StringComparison.Ordinal);
            var match = Regex.Match(html, "data-migration-component=\"knowledge-guidelines-structured-data\"[^>]*>(?<json>.*?)</script>",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            Assert.True(match.Success);
            using var document = JsonDocument.Parse(match.Groups["json"].Value);
            Assert.Equal(canonical, document.RootElement.GetProperty("url").GetString());
            Assert.Equal(4, document.RootElement.GetProperty("step").GetArrayLength());
            foreach (var step in document.RootElement.GetProperty("step").EnumerateArray())
            {
                var stepUrl = step.GetProperty("url").GetString();
                Assert.NotNull(stepUrl);
                Assert.StartsWith(canonical + "#guideline-", stepUrl, StringComparison.Ordinal);
                Assert.DoesNotContain("tracking=excluded", stepUrl, StringComparison.Ordinal);
            }
        }
        if (route == "/knowledges/cnc/geometry-and-workholding")
        {
            Assert.Contains("/src/images/knowledge/cnc-wall-stiffness.svg", html, StringComparison.Ordinal);
            Assert.Contains("/src/images/knowledge/cnc-corner-clearance.svg", html, StringComparison.Ordinal);
        }
        if (route == "/knowledges/finishing/surface-finishes")
            Assert.Contains("/src/images/knowledge/anodize-dimensional-growth.svg", html, StringComparison.Ordinal);
        if (route.EndsWith("cnc-machining", StringComparison.Ordinal))
        {
            Assert.Equal(2, Regex.Matches(html, "<img[^>]*src=\"[^\"]*/src/images/knowledge/").Count);
            Assert.Contains("/src/images/knowledge/visual-cnc-preparation.svg", html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("anodize-dimensional-growth.svg", "44424727d2e0cc7e906257d62999a578c13ee9754a6d24f63e0fc6c0cef61b14")]
    [InlineData("cnc-corner-clearance.svg", "743ba8f3edacaa001f716f3251f8d4cec0914685111481cc6e134549daca8844")]
    [InlineData("cnc-wall-stiffness.svg", "041d4f9d6ce93905fd213ecdfab047e4ebae54093ac0b542fc0efb056f34c201")]
    public async Task DiagramsAreServedWithExactSourceBytes(string file, string expectedSha256)
    {
        await using var factory = new TestingWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/src/images/knowledge/" + file);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var svg = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(bytes));
        Assert.Equal("svg", svg.Root?.Name.LocalName);
    }
}
