using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationReviewEditParityTests
{
    [Fact]
    public async Task Review_RendersNativePartSelectionAndEditControlsWithOneActivePart()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var html = await RenderReviewAsync([Part(firstId, "first.stl"), Part(secondId, "second.stl")], secondId);

        Assert.Equal(2, Count(html, "data-review-select-part"));
        Assert.Equal(2, Count(html, "data-review-edit-part"));
        Assert.Equal(1, Count(html, "aria-pressed=\"true\""));
        Assert.Contains($"data-review-part-id=\"{secondId}\" data-selected=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"View first.stl\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Edit second.stl\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"button\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_ExposesLabeledPerPartSettingsWithoutLosingTheEditEscape()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var html = await RenderReviewAsync([Part(id, "part.stl")], id);

        Assert.Contains($"id=\"review-material-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"review-preference-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"review-color-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"review-quantity-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-review-edit-part", html, StringComparison.Ordinal);
        Assert.Contains("data-workflow-review-total", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_CollapsesMeasurementsButAnnouncesEachPartsDfmVerdict()
    {
        var clearId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var warningId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var html = await RenderReviewAsync(
            [Part(clearId, "clear.stl"), Part(warningId, "warning.stl", isManifold: false)],
            clearId);

        Assert.Equal(2, Count(html, "data-review-part-details"));
        Assert.Contains("data-review-dfm-status=\"clear\"", html, StringComparison.Ordinal);
        Assert.Contains("data-review-dfm-status=\"warning\"", html, StringComparison.Ordinal);
        Assert.Contains("Non-watertight mesh", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Manufacturing warning: Non-watertight mesh", html, StringComparison.Ordinal);
        Assert.Contains("No automatic DFM warnings were found", html, StringComparison.Ordinal);
        Assert.Contains("<summary", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_KeepsAsyncWallThicknessWarningOnItsPart()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var html = await RenderReviewAsync(
            [Part(id, "thin.stl")], id,
            _ => ["Thin walls may print with defects"]);

        Assert.Contains("data-review-dfm-status=\"warning\"", html, StringComparison.Ordinal);
        Assert.Contains("Thin walls may print with defects", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_PlacesPreliminaryDocumentBesideForwardActionAfterTheTotal()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var html = await RenderReviewAsync([Part(id, "part.stl")], id);
        var total = html.IndexOf("data-workflow-review-total", StringComparison.Ordinal);
        var actions = html.IndexOf("data-review-forward-actions", StringComparison.Ordinal);
        var document = html.IndexOf("id=\"preliminary-quotation-button\"", StringComparison.Ordinal);
        var continueAction = html.IndexOf("data-review-continue", StringComparison.Ordinal);

        Assert.True(total >= 0 && actions > total && document > actions && continueAction > document);
        Assert.Contains("data-review-back", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Print preliminary quotation\"", html, StringComparison.Ordinal);
        Assert.Matches(@">\s*PDF\s*</button>", html);
    }

    [Fact]
    public void Workflow_ReviewSelectionAndEditNavigationKeepTheRequestedPartActive()
    {
        var markup = ReadComponent("InstantQuotationWorkflow.razor");
        var code = ReadComponent("InstantQuotationWorkflow.razor.cs");

        Assert.Contains("SelectedPartId=\"@selectedPreviewPartId\"", markup, StringComparison.Ordinal);
        Assert.Contains("OnSelectPart=\"SelectReviewPartAsync\"", markup, StringComparison.Ordinal);
        Assert.Contains("OnEditPart=\"EditReviewPartAsync\"", markup, StringComparison.Ordinal);
        Assert.Contains("await SelectPreviewAsync(partId);", code, StringComparison.Ordinal);
        Assert.Contains("workflow?.ReturnToConfiguration();", code, StringComparison.Ordinal);
        Assert.Contains("pendingFocus = PendingWorkflowFocus.Configuration;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewControlsRemainTouchSafeResponsiveAndKeyboardVisible()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "css",
            "instant-quotation.css"));

        Assert.Contains(".instant-quote__review-thumbnail-button", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 44px", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
        Assert.Contains("[aria-pressed=\"true\"]", css, StringComparison.Ordinal);
        Assert.Contains(".instant-quote__review-edit", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 48rem)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewEditActionsHaveThaiLabelsWithoutChangingFileNames()
    {
        var resources = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.th.resx"));

        Assert.Contains("<data name=\"Edit\" xml:space=\"preserve\"><value>แก้ไข</value></data>", resources, StringComparison.Ordinal);
        Assert.Contains("<data name=\"Edit {0}\" xml:space=\"preserve\"><value>แก้ไข {0}</value></data>", resources, StringComparison.Ordinal);
    }

    private static async Task<string> RenderReviewAsync(
        IReadOnlyList<InstantQuotationWorkflowPartViewModel> parts,
        Guid selectedPartId,
        Func<Guid, IReadOnlyList<string>>? thicknessWarnings = null)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IJSRuntime, NullJsRuntime>()
            .AddSingleton<IWebHostEnvironment>(_ => new TestWebHostEnvironment
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name ?? "Legacy.Maliev.Web",
                ContentRootPath = FindRepositoryRoot(),
                WebRootPath = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot"),
            })
            .BuildServiceProvider();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(services, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Parts"] = parts,
                ["SelectedPartId"] = selectedPartId,
                ["ThicknessWarnings"] = thicknessWarnings ?? ((Guid _) => Array.Empty<string>()),
            });
            var output = await renderer.RenderComponentAsync<InstantQuotationReview>(parameters);
            return output.ToHtmlString();
        });
    }

    private static InstantQuotationWorkflowPartViewModel Part(Guid id, string fileName, bool isManifold = true) => new(
        id,
        Guid.NewGuid(),
        fileName,
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
            10,
            1_000,
            100,
            [10],
            [10],
            12,
            1,
            isManifold),
        new InstantQuotationPartConfiguration("PLA", "Black", 1),
        null);

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadComponent(string name) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Legacy.Maliev.Web",
        "Components",
        "Pages",
        "InstantQuotation",
        name));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class NullJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Testing";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
