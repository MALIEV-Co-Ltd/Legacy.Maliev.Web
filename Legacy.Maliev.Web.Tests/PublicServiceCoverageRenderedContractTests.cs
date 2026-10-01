using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Source-pinned rendered contracts, not browser visibility or fulfillment proof.</summary>
public sealed class PublicServiceCoverageRenderedContractTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    // 6de82fd9760e86c71ddba3085879a63b43faff9f and 7b4b2af697207d36a6e7b7784dddefa150193e97.
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Printing_ActualRenderedProofLinkReachesUniqueOwnedGallery(string culture)
    {
        foreach (var active in new[] { true, false })
        {
            var html = await Get("/services/3d-printing", culture, active);
            AssertProofLink(html, culture);
            if (culture == "th")
            {
                Assert.Contains("รับปริ้น 3D รับพิมพ์ 3 มิติ | ประเมินราคาออนไลน์ | MALIEV", Text(html), StringComparison.Ordinal);
                var description = Assert.Single(Tags(html, "meta"), tag => Attribute(tag, "name") == "description");
                Assert.Equal("รับปริ้น 3D และรับพิมพ์ 3 มิติ ตั้งแต่ 1 ชิ้น อัปโหลด STL, STEP, OBJ หรือ 3MF ประเมินราคา FDM และเรซิ่นออนไลน์ ดูตัวอย่างชิ้นงานผลิตจริงจาก MALIEV", Attribute(description, "content"));
                Assert.Contains("รับปริ้น 3D และรับพิมพ์ 3 มิติ อัปโหลดไฟล์ประเมินราคา", Text(html), StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("/services/3d-printing", "en")]
    [InlineData("/services/3d-printing", "th")]
    [InlineData("/services/3d-scanning", "en")]
    [InlineData("/services/3d-scanning", "th")]
    [InlineData("/services/low-volume-injection-molding", "en")]
    [InlineData("/services/low-volume-injection-molding", "th")]
    public async Task Services_ActualLocalizedDocumentsPreserveCoverageAndAppointmentPromises(string route, string culture)
    {
        foreach (var active in route == "/services/low-volume-injection-molding" ? new[] { true } : new[] { true, false })
        {
            var html = await Get(route, culture, active);
            AssertCoverage(html, culture);
            AssertOwnedService(html, route, culture);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Scanning_ActualVisibleOnsiteFaqPreservesTravelAndFeasibilityBoundaries(string culture)
    {
        foreach (var active in new[] { true, false })
        {
            var html = await Get("/services/3d-scanning", culture, active);
            AssertScanning(html, culture);
            // Source 7b4 changes visible copy only. Its concise structured FAQ is not silently rewritten.
            var faq = Faq(html);
            var question = culture == "en" ? "Can MALIEV scan at our factory or site?" : "MALIEV ไปสแกนที่โรงงานหรือหน้างานได้หรือไม่?";
            var answer = culture == "en"
                ? "Yes. Onsite scanning is available when access, safety, environment, working space, schedule, and required deliverables are agreed."
                : "ได้ มีบริการสแกนนอกสถานที่เมื่อประเมินการเข้าถึง ความปลอดภัย สภาพแวดล้อม พื้นที่ ตาราง และผลลัพธ์แล้ว";
            Assert.Equal(answer, Assert.Single(faq, item => item.Question == question).Answer);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Injection_ActualVisibleAndStructuredInstallationFaqPreservesSameSourcePromise(string culture)
    {
        AssertInstallation(await Get("/services/low-volume-injection-molding", culture, true), culture);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Injection_DisabledOnlyRouteOwnerDoesNotInventRetainedFallback(string culture)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Services", "false"));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/services/low-volume-injection-molding?culture={culture}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("en", "anchor")]
    [InlineData("th", "anchor")]
    [InlineData("en", "target")]
    [InlineData("th", "target")]
    [InlineData("en", "label")]
    [InlineData("th", "label")]
    public async Task ProofLinkOracle_RejectsIndependentCorruptionOfActualHttpDocument(string culture, string corruption)
    {
        var html = await Get("/services/3d-printing", culture, true);
        AssertProofLink(html, culture);
        var altered = corruption switch
        {
            "anchor" => html.Replace("href=\"#printing-part-proof\"", "href=\"#unowned\"", StringComparison.Ordinal),
            "target" => html.Replace("id=\"printing-part-proof\"", "id=\"missing-proof\"", StringComparison.Ordinal),
            _ => ReplaceText(html, ProofLabel(culture), "Unproven fixture label"),
        };
        AssertRejected(html, altered, () => AssertProofLink(altered, culture));
    }

    [Theory]
    [InlineData("en", 0)]
    [InlineData("th", 0)]
    [InlineData("en", 1)]
    [InlineData("th", 1)]
    [InlineData("en", 2)]
    [InlineData("th", 2)]
    public async Task CoverageOracle_RejectsMissingSourcePromiseFromActualHttpDocument(string culture, int promise)
    {
        var html = await Get("/services/3d-printing", culture, true);
        AssertCoverage(html, culture);
        var altered = ReplaceText(html, Coverage(culture)[promise], "Removed owned promise");
        AssertRejected(html, altered, () => AssertCoverage(altered, culture));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task OnsiteOracle_RejectsMissingTravelBoundaryFromActualHttpDocument(string culture)
    {
        var html = await Get("/services/3d-scanning", culture, true);
        AssertScanning(html, culture);
        var altered = ReplaceText(html, ScanningAnswer(culture), "Onsite scanning without agreed boundaries");
        AssertRejected(html, altered, () => AssertScanning(altered, culture));
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task InstallationOracle_RejectsVisibleOrStructuredPromiseLossIndependently(string culture, bool structured)
    {
        var html = await Get("/services/low-volume-injection-molding", culture, true);
        AssertInstallation(html, culture);
        var pattern = structured ? "<script\\b[^>]*>.*?</script>" : "<details\\b[^>]*>.*?</details>";
        var altered = Regex.Replace(html, pattern, match =>
        {
            if (structured)
            {
                if (Attribute(match.Value.Split('>')[0], "type") != "application/ld+json") return match.Value;
                var replacement = JsonSerializer.Serialize(InstallationAnswer(culture))[1..^1];
                return match.Value.Replace(replacement, "Removed installation boundary", StringComparison.Ordinal);
            }
            return ReplaceText(match.Value, InstallationAnswer(culture), "Removed installation boundary");
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var question = culture == "en" ? "Do you install a purchased PIMM machine at our site?" : "MALIEV ติดตั้งเครื่อง PIMM ที่ซื้อ ณ สถานที่ของลูกค้าหรือไม่?";
        if (structured)
        {
            Assert.Equal(InstallationAnswer(culture), Assert.Single(VisibleFaq(altered), item => item.Question == question).Answer);
            Assert.Equal("Removed installation boundary", Assert.Single(Faq(altered), item => item.Question == question).Answer);
        }
        else
        {
            Assert.Equal(InstallationAnswer(culture), Assert.Single(Faq(altered), item => item.Question == question).Answer);
            Assert.Equal("Removed installation boundary", Assert.Single(VisibleFaq(altered), item => item.Question == question).Answer);
        }
        AssertRejected(html, altered, () => AssertInstallation(altered, culture));
        Assert.IsType<Xunit.Sdk.EqualException>(Record.Exception(() => AssertInstallation(altered, culture)));
    }

    private async Task<string> Get(string route, string culture, bool active)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Services", active.ToString()));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        using var response = await client.GetAsync($"{route}?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(culture, Attribute(Assert.Single(Tags(html, "html")), "lang"));
        return html;
    }

    private static void AssertProofLink(string html, string culture)
    {
        var document = RemoveScripts(html);
        var links = Regex.Matches(document, "<a\\b(?<attributes>[^>]*)>(?<body>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var link = Assert.Single(links.Cast<Match>(), match => Attribute(match.Groups["attributes"].Value, "class").Split(' ').Contains("service-hero-proof-link"));
        Assert.Equal("#printing-part-proof", Attribute(link.Groups["attributes"].Value, "href"));
        Assert.Equal(ProofLabel(culture), Text(link.Groups["body"].Value));
        Assert.Single(Tags(document, "section"), tag => Attribute(tag, "id") == "printing-part-proof");
    }

    private static void AssertCoverage(string html, string culture)
    {
        var paragraphs = Regex.Matches(RemoveScripts(html), "<p\\b[^>]*>(?<text>.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Cast<Match>().Select(match => Text(match.Groups["text"].Value)).ToArray();
        foreach (var promise in Coverage(culture))
            Assert.Single(paragraphs, paragraph => paragraph.Contains(promise, StringComparison.Ordinal));
    }

    private static void AssertScanning(string html, string culture)
    {
        var question = culture == "en" ? "Can you scan at our factory or site?" : "ไปสแกนที่โรงงานหรือหน้างานได้หรือไม่?";
        Assert.Equal(ScanningAnswer(culture), Assert.Single(VisibleFaq(html), item => item.Question == question).Answer);
    }

    private static void AssertInstallation(string html, string culture)
    {
        var question = culture == "en" ? "Do you install a purchased PIMM machine at our site?" : "MALIEV ติดตั้งเครื่อง PIMM ที่ซื้อ ณ สถานที่ของลูกค้าหรือไม่?";
        var visible = VisibleFaq(html);
        var structured = Faq(html);
        Assert.Equal(7, visible.Length);
        Assert.Equal(7, structured.Length);
        Assert.Equal(InstallationAnswer(culture), Assert.Single(visible, item => item.Question == question).Answer);
        Assert.Equal(InstallationAnswer(culture), Assert.Single(structured, item => item.Question == question).Answer);
    }

    private static void AssertOwnedService(string html, string route, string culture)
    {
        var service = Assert.Single(Schemas(html), node => node.GetProperty("@type").GetString() == "Service");
        var name = route switch
        {
            "/services/3d-printing" => culture == "en" ? "3D Printing Services" : "บริการรับพิมพ์ 3D",
            "/services/3d-scanning" => culture == "en" ? "3D Scanning Services" : "บริการรับสแกน 3D",
            _ => culture == "en" ? "Low-Volume Injection Molding" : "บริการฉีดพลาสติกจำนวนน้อย",
        };
        Assert.Equal(name, service.GetProperty("name").GetString());
        Assert.Equal("https://www.maliev.com" + route, service.GetProperty("url").GetString());
        Assert.Equal("https://www.maliev.com/#organization", service.GetProperty("provider").GetProperty("@id").GetString());
        Assert.Equal("Thailand", service.GetProperty("areaServed").GetProperty("name").GetString());
    }

    private static FaqItem[] VisibleFaq(string html) => Regex.Matches(RemoveScripts(html), "<details\\b[^>]*>\\s*<summary[^>]*>(?<question>.*?)</summary>\\s*<p[^>]*>(?<answer>.*?)</p>\\s*</details>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
        .Cast<Match>().Select(match => new FaqItem(Text(match.Groups["question"].Value), Text(match.Groups["answer"].Value))).ToArray();

    private static FaqItem[] Faq(string html) => Assert.Single(Schemas(html), node => node.GetProperty("@type").GetString() == "FAQPage")
        .GetProperty("mainEntity").EnumerateArray().Select(node => new FaqItem(node.GetProperty("name").GetString()!, node.GetProperty("acceptedAnswer").GetProperty("text").GetString()!)).ToArray();

    private static IEnumerable<JsonElement> Schemas(string html)
    {
        foreach (Match match in Regex.Matches(html, "<script\\b(?<attributes>[^>]*)>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            if (Attribute(match.Groups["attributes"].Value, "type") != "application/ld+json") continue;
            using var parsed = JsonDocument.Parse(match.Groups["json"].Value);
            Assert.Equal("https://schema.org", parsed.RootElement.GetProperty("@context").GetString());
            yield return parsed.RootElement.Clone();
        }
    }

    private static void AssertRejected(string original, string altered, Action assertion)
    {
        Assert.NotEqual(original, altered);
        Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(Record.Exception(assertion));
    }

    private static string[] Coverage(string culture) => culture == "en"
        ? ["We accept customer files throughout Thailand and ship completed parts nationwide by parcel.", "For workshop consultation, drop-off, or collection in Pak Kret, notify us by phone or LINE Official Account, or schedule an appointment before visiting.", "Onsite work is available for applicable services after project review; travel is quoted by distance and project scope."]
        : ["รับไฟล์งานจากลูกค้าทั่วประเทศไทย และจัดส่งชิ้นงานสำเร็จทั่วประเทศทางพัสดุ", "สำหรับการปรึกษา ส่งมอบ หรือรับชิ้นงานที่ปากเกร็ด กรุณาแจ้งล่วงหน้าทางโทรศัพท์หรือ LINE Official Account หรือนัดหมายก่อนเข้าพบ", "งานนอกสถานที่ให้บริการตามประเภทบริการและการประเมินโครงการ โดยประเมินค่าเดินทางตามระยะทางและขอบเขตงาน"];

    private static string ProofLabel(string culture) => culture == "en" ? "See actual MALIEV-produced parts" : "ดูตัวอย่างชิ้นงานผลิตจริงจาก MALIEV";
    private static string ScanningAnswer(string culture) => culture == "en"
        ? "Yes. We provide onsite scanning when a part is very large, cannot be disassembled, or cannot reasonably be shipped, subject to feasibility, access, safety, lighting, working space, and schedule. Travel is quoted by distance and project scope. Send photos, approximate dimensions, location, restrictions, and required deliverables for planning."
        : "ได้ เรามีบริการสแกนนอกสถานที่สำหรับชิ้นงานขนาดใหญ่มาก ถอดประกอบไม่ได้ หรือไม่สะดวกจัดส่ง โดยต้องประเมินความเป็นไปได้ การเข้าถึง ความปลอดภัย แสงสว่าง พื้นที่ทำงาน และตารางก่อน ประเมินค่าเดินทางตามระยะทางและขอบเขตโครงการ กรุณาส่งรูป ขนาดโดยประมาณ สถานที่ ข้อจำกัด และผลลัพธ์ที่ต้องการ";
    private static string InstallationAnswer(string culture) => culture == "en"
        ? "Yes. A purchased PIMM machine is installed at the customer location. Outside the Bangkok metropolitan area, installation and travel are quoted by distance. Share the delivery address and site-access requirements so the installation scope can be confirmed."
        : "ได้ เราติดตั้งเครื่อง PIMM ณ สถานที่ของลูกค้า สำหรับพื้นที่นอกกรุงเทพฯ และปริมณฑล ค่าติดตั้งและการเดินทางประเมินตามระยะทาง กรุณาส่งที่อยู่จัดส่งและข้อจำกัดการเข้าพื้นที่เพื่อยืนยันขอบเขตการติดตั้ง";
    private static string ReplaceText(string html, string text, string replacement) => Regex.Replace(html, "(?<text>[^<>]+)", match =>
    {
        var decoded = WebUtility.HtmlDecode(match.Value);
        return decoded.Contains(text, StringComparison.Ordinal)
            ? WebUtility.HtmlEncode(decoded.Replace(text, replacement, StringComparison.Ordinal))
            : match.Value;
    });
    private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", "")), "\\s+", " ").Trim();
    private static string RemoveScripts(string html) => Regex.Replace(html, "<script\\b[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static IEnumerable<string> Tags(string html, string tag) => Regex.Matches(RemoveScripts(html), $"<{tag}\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>().Select(match => match.Value);
    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(Regex.Match(tag, $"\\b{Regex.Escape(name)}\\s*=\\s*[\"'](?<value>[^\"']*)[\"']", RegexOptions.IgnoreCase).Groups["value"].Value);
    private sealed record FaqItem(string Question, string Answer);
}
