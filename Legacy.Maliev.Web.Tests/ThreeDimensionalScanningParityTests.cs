using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class ThreeDimensionalScanningParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ThreeDimensionalScanningParityTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public async Task ThreeDimensionalScanningRoute_RendersTheCurrentWorkflowDeliverablesAndChecklist()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/services/3d-scanning?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"scanning-selection-guide\"", source, StringComparison.Ordinal);
        Assert.Contains("data-scanning-workflow", source, StringComparison.Ordinal);
        Assert.Equal(5, WorkflowStepRegex().Matches(source).Count);
        Assert.Equal(5, WorkflowImageRegex().Matches(source).Count);
        Assert.Equal(4, DeliverableCardRegex().Matches(source).Count);
        Assert.Equal(4, DeliverableImageRegex().Matches(source).Count);
        Assert.Equal(5, ChecklistCardRegex().Matches(source).Count);
        Assert.Contains("What You Receive at Each Stage", source, StringComparison.Ordinal);
        Assert.Contains("Can I order only 3D scanning and receive a DWG?", source, StringComparison.Ordinal);
        Assert.Contains("Print / save as PDF", source, StringComparison.Ordinal);
        Assert.Contains("window.print()", source, StringComparison.Ordinal);
        Assert.Contains("src=\"/src/app/js/scanning-workflow.js\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDimensionalScanningSource_ContainsTheResponsivePresentationContractAndAssets()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var component = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "ThreeDimensionalScanningContent.razor"));
        var styles = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "service-pages.css"));
        var script = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "scanning-workflow.js"));

        Assert.Contains("RouteOwner", component, StringComparison.Ordinal);
        Assert.Contains("ServiceBreadcrumb", component, StringComparison.Ordinal);
        Assert.Contains("ServiceLocation", component, StringComparison.Ordinal);
        Assert.Contains("class=\"service-page-toc\"", component, StringComparison.Ordinal);
        Assert.Contains("data-scanning-step", component, StringComparison.Ordinal);
        Assert.Contains("scanning-checklist-item", component, StringComparison.Ordinal);
        Assert.Contains("scanning-workflow-timeline.is-active", styles, StringComparison.Ordinal);
        Assert.Contains("scanning-workflow-step.is-visible", styles, StringComparison.Ordinal);
        Assert.Contains("@media print", styles, StringComparison.Ordinal);
        Assert.Contains("#scanning-onsite-checklist .scanning-checklist-actions { display: none; }", styles, StringComparison.Ordinal);
        Assert.Contains("IntersectionObserver", script, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", script, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "wwwroot/src/images/services/scanning/art/scanning-art-clean-mesh.webp",
            "wwwroot/src/images/services/scanning/art/scanning-art-raw-capture.webp",
            "wwwroot/src/images/services/scanning/workflow/scanning-workflow-capture.webp",
            "wwwroot/src/images/services/scanning/workflow/scanning-workflow-clean-mesh.webp",
            "wwwroot/src/images/services/scanning/workflow/scanning-workflow-deviation-analysis.webp",
            "wwwroot/src/images/services/scanning/workflow/scanning-workflow-reverse-engineering.webp"
        })
        {
            var path = Path.Combine(root, "Legacy.Maliev.Web", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Expected scanning asset '{path}'.");
            Assert.True(new FileInfo(path).Length > 0, $"Expected scanning asset '{path}' to be non-empty.");
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

    [GeneratedRegex("class=\"scanning-workflow-step\"")]
    private static partial Regex WorkflowStepRegex();

    [GeneratedRegex("class=\"scanning-workflow-image\"")]
    private static partial Regex WorkflowImageRegex();

    [GeneratedRegex("class=\"scanning-deliverable-card(?: |\\\")")]
    private static partial Regex DeliverableCardRegex();

    [GeneratedRegex("class=\"scanning-deliverable-image\"")]
    private static partial Regex DeliverableImageRegex();

    [GeneratedRegex("class=\"scanning-checklist-card\"")]
    private static partial Regex ChecklistCardRegex();
}
