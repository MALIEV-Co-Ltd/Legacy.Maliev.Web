namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationResponsiveCssTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Feature_styles_are_mobile_first_touch_safe_and_zoom_resilient()
    {
        var css = ReadCss();

        Assert.Contains(".instant-quote__workflow", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr)", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 0", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
        Assert.Contains("width: 100%", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("outline: 3px solid", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 48rem)", css, StringComparison.Ordinal);
        Assert.Contains("body.instant-quotation-shell", css, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-state=\"empty\"]", css, StringComparison.Ordinal);
        Assert.Contains(".iq-empty-dropzone", css, StringComparison.Ordinal);
        Assert.Contains("min-height: calc(100dvh - 4.5625rem)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 44fr) minmax(0, 56fr)", css, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-state=\"review\"]", css, StringComparison.Ordinal);
        Assert.Contains("height: clamp(280px, 72vw, 340px)", css, StringComparison.Ordinal);
        Assert.Contains("height: clamp(260px, 32vh, 420px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 35.99875rem)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 52px minmax(0, 1fr)", css, StringComparison.Ordinal);
        Assert.Contains(".instant-quote__workflow .instant-quote__review-fields {\n        grid-template-columns: 1fr;\n        gap: .45rem;", css.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("[data-workflow-build-preference]", css, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-material-comparison]", css, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-part-number]", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewer_review_totals_and_actions_cannot_force_horizontal_page_overflow()
    {
        var css = ReadCss();

        Assert.Contains("[data-workflow-viewer] canvas", css, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio: 4 / 3", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%", css, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-review-part]", css, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-order-total]", css, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-review-total]", css, StringComparison.Ordinal);
        Assert.Contains(".instant-quote__actions", css, StringComparison.Ordinal);
        Assert.Contains("flex-wrap: wrap", css, StringComparison.Ordinal);
        Assert.Contains(".instant-quote__workflow:not(:has(> .instant-quote__gltf-unit)):has(> [data-workflow-viewer]):has(> [data-workflow-parts-panel])", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: minmax(0, 1fr) auto", css, StringComparison.Ordinal);
        Assert.Contains(".instant-quote__workflow:not(:has(> .instant-quote__gltf-unit)):has(> [data-workflow-viewer]) > [data-workflow-parts-panel]", css, StringComparison.Ordinal);
        Assert.Contains("grid-row: 1 / -1", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: auto minmax(0, 1fr) auto", css, StringComparison.Ordinal);
        Assert.Contains("> p[role=\"alert\"]", css, StringComparison.Ordinal);
        Assert.Contains("grid-row: 2 / -1", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_height_regimes_keep_compact_native_scroll_and_tall_intrinsic_parts()
    {
        var css = ReadCss();
        const string compactLayout = """
            .instant-quote__workflow:has(> .instant-quote__gltf-unit):has(> [data-workflow-viewer]) {
                grid-template-rows: auto minmax(8rem, 1fr) auto;
                overflow-y: auto;
            }
        """;
        const string compactRail = """
            .instant-quote__workflow:has(> .instant-quote__gltf-unit):has(> [data-workflow-viewer]) > [data-workflow-parts-panel] > [data-workflow-parts] {
                flex: 1 1 auto;
                min-height: 44px;
            }
        """;
        const string tallLayout = """
        @media (min-width: 64rem) and (min-height: 48rem) {
            .instant-quote__workflow:has(> .instant-quote__gltf-unit):has(> [data-workflow-viewer]) {
                grid-template-rows: auto minmax(8rem, 1fr) max-content;
            }

            .instant-quote__workflow:has(> .instant-quote__gltf-unit):has(> [data-workflow-viewer]) > [data-workflow-parts-panel] > [data-workflow-parts] {
                flex: 0 0 auto;
                max-height: none;
            }
        }
        """;
        const string configurationMinimum = """
            .instant-quote__workflow:has(> .instant-quote__gltf-unit):has(> [data-workflow-viewer]) > [data-workflow-configuration] > [data-workflow-configuration-content] {
                min-height: 8rem;
            }
        """;

        Assert.Contains(compactLayout, css, StringComparison.Ordinal);
        Assert.Contains(compactRail, css, StringComparison.Ordinal);
        Assert.Contains(tallLayout, css, StringComparison.Ordinal);
        Assert.Contains(configurationMinimum, css, StringComparison.Ordinal);
    }

    [Fact]
    public void Material_details_reserve_both_terminal_price_rows_without_clipping_content()
    {
        var css = ReadCss();
        var selector = "[data-material-selector] .material-card__detail";
        var rule = System.Text.RegularExpressions.Regex.Match(css,
            @"(?m)^" + System.Text.RegularExpressions.Regex.Escape(selector) + @"\s*\{(?<declarations>[^}]*)\}");
        Assert.True(rule.Success);
        var declarations = rule.Groups["declarations"].Value;
        Assert.Contains("line-height: 1.2", declarations, StringComparison.Ordinal);
        Assert.Contains("min-height: calc(2lh + .1rem)", declarations, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?<!-)height\s*:", declarations);
        Assert.DoesNotContain("overflow: hidden", declarations, StringComparison.Ordinal);
        Assert.Contains(selector + ":has([data-price-status=\"unavailable\"]) { grid-template-columns: minmax(0, 1fr); row-gap: .1rem; }",
            css, StringComparison.Ordinal);
    }

    private static string ReadCss() => File.ReadAllText(Path.Combine(
        RepositoryRoot,
        "Legacy.Maliev.Web",
        "wwwroot",
        "src",
        "app",
        "css",
        "instant-quotation.css"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
