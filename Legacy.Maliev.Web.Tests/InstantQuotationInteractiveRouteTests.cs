using System.Reflection;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationInteractiveRouteTests
{
    private const string WorkflowNamespace = "Legacy.Maliev.Web.Components.Pages.InstantQuotation";

    [Fact]
    public void Route_RemainsStaticSsr_WithExactlyOneInteractiveServerWorkflowIsland()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var page = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "InstantQuotation",
            "InstantQuotationPage.razor"));
        var content = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.razor"));
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var renderModeOwners = Directory.EnumerateFiles(web, "*.razor", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("@rendermode", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains("@page \"/InstantQuotation/3D-Printing\"", page, StringComparison.Ordinal);
        Assert.Contains("<PageTitle>", page, StringComparison.Ordinal);
        Assert.Contains("<HeadContent>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", page, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", content, StringComparison.Ordinal);
        Assert.Equal(1, Count(content, "<InstantQuotationWorkflow"));
        Assert.Equal(1, Count(content, "@rendermode=\"InteractiveServer\""));
        Assert.Single(renderModeOwners);
        Assert.EndsWith("ThreeDimensionalPrintingEstimateContent.razor", renderModeOwners[0], StringComparison.Ordinal);
        Assert.Contains("AddInteractiveServerComponents", program, StringComparison.Ordinal);
        Assert.Contains("AddInteractiveServerRenderMode", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBlazorHub", program, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryJourney_IsUploadFirst_AndExposesAllWorkflowMarkers()
    {
        var source = ReadWorkflowSource();
        var document = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.razor"));

        Assert.Contains("Start by uploading a file", document, StringComparison.Ordinal);
        Assert.DoesNotContain("Height (mm)", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Solid volume", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Footprint", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("areaProfile", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perimeterProfile", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetEstimate", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetOrderTotal", source, StringComparison.OrdinalIgnoreCase);

        foreach (var marker in new[]
        {
            "data-workflow-upload",
            "data-workflow-viewer",
            "data-workflow-parts",
            "data-workflow-configuration",
            "data-workflow-dfm-status",
            "data-workflow-material-color-quantity",
            "data-workflow-part-price",
            "data-workflow-order-summary",
            "data-workflow-lead-time",
            "data-workflow-review",
            "data-workflow-customer-details",
            "data-workflow-submitted",
        })
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Workflow_UsesNativeAccessibleUploadAndFailClosedStatusSemantics()
    {
        var source = ReadWorkflowSource();

        Assert.Contains("<label for=\"instant-quote-files\"", source, StringComparison.Ordinal);
        Assert.Contains("<input id=\"instant-quote-files\"", source, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", source, StringComparison.Ordinal);
        Assert.Contains("multiple", source, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"@IsBusy", source, StringComparison.Ordinal);
        Assert.Contains("<button type=\"button\"", source, StringComparison.Ordinal);
        Assert.Contains("disabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick=", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<div role=\"button\"", source, StringComparison.OrdinalIgnoreCase);

        foreach (var forbidden in new[]
        {
            "sessionId",
            "uploadReference",
            "storagePath",
            "access_token",
            "refresh_token",
            "serviceToken",
            "credential",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WorkflowState_HasExactValues_AndMapsToExactVisibleSections()
    {
        var assembly = typeof(Program).Assembly;
        var stateType = assembly.GetType($"{WorkflowNamespace}.InstantQuotationWorkflowState");
        Assert.NotNull(stateType);
        Assert.True(stateType.IsEnum);
        Assert.Equal(
            ["Empty", "Uploading", "Uploaded", "Error", "MultiPart", "Configured", "Review", "CustomerDetails", "Submitted"],
            Enum.GetNames(stateType));

        var workflowType = assembly.GetType($"{WorkflowNamespace}.InstantQuotationWorkflow");
        Assert.NotNull(workflowType);
        var visibilityMethod = workflowType.GetMethod(
            "GetVisibleSections",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(visibilityMethod);

        AssertVisibility("Empty", upload: true);
        AssertVisibility("Uploading", upload: true);
        AssertVisibility("Uploaded", viewer: true, parts: true, configuration: true);
        AssertVisibility("Error", upload: true, error: true);
        AssertVisibility("MultiPart", viewer: true, parts: true, configuration: true);
        AssertVisibility("Configured", viewer: true, parts: true, configuration: true);
        AssertVisibility("Review", viewer: true, parts: true, configuration: true, review: true);
        AssertVisibility("CustomerDetails", customerDetails: true);
        AssertVisibility("Submitted", submitted: true);

        void AssertVisibility(
            string state,
            bool upload = false,
            bool error = false,
            bool viewer = false,
            bool parts = false,
            bool configuration = false,
            bool review = false,
            bool customerDetails = false,
            bool submitted = false)
        {
            var value = Enum.Parse(stateType, state);
            var visibility = visibilityMethod.Invoke(null, [value]);
            Assert.NotNull(visibility);
            Assert.Equal(upload, ReadBoolean(visibility, "Upload"));
            Assert.Equal(error, ReadBoolean(visibility, "Error"));
            Assert.Equal(viewer, ReadBoolean(visibility, "Viewer"));
            Assert.Equal(parts, ReadBoolean(visibility, "Parts"));
            Assert.Equal(configuration, ReadBoolean(visibility, "Configuration"));
            Assert.Equal(review, ReadBoolean(visibility, "Review"));
            Assert.Equal(customerDetails, ReadBoolean(visibility, "CustomerDetails"));
            Assert.Equal(submitted, ReadBoolean(visibility, "Submitted"));
        }
    }

    private static bool ReadBoolean(object target, string propertyName) =>
        (bool)(target.GetType().GetProperty(propertyName)?.GetValue(target)
            ?? throw new InvalidOperationException($"Visibility property {propertyName} was not found."));

    private static string ReadWorkflowSource()
    {
        var directory = Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation");
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(directory, "InstantQuotationWorkflow.razor")),
            File.ReadAllText(Path.Combine(directory, "InstantQuotationWorkflow.razor.cs")),
            File.ReadAllText(Path.Combine(directory, "InstantQuotationWorkflowState.cs")));
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
