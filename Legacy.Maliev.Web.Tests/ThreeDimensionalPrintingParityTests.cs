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
        Assert.Contains("class=\"service-page-toc\"", source, StringComparison.Ordinal);
        Assert.Equal(6, ServiceCardMediaRegex().Matches(source).Count);
        Assert.Equal(7, FaqDetailsRegex().Matches(source).Count);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
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
