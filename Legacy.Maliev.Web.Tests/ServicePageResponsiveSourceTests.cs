namespace Legacy.Maliev.Web.Tests;

public sealed class ServicePageResponsiveSourceTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void ServicePagesExposeAProgressiveStickyTocWithoutChangingSectionMarkup()
    {
        var toc = Read("Legacy.Maliev.Web", "Components", "Shared", "ServicePageToc.razor");
        var css = Read("Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css");
        var js = Read("Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "service-toc.js");
        var entry = Read("Legacy.Maliev.Web", "assets", "route-service-toc.js");

        Assert.Matches(@"(?m)^\.service-page\s*\{[^}]*overflow:\s*clip\s*;", css);
        Assert.Matches(@"(?m)^\.service-page-toc\s*\{(?=[^}]*position:\s*sticky\s*;)(?=[^}]*padding-block:\s*\.25rem\s*;)", css);
        Assert.Matches(@"(?m)^\.service-page-toc-inner\s*\{[^}]*min-width:\s*0\s*;", css);
        Assert.Matches(@"(?m)^\.service-page-toc ul\s*\{(?=[^}]*justify-content:\s*flex-start\s*;)(?=[^}]*overflow-x:\s*auto\s*;)(?=[^}]*overflow-y:\s*hidden\s*;)(?=[^}]*scroll-padding-inline:\s*\.25rem\s*;)(?=[^}]*scrollbar-width:\s*none\s*;)(?=[^}]*-ms-overflow-style:\s*none\s*;)(?=[^}]*-webkit-overflow-scrolling:\s*touch\s*;)", css);
        Assert.Matches(@"(?m)^\.service-page-toc ul::-webkit-scrollbar\s*\{[^}]*display:\s*none\s*;", css);

        Assert.Contains("data-service-toc", toc, StringComparison.Ordinal);
        Assert.Contains("data-service-toc-list", toc, StringComparison.Ordinal);
        Assert.Contains("data-service-toc-toggle", toc, StringComparison.Ordinal);
        Assert.Contains("data-service-toc-preview", toc, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"service-page-toc-panel\"", toc, StringComparison.Ordinal);
        Assert.Contains("hidden", toc, StringComparison.Ordinal);
        Assert.Contains("position: sticky", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto", css, StringComparison.Ordinal);
        Assert.Contains("padding-inline-end: 2.75rem", css, StringComparison.Ordinal);
        Assert.Contains("service-page-toc[data-toc-open=\"true\"] .service-page-toc-panel", css, StringComparison.Ordinal);
        Assert.Contains(".service-page-toc a.is-active", css, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"true\"", css, StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame", js, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", js, StringComparison.Ordinal);
        Assert.Contains("scrollTo({", js, StringComparison.Ordinal);
        Assert.Contains("addEventListener('scroll'", js, StringComparison.Ordinal);
        Assert.Contains("TRAILING_EDGE_PADDING = 44", js, StringComparison.Ordinal);
        Assert.Contains("service-toc.js", entry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ThreeDimensionalPrintingContent.razor", "3D Printing")]
    [InlineData("ThreeDimensionalScanningContent.razor", "3D Scanning")]
    [InlineData("CncMachiningContent.razor", "CNC Machining")]
    [InlineData("CustomManufacturingContent.razor", "Custom Manufacturing")]
    public void ManufacturingServiceRoutesRenderTheSharedToc(string fileName, string service)
    {
        var source = Read("Legacy.Maliev.Web", "Components", "Pages", "Services", fileName);
        Assert.Contains("<ServiceBreadcrumb", source, StringComparison.Ordinal);
        Assert.Contains("<ServicePageToc />", source, StringComparison.Ordinal);
        Assert.Contains($"ServiceKey=\"{service}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ManufacturingPagesKeepOriginalGuideContentGuards()
    {
        var cnc = Read("Legacy.Maliev.Web", "Components", "Pages", "Services", "CncMachiningContent.razor");
        var printing = Read("Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor");
        var scanning = Read("Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalScanningContent.razor");

        foreach (var required in new[] { "SS400", "S50C", "SCM", "SKD", "SUS", "POM", "PA6", "Acrylic", "PVC", "PTFE", "One piece", "wood or organic materials" })
        {
            Assert.Contains(required, cnc, StringComparison.Ordinal);
        }

        var keys = global::Legacy.Maliev.Web.Application.Pricing.PricingCatalog.Materials.Keys;
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.Contains($"data-material-key=\"{key}\"", printing, StringComparison.Ordinal);
        }

        Assert.Contains("reflective", scanning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scanning spray", scanning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("สแกนนอกสถานที่", scanning, StringComparison.Ordinal);
        Assert.DoesNotContain("ISO 9001", printing + scanning, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
