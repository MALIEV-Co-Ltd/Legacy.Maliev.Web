using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class ThreeDimensionalPrintingParityTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ThreeDimensionalPrintingParityTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("en", "Previous sample · PC-ESD", "Reinforced ESD nylon", "Static-dissipative ABS")]
    [InlineData("th", "ตัวอย่างเดิม · PC-ESD", "ไนลอน ESD เสริมแรง", "ABS สลายประจุไฟฟ้าสถิต")]
    public async Task MaterialComparisonOffersCurrentEsdGradesWithoutRelabelingHistoricalSample(
        string culture, string historicalLabel, string nylonDescription, string absDescription)
    {
        using var client = factory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/services/3d-printing?culture={culture}"));
        Assert.Contains("data-material-key=\"PA612-ESD\"", html, StringComparison.Ordinal);
        Assert.Contains("data-material-key=\"ABS-ESD\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-material-key=\"PC-ESD\"", html, StringComparison.Ordinal);
        Assert.Contains(historicalLabel, html, StringComparison.Ordinal);
        Assert.Contains("part-bento-pc-esd-threaded-component.webp", html, StringComparison.Ordinal);
        Assert.Contains(nylonDescription, html, StringComparison.Ordinal);
        Assert.Contains(absDescription, html, StringComparison.Ordinal);
        Assert.Contains("printing-material-details.json?v=3", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThreeDimensionalPrintingRoute_RendersTheCurrentSourceAssemblyAndFileGuidance()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/services/3d-printing?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"printing-tolerances\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"printing-related\"", source, StringComparison.Ordinal);
        Assert.Contains("What tolerance can 3D printing hold?", source, StringComparison.Ordinal);
        Assert.Contains("Painting, assembly, or a different process", source, StringComparison.Ordinal);
        Assert.Contains("Compare Materials", source, StringComparison.Ordinal);
        Assert.Contains("How we agree colour, sheen, and seams", source, StringComparison.Ordinal);
        Assert.Contains("See indicative starting prices", source, StringComparison.Ordinal);
        Assert.Contains("Your files stay confidential. We sign an NDA on request.", source, StringComparison.Ordinal);
        Assert.Contains("Read our NDA", source, StringComparison.Ordinal);
        Assert.Contains("Talk to an engineer", source, StringComparison.Ordinal);
        Assert.Contains("id=\"printing-quote-guide\"", source, StringComparison.Ordinal);
        Assert.Contains("Accepted 3D printing files", source, StringComparison.Ordinal);
        Assert.Contains("STEP / STP", source, StringComparison.Ordinal);
        Assert.Contains("3MF", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CATPart (CATIA)", source, StringComparison.Ordinal);
        Assert.Contains("Compare CNC machining", source, StringComparison.Ordinal);
        Assert.Contains("Review scanning and reverse engineering", source, StringComparison.Ordinal);
        Assert.Contains("ไฟล์เมชอาจต้องซ่อม", WebUtility.HtmlDecode((await factory.CreateClient().GetStringAsync("/services/3d-printing?culture=th"))), StringComparison.Ordinal);
        Assert.Contains("class=\"service-page-toc\"", source, StringComparison.Ordinal);
        Assert.Equal(6, ServiceCardMediaRegex().Matches(source).Count);
        Assert.Equal(3, Regex.Matches(
            Regex.Match(source, "<section id=\"printing-processes\"[\\s\\S]*?</section>").Value,
            "class=\"service-card service-card-visual\"").Count);
        Assert.Equal(3, Regex.Matches(
            Regex.Match(source, "<section id=\"printing-file-guide\"[\\s\\S]*?</section>").Value,
            "class=\"service-card service-card-visual\"").Count);
        Assert.Equal(8, FaqDetailsRegex().Matches(source).Count);
        Assert.Contains("Shipping within Thailand starts at THB 100", source, StringComparison.Ordinal);
        Assert.Contains("Pickup is available at our Pak Kret workshop by appointment", source, StringComparison.Ordinal);
        Assert.Contains("ค่าจัดส่งภายในประเทศไทยเริ่มต้น 100 บาท", WebUtility.HtmlDecode((await factory.CreateClient().GetStringAsync("/services/3d-printing?culture=th"))), StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ThreeDimensionalPrintingRoute_AdvertisesOnlySupportedQuotationFileFormats(string culture)
    {
        using var client = factory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/services/3d-printing?culture={culture}"));
        var guideStart = html.IndexOf("id=\"printing-quote-guide\"", StringComparison.Ordinal);
        Assert.True(guideStart >= 0);
        var cardStart = html.IndexOf("<article class=\"service-card\"", guideStart, StringComparison.Ordinal);
        Assert.True(cardStart > guideStart);
        var cardEnd = html.IndexOf("</article>", cardStart, StringComparison.Ordinal);
        Assert.True(cardEnd > cardStart);
        var fileCard = html[cardStart..cardEnd];

        foreach (var format in new[] { "STL", "OBJ", "3MF", "GLB", "GLTF", "STEP / STP", "IGES / IGS" })
        {
            Assert.Contains($"<li>{format}</li>", fileCard, StringComparison.Ordinal);
        }
        foreach (var format in new[] { "IPT (Inventor)", "CATPart (CATIA)", "SLDPRT (SolidWorks)", "PRT (NX)", "X_T / X_B (Parasolid)", "3DM (Rhino)", "SKP (SketchUp)", "PAR (Solid Edge)" })
        {
            Assert.DoesNotContain(format, fileCard, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ThreeDimensionalPrintingRoute_PlacesQuoteGuideAfterInPageToc(string culture)
    {
        using var client = factory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/services/3d-printing?culture={culture}"));
        var quickFacts = html.IndexOf("<section class=\"service-quick\">", StringComparison.Ordinal);
        var inPageToc = html.IndexOf("<nav class=\"service-page-toc\" aria-label=", StringComparison.Ordinal);
        var quoteGuide = html.IndexOf("<section id=\"printing-quote-guide\"", StringComparison.Ordinal);

        Assert.True(quickFacts >= 0);
        Assert.True(inPageToc > quickFacts);
        Assert.True(quoteGuide > inPageToc);
        Assert.Contains("href=\"#printing-quote-guide\"", html[inPageToc..quoteGuide], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Estimate FDM or resin", "Request engineering review")]
    [InlineData("th", "ประเมินราคา FDM หรือเรซิ่น", "ขอให้วิศวกรประเมิน")]
    public async Task ThreeDimensionalPrintingRoute_ShowsDistinctQuotationRoutesAndReviewRequirements(
        string culture,
        string instantLabel,
        string engineeringLabel)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/services/3d-printing?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"printing-route-grid\"", html, StringComparison.Ordinal);
        Assert.Contains("data-quote-route=\"instant\" data-quote-placement=\"printing_quote_guide\"", html, StringComparison.Ordinal);
        Assert.Contains("data-quote-route=\"engineering\" data-quote-placement=\"printing_quote_guide\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/InstantQuotation/3D-Printing\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/Quotation?item=3D-Printing\"", html, StringComparison.Ordinal);
        Assert.Contains(instantLabel, html, StringComparison.Ordinal);
        Assert.Contains(engineeringLabel, html, StringComparison.Ordinal);
        Assert.Contains("class=\"printing-review-grid\"", html, StringComparison.Ordinal);
        Assert.Contains("MJF, SLS, SLM", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ThreeDimensionalPrintingRoute_LabelsEveryQuoteChoiceForConsentSafeMeasurement(string culture)
    {
        using var client = factory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/services/3d-printing?culture={culture}"));

        Assert.Contains("data-quote-service-id=\"3d_printing\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-quote-locale=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Equal(10, Regex.Matches(html, "data-quote-route=\\\"(?:instant|engineering)\\\"").Count);
        foreach (var placement in new[] { "hero", "engineering_review", "part_proof", "printing_quote_guide", "final_cta" })
        {
            Assert.Equal(2, Regex.Matches(html, $"data-quote-placement=\\\"{placement}\\\"").Count);
        }

        var entry = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "assets", "route-service-printing.js"));
        Assert.Contains("inquiry-pages.js", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDimensionalPrintingSource_PreservesMaterialComparisonAccessibilityAndAssets()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var component = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "ThreeDimensionalPrintingContent.razor"));
        var appEntry = File.ReadAllText(Path.Combine(web, "assets", "route-service-printing.js"));
        var comparisonScript = File.ReadAllText(Path.Combine(
            web,
            "wwwroot",
            "src",
            "app",
            "js",
            "material-comparison.js"));

        Assert.Contains("id=\"material-comparison\"", component, StringComparison.Ordinal);
        Assert.Contains("id=\"material-search\"", component, StringComparison.Ordinal);
        Assert.Contains("id=\"material-process\"", component, StringComparison.Ordinal);
        Assert.Contains("id=\"material-reset\"", component, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", component, StringComparison.Ordinal);
        Assert.Contains("<caption class=\"sr-only\"", component, StringComparison.Ordinal);
        Assert.Contains("scope=\"col\"", component, StringComparison.Ordinal);
        Assert.Contains("scope=\"row\"", component, StringComparison.Ordinal);
        Assert.Equal(25, MaterialRowRegex().Matches(component).Count);
        Assert.Contains("material-comparison.js", appEntry, StringComparison.Ordinal);
        Assert.Contains("data-material-row", comparisonScript, StringComparison.Ordinal);
        Assert.Contains("data-material-empty", comparisonScript, StringComparison.Ordinal);
        Assert.Contains("data-material-details-url", component, StringComparison.Ordinal);
        Assert.Contains("material_detail_viewed", comparisonScript, StringComparison.Ordinal);
        Assert.Contains("ไฟล์สามมิติที่รับรองสำหรับงาน 3D Printing", component, StringComparison.Ordinal);
        var quickFacts = component.IndexOf("<section class=\"service-quick\">", StringComparison.Ordinal);
        var inPageToc = component.IndexOf("<nav class=\"service-page-toc\" aria-label=", StringComparison.Ordinal);
        var quoteGuide = component.IndexOf("<section id=\"printing-quote-guide\"", StringComparison.Ordinal);
        Assert.True(quickFacts >= 0 && inPageToc > quickFacts && quoteGuide > inPageToc);
        var fileCardStart = component.IndexOf("<h3>@T(\"Accepted 3D printing files\"", StringComparison.Ordinal);
        Assert.True(fileCardStart >= 0);
        var fileCardEnd = component.IndexOf("</article>", fileCardStart, StringComparison.Ordinal);
        Assert.True(fileCardEnd > fileCardStart);
        var fileCard = component[fileCardStart..fileCardEnd];
        foreach (var supportedFormat in new[] { "STL", "OBJ", "3MF", "GLB", "GLTF", "STEP / STP", "IGES / IGS" })
        {
            Assert.Contains($"<li>{supportedFormat}</li>", fileCard, StringComparison.Ordinal);
        }
        foreach (var unsupportedFormat in new[] { "IPT (Inventor)", "CATPart (CATIA)", "SLDPRT (SolidWorks)", "PRT (NX)", "X_T / X_B (Parasolid)", "3DM (Rhino)", "SKP (SketchUp)", "PAR (Solid Edge)" })
        {
            Assert.DoesNotContain(unsupportedFormat, fileCard, StringComparison.Ordinal);
        }
        var uploadWorkflow = File.ReadAllText(Path.Combine(web, "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor"));
        var modelViewer = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "model-viewer", "model-viewer.js"));
        Assert.Contains("accept=\".stl,.obj,.3mf,.glb,.gltf,.stp,.step,.igs,.iges", uploadWorkflow, StringComparison.Ordinal);
        Assert.Contains("var SUPPORTED_EXTENSIONS = ['stl', 'obj', '3mf', 'glb', 'gltf', 'stp', 'step', 'igs', 'iges'];", modelViewer, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "wwwroot/src/images/services/printing/printing-fdm-application.webp",
            "wwwroot/src/images/services/printing/printing-resin-application.webp",
            "wwwroot/src/images/services/printing/printing-industrial-application.webp",
            "wwwroot/src/images/services/printing/printing-finish-color-approval.webp",
            "wwwroot/src/images/services/printing/printing-finish-surface-prep.webp",
            "wwwroot/src/images/services/printing/printing-finish-clear-coat.webp",
            "wwwroot/src/images/services/printing/printing-split-assembly.webp",
            "wwwroot/src/images/services/printing/printing-file-manifold-check.webp",
            "wwwroot/src/images/services/printing/printing-file-wall-clearance.webp",
            "wwwroot/src/images/services/printing/printing-file-orientation-support.webp"
        })
        {
            var path = Path.Combine(root, "Legacy.Maliev.Web", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Expected printing asset '{path}'.");
            Assert.True(new FileInfo(path).Length > 0, $"Expected printing asset '{path}' to be non-empty.");
        }
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task ToleranceComparison_RendersFourSourceRangesAndExpandableFactors(
        bool servicesEnabled,
        string culture)
    {
        await using var routeFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Services", servicesEnabled ? "true" : "false"));
        using var client = routeFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync($"/services/3d-printing?culture={culture}", deadline.Token);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(deadline.Token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(servicesEnabled, html.Contains(
            "data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var start = html.IndexOf("<section id=\"printing-tolerances\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var section = html[start..end];

        Assert.Contains("class=\"service-tolerance-grid\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", section, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "เปรียบเทียบค่าความคลาดเคลื่อนเริ่มต้นโดยทั่วไปสำหรับชิ้นงานที่ออกแบบมาเหมาะสม ค่านี้ไม่ใช่การรับประกัน กรุณาส่งแบบเพื่อให้เรายืนยันขนาดสำคัญก่อนผลิต"
            : "Compare typical starting ranges for a well-designed part. These are not guarantees; send the drawing so we can confirm the critical dimensions before production.",
            section, StringComparison.Ordinal);

        (string Heading, string Value, string? Alternative, string Rule)[] expected = culture == "th"
            ? [
                ("FDM", "±0.3–0.5 มม.", "หรือ ±0.5%", "ฟีเจอร์ขนาดเล็ก · ใช้ค่าเปอร์เซ็นต์เมื่อขนาดเกินราว 100 มม."),
                ("เรซิ่น SLA / DLP", "±0.05–0.15 มม.", "หรือ ±0.2–0.3%", "ฟีเจอร์ไม่เกินราว 30 มม. · ใช้ค่าเปอร์เซ็นต์สำหรับขนาดที่ใหญ่ขึ้น"),
                ("ไนลอน MJF / SLS", "±0.3 มม. หรือ ±0.3%", null, "ใช้ค่าที่มากกว่า"),
                ("โลหะ SLM / DMLS", "±0.1–0.2 มม.", "จากนั้นประมาณ ±0.1%", "ช่วง 25 มม. แรก · ใช้ค่าเปอร์เซ็นต์กับขนาดที่เหลือ")
            ]
            : [
                ("FDM", "±0.3–0.5 mm", "or ±0.5%", "Small features · percentage basis above about 100 mm"),
                ("Resin SLA / DLP", "±0.05–0.15 mm", "or ±0.2–0.3%", "Features up to about 30 mm · percentage basis for larger dimensions"),
                ("MJF / SLS nylon", "±0.3 mm or ±0.3%", null, "Use whichever value is greater"),
                ("Metal SLM / DMLS", "±0.1–0.2 mm", "then about ±0.1%", "First 25 mm · percentage basis for the remaining nominal dimension")
            ];
        var cards = Regex.Matches(section,
            "<article class=\"service-tolerance-item\">(?<body>[\\s\\S]*?)</article>",
            RegexOptions.CultureInvariant);
        Assert.Equal(4, cards.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            var body = cards[index].Groups["body"].Value;
            var item = expected[index];
            Assert.Contains($"<h3>{item.Heading}</h3>", body, StringComparison.Ordinal);
            Assert.Contains($"<strong>{item.Value}</strong>", body, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(body, "class=\"service-tolerance-value\"",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Contains($"<p class=\"service-tolerance-rule\">{item.Rule}</p>", body, StringComparison.Ordinal);
            if (item.Alternative is not null)
            {
                Assert.Contains($"<span>{item.Alternative}</span>", body, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("<span>", body, StringComparison.Ordinal);
            }
        }

        var details = Assert.Single(Regex.Matches(section,
            "<details class=\"service-tolerance-details service-tolerance-factors\"(?<attributes>[^>]*)>(?<body>[\\s\\S]*?)</details>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.False(Regex.IsMatch(details.Groups["attributes"].Value,
            "(?:^|\\s)open(?:\\s|=|$)", RegexOptions.CultureInvariant));
        var explanation = details.Groups["body"].Value;
        Assert.Contains(culture == "th"
            ? "<summary>อะไรทำให้ค่าคลาดเคลื่อนและการประกอบเปลี่ยนไป?</summary>"
            : "<summary>What changes these ranges and part fit?</summary>",
            explanation, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "การหดตัวของวัสดุ ขนาดชิ้นงาน ทิศทางการพิมพ์ ตำแหน่งซัพพอร์ต การอบหรือความเค้นจากความร้อน และการกัดแต่งหลังพิมพ์"
            : "Material shrinkage, part size, build orientation, support placement, curing or thermal stress, and any post-machining.",
            explanation, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "ค่าความคลาดเคลื่อนไม่เท่ากับการประกอบได้พอดี ชิ้นงานสองชิ้นที่คุมได้ ±0.3 มม. เท่ากันก็ยังอาจประกอบติดขัดได้ ควรระบุขนาดที่สำคัญจริงลงในแบบงาน เราสามารถคว้าน ต๊าปเกลียว หรือกัดแต่งเฉพาะจุดหลังพิมพ์ได้เมื่อค่าจากการพิมพ์ไม่เพียงพอ"
            : "Tolerance is not the same as fit. Two parts that each hold ±0.3 mm can still bind when assembled. Mark the dimensions that actually matter on your drawing — we can ream, tap, or machine those features after printing when the printed tolerance is not enough.",
            explanation, StringComparison.Ordinal);
        Assert.Contains("<section id=\"printing-workflow\" class=\"service-section\">",
            html, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    [GeneratedRegex("class=\"service-card-media\"")]
    private static partial Regex ServiceCardMediaRegex();

    [GeneratedRegex("<details>", RegexOptions.CultureInvariant)]
    private static partial Regex FaqDetailsRegex();

    [GeneratedRegex("data-material-row")]
    private static partial Regex MaterialRowRegex();
}
