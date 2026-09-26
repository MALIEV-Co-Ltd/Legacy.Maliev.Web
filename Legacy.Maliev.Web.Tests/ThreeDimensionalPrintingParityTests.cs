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
        Assert.Contains("CATPart (CATIA)", source, StringComparison.Ordinal);
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
        Assert.Equal(24, MaterialRowRegex().Matches(component).Count);
        Assert.Contains("material-comparison.js", appEntry, StringComparison.Ordinal);
        Assert.Contains("data-material-row", comparisonScript, StringComparison.Ordinal);
        Assert.Contains("data-material-empty", comparisonScript, StringComparison.Ordinal);
        Assert.Contains("data-material-details-url", component, StringComparison.Ordinal);
        Assert.Contains("material_detail_viewed", comparisonScript, StringComparison.Ordinal);
        Assert.Contains("ไฟล์สามมิติที่รับรองสำหรับงาน 3D Printing", component, StringComparison.Ordinal);
        Assert.Contains("IPT (Inventor)", component, StringComparison.Ordinal);
        Assert.Contains("PAR (Solid Edge)", component, StringComparison.Ordinal);

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
