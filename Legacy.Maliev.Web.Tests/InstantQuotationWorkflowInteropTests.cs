using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationWorkflowInteropTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Workflow_renders_accessible_canvas_and_wired_controls()
    {
        var markup = Read("Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor");
        var code = Read("Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor.cs");

        Assert.Contains("<canvas", markup, StringComparison.Ordinal);
        Assert.Contains("aria-label=", markup, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", markup, StringComparison.Ordinal);
        Assert.Contains("ResetPreviewAsync", markup, StringComparison.Ordinal);
        Assert.Contains("FitPreviewAsync", markup, StringComparison.Ordinal);
        Assert.Contains("FullscreenPreviewAsync", markup, StringComparison.Ordinal);
        Assert.Contains("/dist/instant-quotation-workflow.mjs", code, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync(StateHasChanged)", code, StringComparison.Ordinal);
        Assert.Contains("DisposeAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_only_passes_ui_local_correlation_to_javascript()
    {
        var code = Read("Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor.cs");
        var boundaryStart = code.IndexOf("private async Task<string[]> BeginPreviewSelectionAsync", StringComparison.Ordinal);
        var boundaryEnd = code.IndexOf("private async Task ChangeMaterialAsync", boundaryStart, StringComparison.Ordinal);
        var interopBoundary = code[boundaryStart..boundaryEnd];

        Assert.DoesNotContain("ProtectedSessionIdentity", interopBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("UploadReference", interopBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("OperationId", interopBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthoritativeGeometry", interopBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderQuote", interopBoundary, StringComparison.Ordinal);
        Assert.DoesNotContain("Customer", interopBoundary, StringComparison.Ordinal);
        Assert.Contains("PreviewCorrelationId", interopBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Asset_manifest_exposes_route_module_without_changing_global_scripts()
    {
        using var manifest = JsonDocument.Parse(Read("Legacy.Maliev.Web", "wwwroot", "dist", "asset-manifest.json"));
        var root = manifest.RootElement;
        Assert.Equal(["vendor.min.js", "app.min.js"], root.GetProperty("scripts").EnumerateArray().Select(static item => item.GetString()));
        Assert.Equal(
            "instant-quotation-workflow.mjs",
            root.GetProperty("routeScopedModules").GetProperty("instantQuotationWorkflow").GetString());
    }

    [Fact]
    public void Global_entry_does_not_import_instant_quotation_or_three()
    {
        var entry = Read("Legacy.Maliev.Web", "assets", "app-entry.js");
        Assert.DoesNotContain("workflow-interop", entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("model-viewer", entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("three", entry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workflow_owner_resolution_uses_circuit_authentication_state()
    {
        var code = Read("Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor.cs");

        Assert.Contains("GetService<AuthenticationStateProvider>()", code, StringComparison.Ordinal);
        Assert.Contains("GetAuthenticationStateAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IHttpContextAccessor", code, StringComparison.Ordinal);
        Assert.Contains("ResolveOwnerIdentity(principal)", code, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

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
