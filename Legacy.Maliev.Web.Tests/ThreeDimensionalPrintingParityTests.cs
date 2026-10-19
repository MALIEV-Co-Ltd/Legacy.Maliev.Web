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

        Assert.Contains("<strong>PC-FR</strong><small>Bambu PC FR</small>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>PA12</strong><small>eSUN ePA12</small>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>PETG-rCF08</strong><small>Fiberon · 8% recycled CF</small>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>PET-CF17</strong><small>Fiberon · 17% carbon fiber</small>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>ASA-CF</strong><small>Bambu ASA-CF</small>", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "แข็งแรงและหน่วงไฟ" : "Strong, flame-retardant grade", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ต้องยืนยันใบรับรองที่ต้องการ" : "Confirm required certification", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ต้องอบแห้งและใช้เครื่องแบบปิด" : "Drying and enclosure required", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "กัดหัวฉีดและต้องอบแห้ง" : "Abrasive; drying required", html, StringComparison.Ordinal);
        using var details = System.Text.Json.JsonDocument.Parse(
            await client.GetStringAsync("/src/data/printing-material-details.json"));
        var expectedDetails = culture == "th"
            ? new (string Key, string Description, string Availability)[]
        {
            ("PC-FR", "Bambu PC FR สำหรับกล่องไฟฟ้าและงานอุตสาหกรรม โดยต้องระบุใบรับรองการหน่วงไฟที่ต้องการให้ชัดเจน", "MALIEV ประเมินราคาด้วยโปรไฟล์ Bambu PC FR และยืนยันใบรับรองกับสต็อกปัจจุบันเป็นรายโครงการ"),
            ("PA12", "ไนลอน eSUN ePA12 สำหรับชิ้นส่วนแม่นยำทนทาน ดูดความชื้นน้อยกว่า PA6", "MALIEV ประเมินราคาด้วย eSUN ePA12 โดยยืนยันสีและสต็อกปัจจุบันในไทยตามใบประเมินราคา"),
            ("PETG-CF", "Fiberon PETG-rCF08 ใช้คาร์บอนไฟเบอร์รีไซเคิล 8% สำหรับฟิกซ์เจอร์ใช้งานที่ต้องการทั้งความแข็งและความเหนียว", "โปรไฟล์ที่ MALIEV ใช้พิมพ์คือ Fiberon PETG-rCF08 โดยยืนยันม้วนวัสดุและสต็อกปัจจุบันตามใบประเมินราคา"),
            ("PET-CF", "Fiberon PET-CF17 ใช้คาร์บอนไฟเบอร์ 17% สำหรับจิ๊กและเครื่องมือที่ต้องการความแข็งและคงรูป", "MALIEV จัดหา Fiberon PET-CF17 ตามคำสั่งซื้อ โดยยืนยันล็อตผู้จำหน่ายและสต็อกปัจจุบันในไทยตามใบประเมินราคา"),
            ("ASA-CF", "Bambu ASA-CF เป็น ASA เสริมคาร์บอนไฟเบอร์ที่แข็งและทน UV สำหรับเครื่องมือและกล่องครอบกลางแจ้ง", "ไม่พบสีตายตัวที่ยืนยันได้ในไทยสำหรับเกรดเสริมคาร์บอนนี้ สอบถามเพื่อให้เราตรวจสอบล็อตและสต็อกของผู้จำหน่าย"),
        }
            : new (string Key, string Description, string Availability)[]
        {
            ("PC-FR", "Bambu PC FR for electrical and industrial housings when the required flame-retardant certification is specified.", "MALIEV prices the Bambu PC FR profile; certification and current stock are confirmed per project."),
            ("PA12", "eSUN ePA12 nylon for durable precision parts with lower moisture uptake than PA6.", "MALIEV prices eSUN ePA12; colour and current Thailand stock are confirmed per quotation."),
            ("PETG-CF", "Fiberon PETG-rCF08 uses 8% recycled carbon fibre for functional fixtures that need stiffness and useful toughness.", "MALIEV's stocked printing profile is Fiberon PETG-rCF08; current spool and stock are confirmed per quotation."),
            ("PET-CF", "Fiberon PET-CF17 uses 17% carbon fibre for stiff, dimensionally stable jigs and tooling.", "MALIEV sources Fiberon PET-CF17 to order; supplier batch and current Thailand stock are confirmed per quotation."),
            ("ASA-CF", "Bambu ASA-CF is stiff, UV-stable carbon-fibre ASA for outdoor tooling and housings.", "No fixed Thailand colour variant is published for this carbon-fibre grade; ask us to verify the supplier batch and stock."),
        };
        foreach (var expected in expectedDetails)
        {
            var detail = details.RootElement.GetProperty(expected.Key);
            Assert.Equal(expected.Description, detail.GetProperty("description").GetProperty(culture).GetString());
            Assert.Equal(expected.Availability, detail.GetProperty("availability").GetProperty(culture).GetString());
        }

        var expectedReferences = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PLA"] = "Polymaker PolyLite PLA",
            ["PETG"] = "eSUN PETG",
            ["ASA"] = "eSUN ASA+",
            ["TPU"] = "eSUN TPU-95A",
            ["M68"] = "eSUN Standard Resin",
        };
        var verifiedColourRecords = new HashSet<string>(StringComparer.Ordinal)
        {
            "PLA", "PETG", "ASA", "TPU", "M68", "G217", "PA612-ESD", "ABS-ESD",
        };
        foreach (var material in details.RootElement.EnumerateObject())
        {
            var availability = material.Value.GetProperty("availability").GetProperty(culture).GetString()!;
            Assert.DoesNotContain("Elegoo", availability, StringComparison.OrdinalIgnoreCase);
            if (expectedReferences.TryGetValue(material.Name, out var reference))
            {
                Assert.Contains(reference, availability, StringComparison.Ordinal);
            }
            var colours = material.Value.GetProperty("colors").GetProperty(culture).EnumerateArray();
            if (verifiedColourRecords.Contains(material.Name))
            {
                Assert.NotEmpty(colours);
                if (culture == "en")
                {
                    Assert.True(availability.Contains("local", StringComparison.OrdinalIgnoreCase)
                        || availability.Contains("Thailand", StringComparison.OrdinalIgnoreCase));
                }
            }
            else
            {
                Assert.Empty(colours);
            }
        }
        var expectedResinColours = culture == "th"
            ? new[] { "เบจ", "ดำ", "ใส", "เทา", "เขียว", "ส้ม", "แดง", "ฟ้า", "ขาว", "เหลือง" }
            : new[] { "Beige", "Black", "Clear", "Gray", "Green", "Orange", "Red", "Sky blue", "White", "Yellow" };
        Assert.Equal(expectedResinColours, details.RootElement.GetProperty("M68").GetProperty("colors")
            .GetProperty(culture).EnumerateArray().Select(colour => colour.GetString()).ToArray());
        // Final source replaces the earlier PC-ESD catalogue record, not its historical sample.
        Assert.False(details.RootElement.TryGetProperty("PC-ESD", out _));
        Assert.Contains("Fiberon PA612-ESD", details.RootElement.GetProperty("PA612-ESD")
            .GetProperty("availability").GetProperty(culture).GetString(), StringComparison.Ordinal);
        Assert.Contains("eSUN ABS-ESD", details.RootElement.GetProperty("ABS-ESD")
            .GetProperty("availability").GetProperty(culture).GetString(), StringComparison.Ordinal);
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
