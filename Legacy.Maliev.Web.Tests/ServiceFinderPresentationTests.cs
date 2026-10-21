namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceFinderPresentationTests
{
    [Fact]
    public void ServicesDirectoryKeepsAnAccessibleProgressiveFinderSurface()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Services", "ServicesContent.razor"));
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "service-finder.css"));

        Assert.Contains("class=\"service-finder\"", content, StringComparison.Ordinal);
        Assert.Contains("data-service-finder", content, StringComparison.Ordinal);
        Assert.Contains("role=\"progressbar\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"false\"", content, StringComparison.Ordinal);
        Assert.Contains("data-finder-skip-to-results", content, StringComparison.Ordinal);
        Assert.Contains("data-finder-quotation-link", content, StringComparison.Ordinal);
        Assert.Contains("href=\"/quotation\"", content, StringComparison.Ordinal);
        Assert.Contains(".service-finder-options", css, StringComparison.Ordinal);
        Assert.Contains("display: grid;", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-option:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-option.is-selected", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);

        Assert.Contains("data-finder-suggestion-label", content, StringComparison.Ordinal);
        Assert.Contains("data-finder-suggestion-description", content, StringComparison.Ordinal);
        var script = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "service-finder.js"));
        Assert.Contains("files-real-part", script, StringComparison.Ordinal);
        Assert.Contains("performance-strength", script, StringComparison.Ordinal);
        Assert.Contains("environment-outdoor", script, StringComparison.Ordinal);
        Assert.Contains("getActiveSteps", script, StringComparison.Ordinal);
        Assert.Contains("hasCoreAnswers", script, StringComparison.Ordinal);
        Assert.Contains("renderMaterialGuidance", script, StringComparison.Ordinal);
        Assert.Contains("service_finder_results_viewed", script, StringComparison.Ordinal);
        Assert.Contains("getContextualServiceIds", script, StringComparison.Ordinal);
        Assert.Contains("getOptionContext", script, StringComparison.Ordinal);
        Assert.Contains("refreshOptionContext", script, StringComparison.Ordinal);
        Assert.Contains("material-silicone", script, StringComparison.Ordinal);
        Assert.Contains("data-finder-suggestion", script, StringComparison.Ordinal);
        Assert.Contains(".service-finder-option.is-suggested", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-option[hidden]", css, StringComparison.Ordinal);
        Assert.Contains("service-finder-shortcuts", css, StringComparison.Ordinal);
        Assert.Contains("service-finder-material-guidance", css, StringComparison.Ordinal);

        Assert.Contains("scrollFinderElement", script, StringComparison.Ordinal);
        Assert.Contains("service_finder_started", script, StringComparison.Ordinal);
        Assert.Contains("service_finder_step_completed", script, StringComparison.Ordinal);
        Assert.Contains("service_finder_completed", script, StringComparison.Ordinal);
        Assert.Contains("service_finder_recommendation_clicked", script, StringComparison.Ordinal);
        Assert.Contains("recommended_service_ids: state.recommendedIds.join(',')", script, StringComparison.Ordinal);
        Assert.Contains("finder_path: state.finderPath.join('>')", script, StringComparison.Ordinal);
        Assert.Contains("handoffUrl.searchParams.set('finder_recommendations', state.recommendedIds.join(','));", script, StringComparison.Ordinal);
        Assert.Contains("service-finder-step-in", css, StringComparison.Ordinal);
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
}
