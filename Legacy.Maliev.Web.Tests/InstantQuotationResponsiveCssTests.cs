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
        Assert.DoesNotContain("@media (max-width:", css, StringComparison.Ordinal);
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
