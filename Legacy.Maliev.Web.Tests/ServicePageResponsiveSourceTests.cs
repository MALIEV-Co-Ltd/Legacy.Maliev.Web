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
        Assert.True(
            source.IndexOf("<ServiceBreadcrumb", StringComparison.Ordinal) < source.IndexOf("<section class=\"service-hero\"", StringComparison.Ordinal),
            "The service breadcrumb must precede its hero.");
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

        var cncCapabilities = System.Text.RegularExpressions.Regex.Match(cnc, "<section id=\"cnc-capabilities\"[\\s\\S]*?</section>").Value;
        var scanningDecisions = string.Join(Environment.NewLine,
            new[] { "scanning-deliverables", "scanning-service-modes", "scanning-preparation" }
                .Select(id => System.Text.RegularExpressions.Regex.Match(scanning, $"<section id=\"{id}\"[\\s\\S]*?</section>").Value));
        var decisionSections = string.Join(Environment.NewLine, cncCapabilities, scanningDecisions);
        var cardImages = System.Text.RegularExpressions.Regex.Matches(decisionSections, "<img class=\"service-card-media\"[^>]+>");
        Assert.Equal(12, cardImages.Count);
        Assert.Equal(12, System.Text.RegularExpressions.Regex.Matches(decisionSections, "class=\"service-card service-card-visual\"").Count);
        foreach (System.Text.RegularExpressions.Match image in cardImages)
        {
            Assert.Contains("loading=\"lazy\"", image.Value, StringComparison.Ordinal);
            Assert.Contains("width=\"960\" height=\"640\"", image.Value, StringComparison.Ordinal);
            Assert.Contains("alt=\"@T(", image.Value, StringComparison.Ordinal);
        }

        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(cncCapabilities, "services/cnc/cards/[^\"']+\\.webp").Count);
        Assert.Equal(9, System.Text.RegularExpressions.Regex.Matches(scanningDecisions, "services/scanning/cards/[^\"']+\\.webp").Count);
        foreach (var (page, image) in new[]
        {
            (printing, "printing/printing-hero.webp"),
            (cnc, "cnc/cnc-hero.webp"),
            (scanning, "scanning/scanning-hero.webp"),
        })
        {
            var hero = System.Text.RegularExpressions.Regex.Match(
                page,
                $"<img class=\"service-hero-media\" src=\"/src/images/services/{System.Text.RegularExpressions.Regex.Escape(image)}\"[^>]+>");
            Assert.True(hero.Success);
            Assert.Contains("width=\"1672\"", hero.Value, StringComparison.Ordinal);
            Assert.Contains("height=\"941\"", hero.Value, StringComparison.Ordinal);
            Assert.Contains("loading=\"eager\"", hero.Value, StringComparison.Ordinal);
            Assert.Contains("fetchpriority=\"high\"", hero.Value, StringComparison.Ordinal);
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

    [Fact]
    public void SharedServiceCss_PreservesOriginalSvgSizingAndMobileActions()
    {
        var css = Read("Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css");
        foreach (var (selector, size) in new[]
        {
            (".service-hero-points i, .service-hero-points .svg-inline--fa", "1rem"),
            (".service-button i, .service-button .svg-inline--fa", "1rem"),
            (".service-quick i, .service-quick .svg-inline--fa", "2rem"),
            (".service-card > i, .service-card > .svg-inline--fa", "2.6rem"),
        })
        {
            var rule = Assert.Single(System.Text.RegularExpressions.Regex.Matches(
                css,
                @"(?m)^" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}")
                .Cast<System.Text.RegularExpressions.Match>()).Groups["body"].Value;
            Assert.Matches(@"(?:^|;)\s*width:\s*" + System.Text.RegularExpressions.Regex.Escape(size) + @"\s*;", rule);
            Assert.Matches(@"(?:^|;)\s*height:\s*" + System.Text.RegularExpressions.Regex.Escape(size) + @"\s*;", rule);
        }

        var mobile = Assert.Single(System.Text.RegularExpressions.Regex.Matches(
                css,
                @"(?ms)^@media\s*\(max-width:\s*47\.99rem\)\s*\{(?<rules>.*?)^\}")
            .Cast<System.Text.RegularExpressions.Match>(),
            block => block.Groups["rules"].Value.Contains(".service-hero {", StringComparison.Ordinal));
        Assert.Matches(@"(?m)^\s*\.service-actions\s*\{[^}]*\bjustify-content:\s*center\s*;", mobile.Groups["rules"].Value);
    }

    [Fact]
    public void SharedServiceCss_PreservesSourceNotePriceAndMobileBulletHierarchy()
    {
        var css = Read("Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css");
        // Frozen edf4 evolves the original left border into a full border while retaining rounded notes.
        Assert.Matches(@"(?m)^\.service-note\s*\{(?=[^}]*\bborder:\s*1px solid #c5daf7\s*;)(?=[^}]*border-radius:\s*var\(--service-radius-sm\)\s*;)", css);
        Assert.Matches(@"(?m)^\.service-pricing-card strong\s*\{(?=[^}]*border-top:\s*1px solid var\(--service-line\)\s*;)(?=[^}]*font-size:\s*1\.3rem\s*;)(?=[^}]*line-height:\s*1\.3\s*;)", css);

        var mobile = Assert.Single(System.Text.RegularExpressions.Regex.Matches(
                css,
                @"(?ms)^@media\s*\(max-width:\s*47\.99rem\)\s*\{(?<rules>.*?)^\}")
            .Cast<System.Text.RegularExpressions.Match>(),
            block => block.Groups["rules"].Value.Contains(".service-hero {", StringComparison.Ordinal));
        Assert.Matches(@"(?m)^\s*\.service-hero-points\s*\{(?=[^}]*width:\s*fit-content\s*;)(?=[^}]*margin-inline:\s*auto\s*;)(?=[^}]*flex-direction:\s*column\s*;)(?=[^}]*align-items:\s*flex-start\s*;)(?=[^}]*justify-content:\s*center\s*;)", mobile.Groups["rules"].Value);
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
