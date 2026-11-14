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

    [Fact]
    public void FinderKeepsFinalSourceControllerScoringAndPresentationGuards()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Services", "ServicesContent.razor"));
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "service-finder.css"));
        var script = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "service-finder.js"));
        var controller = System.Text.RegularExpressions.Regex.Match(content,
            @"<main\b[^>]*\sdata-service-finder(?=\s|=|>)[^>]*>(?<body>[\s\S]*?)</main>");

        Assert.True(controller.Success, "The finder controller must own the questions and result cards.");
        var finder = controller.Groups["body"].Value;
        Assert.Contains("service-finder-title", finder, StringComparison.Ordinal);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(finder, "data-finder-step=\"[0-4]\"").Count);
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(finder, "data-finder-service=\"").Count);
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(finder, "service-finder-service-price").Count);
        Assert.Contains("ช่วยประเมินกระบวนการที่เกี่ยวข้องเบื้องต้น", finder, StringComparison.Ordinal);

        Assert.Matches(@"\.service-finder-shell\s*\{[^}]*align-items:\s*start", css);
        Assert.Matches(@"\.services-index-page \.maliev-page-header\s*\{[^}]*color:\s*#fff", css);
        Assert.Matches(@"\.service-finder-image\s*\{[^}]*height:\s*auto", css);
        Assert.Contains("@media (max-width: 47.99rem)", css, StringComparison.Ordinal);
        Assert.Matches(@"\.services-index-page\.maliev-page-main\s*\{[^}]*padding-bottom:\s*0", css);
        Assert.Matches(@"\.service-finder-results\s*\{[^}]*padding-block:[^}]*clamp\(1\.25rem,\s*2\.5vw,\s*2rem\)", css);
        Assert.Matches(@"\.service-finder-path\s*\{", css);
        Assert.Matches(@"\.service-finder-service-price\s*\{", css);
        Assert.Contains("grid-template-columns: minmax(0, .9fr) minmax(0, 1.1fr)", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-summary dt,", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", css, StringComparison.Ordinal);
        Assert.Contains("text-align: right", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", css, StringComparison.Ordinal);
        Assert.Contains("text-align: left", css, StringComparison.Ordinal);

        Assert.Contains("quantity-over-1000", script, StringComparison.Ordinal);
        Assert.Contains("service.id === 'injection') score -= 100", script, StringComparison.Ordinal);
        Assert.Contains("cnc-priority-precision", script, StringComparison.Ordinal);
        Assert.Contains("scanning-output-deviation", script, StringComparison.Ordinal);
        Assert.Contains("molding-priority-rigid", script, StringComparison.Ordinal);
        Assert.Contains("recommendationChain", script, StringComparison.Ordinal);
        Assert.Contains("files-image", script, StringComparison.Ordinal);
        Assert.Contains("aria-current", script, StringComparison.Ordinal);
        Assert.Contains("scrollFinderElement", script, StringComparison.Ordinal);

        Assert.Contains("data-finder-progress-optional", finder, StringComparison.Ordinal);
        Assert.Contains("data-finder-optional-progress", finder, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr)", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-progress-optional", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-progress li[data-finder-optional-progress]", css, StringComparison.Ordinal);
        Assert.Contains("function shouldIncludeOptionalStep(step)", script, StringComparison.Ordinal);
        Assert.Contains("return allSteps.filter(shouldIncludeOptionalStep);", script, StringComparison.Ordinal);
        Assert.Contains("if (step.hasAttribute('data-finder-optional') && !shouldIncludeOptionalStep(step))", script, StringComparison.Ordinal);
        Assert.Contains("if (progressOptionalLabel) progressOptionalLabel.hidden = !currentStep || !currentStep.hasAttribute('data-finder-optional');", script, StringComparison.Ordinal);
        Assert.Contains("direction === 'service-machining'", script, StringComparison.Ordinal);
        Assert.Contains("workflow === 'workflow-3d-scanning'", script, StringComparison.Ordinal);
        Assert.Contains("direction === 'service-molding'", script, StringComparison.Ordinal);
        Assert.Contains("guidanceReason", script, StringComparison.Ordinal);
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
