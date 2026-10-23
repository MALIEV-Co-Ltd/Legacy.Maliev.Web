using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public partial class InstantQuotationWorkflow : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IServiceProvider Services { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Parameter]
    public InstantQuotationWorkflowState InitialState { get; set; } = InstantQuotationWorkflowState.Empty;

    [Parameter]
    public InstantQuotationCustomerDisplayModel CustomerModel { get; set; } = InstantQuotationCustomerDisplayModel.Empty;

    // Untrusted Development presentation only: never inserted into workflow.Parts or its session.
    private readonly Dictionary<Guid, DevelopmentPreviewPart> developmentPreviews = [];
    private readonly Dictionary<Guid, InstantQuotationGeometryClaim> developmentClaims = [];
    private readonly HashSet<Guid> developmentRetries = [];
    private long developmentPreviewGeneration;
    private long developmentSelectionGeneration;
    private ElementReference developmentPreviewCanvas;
    private DevelopmentPreviewStep developmentStep;
    private OrderQuote? developmentTotal;
    private AdditiveLeadTimeRange? developmentLeadTime;
    private bool DevelopmentPreviewEnabled =>
        Services.GetService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>()?.IsDevelopment() == true;
    private bool HasDevelopmentPreview => DevelopmentPreviewEnabled && developmentPreviews.Count > 0;
    private bool DevelopmentPriceAvailable => developmentPreviews.Count > 0
        && developmentPreviews.Values.All(part => part.Price is not null)
        && developmentTotal is not null && developmentLeadTime is not null;

    private readonly string analyticsWorkflowId = Guid.NewGuid().ToString("N");
    private InstantQuotationWorkflowCoordinator? workflow;
    private bool initializationFailed;
    private InputFile? fileInput;
    private ElementReference workflowSection;
    private ElementReference previewCanvas;
    private ElementReference incompletePreviewCanvas;
    private string? incompletePreviewKey;
    private bool incompletePreviewAttached;
    private bool incompletePreviewMeasured;
    private long incompletePreviewRevision;
    private bool disposed;
    private IDisposable? materialDisplaySubscription;
    private long minimumDisplayGeneration;
    private MaterialDisplayRenderProjection? materialDisplayRender;
    private readonly Dictionary<Guid, InstantQuotationPartConfiguration> displayIntents = [];
    private readonly Dictionary<Guid, (long Revision, string Raw)> quantityDrafts = [];
    private long quantityDraftRevision;
    private ElementReference configurationSection;
    private ElementReference reviewSection;
    private ElementReference customerDetailsSection;
    private IJSObjectReference? previewModule;
    private IJSObjectReference? previewInterop;
    private DotNetObjectReference<InstantQuotationWorkflow>? previewStatusReporter;
    private Task? previewInitialization;
    private readonly Dictionary<Guid, string> previewKeys = [];
    private readonly Dictionary<Guid, ThicknessStatus> thicknessStatuses = [];
    private readonly Dictionary<Guid, long> thicknessToggleRevisions = [];
    private readonly Dictionary<Guid, InstantQuotationBoundPhysicalAnalysisResult> physicalAnalyses = [];
    private CancellationTokenSource? physicalAnalysisCancellation;
    private Guid? physicalAnalysisPartId;
    private long physicalAnalysisGeneration;
    private readonly QuantityEditState quantityEdits = new();
    private readonly SemaphoreSlim uploadBatchGate = new(1, 1);
    private IInstantQuotationAnalyticsTracker analytics = NoOpInstantQuotationAnalyticsTracker.Instance;
    private bool previewAttached;
    private bool previewUnavailable;
    private string? uploadSelectionError;
    private bool batchInProgress;
    private readonly Dictionary<string, PendingLocalPreview> pendingPreviewSelections = [];
    private sealed record PendingLocalPreview(string Name, long Generation);
    private readonly Dictionary<string, PendingLocalConfiguration> pendingConfigurations = [];
    private string? selectedPendingPreviewKey;
    private sealed class PendingLocalConfiguration(string name, long generation)
    {
        public string Name { get; } = name;
        public long Generation { get; } = generation;
        public InstantQuotationPartConfiguration Configuration { get; set; } = new("PLA", "Black", 1);
        public string Quantity { get; set; } = "1";
        public long Revision { get; set; }
        public bool Modified { get; set; }
        public bool Applying { get; set; }
        public Guid? LocalId { get; set; }
        public Guid? PartId { get; set; }
    }
    private PendingLocalConfiguration? SelectedPendingConfiguration =>
        selectedPendingPreviewKey is { } key ? pendingConfigurations.GetValueOrDefault(key) : null;
    private readonly RepricingActivity repricing = new();
    private Guid? selectedPreviewPartId;
    private Guid? selectedPreviewUploadId;
    private string? selectedFailedPreviewKey;
    private readonly Dictionary<string, string> localPreviewThumbnails = [];
    private Guid? autoPreviewLocalId;
    private Guid? revealPreviewPartId;
    private long previewSelectionGeneration;
    private sealed record LocalPreviewFailure(string Name, string Category);
    private readonly Dictionary<string, LocalPreviewFailure> failedPreviewSelections = [];
    private readonly HashSet<Guid> selectedBulkParts = [];
    private ElementReference bulkSelectAll;
    private bool bulkApplying;
    private PendingWorkflowFocus pendingFocus;
    private long reviewNavigationRevision;

    private ThicknessStatus? SelectedThickness => !HasLocalPreviewSelection && selectedPreviewPartId is { } id
        && thicknessStatuses.TryGetValue(id, out var status) ? status : null;

    private bool ThicknessNeedsAttention => thicknessStatuses.Values.Any(
        status => status.Warning || status.Incomplete);

    private InstantQuotationWorkflowState State => initializationFailed
        ? InstantQuotationWorkflowState.Error
        : workflow?.State ?? InitialState;

    private bool HasLocalFileFailures => failedPreviewSelections.Count > 0
        || Uploads.Any(upload => upload.Status is InstantQuotationWorkflowUploadStatus.Error
            && Parts.All(part => part.PreviewCorrelationId != upload.LocalId));

    private bool HasLocalPreviewSelection => selectedPendingPreviewKey is not null || selectedPreviewUploadId is not null || selectedFailedPreviewKey is not null;

    private string? SelectedLocalPreviewKey => selectedPendingPreviewKey ?? selectedFailedPreviewKey
        ?? (selectedPreviewUploadId is { } localId && previewKeys.TryGetValue(localId, out var key) ? key : null);

    private (string Name, string Message)? SelectedFileError
    {
        get
        {
            if (disposed) return null;
            if (selectedFailedPreviewKey is { } key && failedPreviewSelections.TryGetValue(key, out var failure))
                return (failure.Name, LocalPreviewFailureMessage(failure));
            var upload = selectedPreviewUploadId is { } id ? Uploads.FirstOrDefault(item => item.LocalId == id) : null;
            return upload?.Status is InstantQuotationWorkflowUploadStatus.Error
                && Parts.All(part => part.PreviewCorrelationId != upload.LocalId)
                ? (upload.DisplayFileName, Localizer[upload.ProblemCategory is InstantQuotationProblemCategory.Validation
                    ? "Invalid file. Repair the model and reupload it."
                    : "The file could not be uploaded. Try again."])
                : null;
        }
    }

    private string LocalPreviewFailureMessage(LocalPreviewFailure failure) => Localizer[failure.Category switch
    {
        "unit_required" => "Choose units before importing GLB/GLTF",
        "unreadable" => "Invalid file. Repair the model and reupload it.",
        "complexity" => "This file exceeds the analysis limits. Simplify the mesh or contact our engineers for a quotation.",
        _ => "3D file processing is unavailable. Try again or contact our engineers for a quotation.",
    }].Value;

    private async Task<string> ReadPreviewFailureCategoryAsync(string key)
    {
        var interop = previewInterop;
        if (disposed || interop is null) return "unavailable";
        try
        {
            var category = await interop.InvokeAsync<string?>("getFailureCategory", key);
            return category is "unreadable" or "complexity" or "unit_required" ? category : "unavailable";
        }
        catch (JSException) { return "unavailable"; }
    }

    private string? LocalThumbnail(string? key) => key is not null ? localPreviewThumbnails.GetValueOrDefault(key) : null;

    private WorkflowSectionVisibility VisibleSections
    {
        get
        {
            if (HasDevelopmentPreview && Parts.Count == 0)
                return new(Upload: true, Error: State is InstantQuotationWorkflowState.Error, Parts: true);
            if (pendingPreviewSelections.Count > 0 || HasLocalFileFailures || incompletePreviewKey is not null)
                return new(Upload: true, Error: false, Viewer: true, Parts: true, Configuration: Parts.Count > 0 || SelectedPendingConfiguration is not null);
            if (Parts.Count == 0 && Uploads.Count > 0
                && State is (InstantQuotationWorkflowState.Error or InstantQuotationWorkflowState.Empty))
                return new(Upload: true, Error: State is InstantQuotationWorkflowState.Error, Parts: true);
            return (State is InstantQuotationWorkflowState.Error && Parts.Count > 0)
                || (State is InstantQuotationWorkflowState.Empty && HasMaterialDisplay && Parts.Count > 0)
                ? new(Upload: true, Error: State is InstantQuotationWorkflowState.Error, Viewer: true, Parts: true, Configuration: true)
                : GetVisibleSections(State);
        }
    }

    private bool IsBusy => State is InstantQuotationWorkflowState.Uploading;

    private bool IsRepricing => repricing.IsActive;

    private bool HasMaterialDisplay => workflow?.MaterialPriceDisplay.Entries.Count > 0;

    private string QuantityInputValue(InstantQuotationWorkflowPartViewModel part) =>
        quantityDrafts.TryGetValue(part.PartId, out var draft)
            ? draft.Raw
            : part.Configuration.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private IReadOnlyList<InstantQuotationMaterialPricingProgress> DisplayRows(InstantQuotationWorkflowPartViewModel part)
    {
        if (quantityEdits.IsPending(part.PartId) && !displayIntents.ContainsKey(part.PartId)
            || displayIntents.TryGetValue(part.PartId, out var intended) && intended != part.Configuration)
            return [];
        if (!IsRepricing && DisplayQuote(part) is not null) return [];
        var snapshot = workflow?.MaterialPriceDisplay;
        if (snapshot is not null && snapshot.Generation > minimumDisplayGeneration)
            return snapshot.Entries.Where(row => row.PartId == part.PartId).ToArray();
        return [];
    }

    private IReadOnlyList<InstantQuotationMaterialPricingProgress> CardPriceRows(InstantQuotationWorkflowPartViewModel part)
    {
        var current = DisplayRows(part);
        if (current.Count > 0) return current;
        if (!IsRepricing && DisplayQuote(part) is { } quote)
            return quote.MaterialPrices.Select(price => new InstantQuotationMaterialPricingProgress(
                part.PartId, price.MaterialKey,
                price.UnitPrice is null ? InstantQuotationMaterialPricingStatus.Unavailable : InstantQuotationMaterialPricingStatus.Completed,
                price.UnitPrice)).ToArray();
        return [];
    }

    private ValueTask MaterialDisplayChangedAsync(InstantQuotationMaterialPriceDisplaySnapshot snapshot, CancellationToken token)
    {
        if (disposed || token.IsCancellationRequested) return ValueTask.CompletedTask;
        return new(InvokeAsync(() =>
        {
            if (disposed || token.IsCancellationRequested || workflow is null
                || workflow.MaterialPriceDisplay.Generation != snapshot.Generation) return;
            var sections = VisibleSections;
            InstantQuotationWorkflowPartViewModel[] parts = sections.Configuration ? ConfigurationParts.ToArray() : [];
            var projection = new MaterialDisplayRenderProjection(snapshot.Generation, State, sections,
                HasMaterialDisplay, IsRepricing, parts, parts.SelectMany(DisplayRows).ToArray());
            if (materialDisplayRender is { } previous && projection.Matches(previous)) return;
            StateHasChanged();
            materialDisplayRender = projection;
        }));
    }

    private sealed record MaterialDisplayRenderProjection(
        long Generation,
        InstantQuotationWorkflowState State,
        WorkflowSectionVisibility Sections,
        bool HasDisplay,
        bool Repricing,
        InstantQuotationWorkflowPartViewModel[] Parts,
        InstantQuotationMaterialPricingProgress[] Rows)
    {
        public bool Matches(MaterialDisplayRenderProjection other) =>
            Generation == other.Generation && State == other.State && Sections == other.Sections
            && HasDisplay == other.HasDisplay && Repricing == other.Repricing
            && Parts.SequenceEqual(other.Parts) && Rows.SequenceEqual(other.Rows);
    }

    private bool InputDisabled => IsBusy || batchInProgress || bulkApplying || developmentRetries.Count > 0;

    private bool IsBulkEligible(InstantQuotationWorkflowPartViewModel part) =>
        Uploads.Any(upload => upload.LocalId == part.PreviewCorrelationId
            && upload.Status is InstantQuotationWorkflowUploadStatus.Uploaded)
        && ((part.Geometry.TopologyChecked && !part.Geometry.NonWatertight && !part.Geometry.NonManifold)
            || HasCurrentRecoveryQuote(part))
        && (!physicalAnalyses.TryGetValue(part.PartId, out var physical) || physical.IsReady);

    private bool HasCurrentRecoveryQuote(InstantQuotationWorkflowPartViewModel part)
    {
        if (disposed || (IsRepricing && !bulkApplying) || workflow is null
            || Services.GetService<FdmRuntimeProfileCatalog>() is not { } catalog
            || OrderQuote?.Parts.SingleOrDefault(line => line.PartId == part.PartId) is not { } quote
            || quote.Process != PrintProcess.Fdm
            || quote.MaterialKey != part.Configuration.MaterialKey || quote.Color != part.Configuration.Color
            || quote.Quantity != part.Configuration.Quantity || quote.BuildPreference != part.Configuration.BuildPreference
            || !double.IsFinite(quote.UnitPrice) || quote.UnitPrice <= 0
            || quote.PhysicalReceipt is not { GeometryReconstruction: { } reconstruction } receipt
            || !reconstruction.IsValid()
            || !catalog.TryResolveTrustedProfile(part.Configuration.MaterialKey, part.Configuration.BuildPreference, out var profile)
            || profile is null
            || workflow.CurrentPhysicalAnalysisBinding(part.PartId, catalog.ProfileVersion) is not { } binding)
        {
            return false;
        }

        // Only the current server quotation may relax topology eligibility; the original geometry and warnings remain intact.
        return receipt.SessionId == binding.SessionId && receipt.OwnerIdentity == binding.OwnerIdentity
            && receipt.GltfUnitPolicy == binding.GltfUnitPolicy
            && receipt.PartId == binding.PartId && receipt.FileId == binding.FileId
            && string.Equals(receipt.UploadSha256, binding.UploadSha256, StringComparison.OrdinalIgnoreCase)
            && receipt.ConfiguredMaterialKey == part.Configuration.MaterialKey && receipt.MaterialKey == binding.MaterialKey
            && receipt.Quantity == binding.Quantity && receipt.BuildPreference == binding.BuildPreference
            && receipt.ProfileVersion == binding.ProfileVersion && receipt.ProfileSha256 == profile.ResolvedProfileSha256
            && receipt.AnalysisVersion == Legacy.Maliev.Web.Application.Pricing.Simulation.FdmSimulationEngine.AnalysisVersion
            && double.IsFinite(receipt.DepositedMm3) && receipt.DepositedMm3 > 0
            && double.IsFinite(receipt.SupportMm3) && receipt.SupportMm3 >= 0
            && double.IsFinite(receipt.MotionSeconds) && receipt.MotionSeconds > 0
            && double.IsFinite(receipt.BoundingCm3PerUnit) && receipt.BoundingCm3PerUnit > 0;
    }

    private IReadOnlyList<InstantQuotationWorkflowPartViewModel> BulkParts => Parts.Where(IsBulkEligible).ToArray();

    private int SelectedBulkCount => BulkParts.Count(part => selectedBulkParts.Contains(part.PartId));

    private bool AllBulkPartsSelected => BulkParts.Count > 0 && SelectedBulkCount == BulkParts.Count;

    private bool BulkSelectionMixed => SelectedBulkCount > 0 && !AllBulkPartsSelected;

    private bool CanApplyBulkMaterial => !disposed && !InputDisabled && !IsRepricing && VisibleSections.Configuration
        && SelectedBulkCount > 0 && !Parts.Any(part => quantityEdits.IsPending(part.PartId))
        && ConfigurationParts.SingleOrDefault() is { } active && IsBulkEligible(active);

    private void SetPartSelected(Guid partId, ChangeEventArgs args)
    {
        if (disposed || InputDisabled || IsRepricing || !BulkParts.Any(part => part.PartId == partId)) return;
        if (args.Value is true) selectedBulkParts.Add(partId);
        else selectedBulkParts.Remove(partId);
        StateHasChanged();
    }

    private void SelectAllParts(ChangeEventArgs args)
    {
        if (disposed || InputDisabled || IsRepricing) return;
        selectedBulkParts.Clear();
        if (args.Value is true) selectedBulkParts.UnionWith(BulkParts.Select(part => part.PartId));
        StateHasChanged();
    }

    private async Task ApplyMaterialToSelectedAsync()
    {
        if (!CanApplyBulkMaterial) return;
        var active = ConfigurationParts.SingleOrDefault();
        if (active is null || !IsBulkEligible(active)) return;
        var material = active.Configuration.MaterialKey;
        var ids = BulkParts.Where(part => selectedBulkParts.Contains(part.PartId))
            .Select(part => part.PartId).ToArray();
        bulkApplying = true;
        try
        {
            await RepriceAsync(async () =>
            {
                foreach (var id in ids)
                {
                    if (disposed) return;
                    var part = Parts.SingleOrDefault(candidate => candidate.PartId == id);
                    if (part is null || !IsBulkEligible(part) || part.Configuration.MaterialKey == material) continue;
                    InvalidatePhysicalAnalysis(id);
                    var color = GetColors(material).First();
                    await workflow!.UpdateConfigurationAsync(id, material, color, part.Configuration.Quantity, default);
                    await InvokePreviewAsync("setThicknessMaterial", ViewerPartKey(id), material);
                    await UpdatePartAppearanceAsync(id);
                }
            });
        }
        finally
        {
            bulkApplying = false;
            if (!disposed) await InvokeAsync(StateHasChanged);
        }
    }

    private bool CanEnterReview
    {
        get
        {
            if (disposed || InputDisabled || IsRepricing || pendingConfigurations.Count > 0 || failedPreviewSelections.Count > 0
                || workflow?.State is not InstantQuotationWorkflowState.Configured
                || workflow.OrderQuote is null) return false;
            var parts = Parts;
            return !parts.Any(part => quantityEdits.IsPending(part.PartId))
                && displayIntents.All(intent => parts.Any(part => part.PartId == intent.Key
                    && part.Configuration == intent.Value));
        }
    }

    private IReadOnlyList<InstantQuotationWorkflowUploadViewModel> Uploads => workflow?.Uploads ?? [];

    private IReadOnlyList<InstantQuotationWorkflowPartViewModel> Parts => workflow?.Parts ?? [];

    private IReadOnlyList<InstantQuotationWorkflowPartViewModel> ConfigurationParts
    {
        get
        {
            if (SelectedPendingConfiguration is not null) return [];
            var selected = selectedPreviewPartId is { } selectedId
                ? Parts.FirstOrDefault(part => part.PartId == selectedId)
                : null;
            return selected is null ? Parts.Take(1).ToArray() : [selected];
        }
    }

    private IReadOnlyList<InstantQuotationWorkflowMaterialOption> Materials => workflow?.Materials ?? [];

    private bool HasUnacceptedConfiguration(InstantQuotationWorkflowPartViewModel part) =>
        pendingConfigurations.Values.Any(intent => intent.LocalId == part.PreviewCorrelationId && intent.Modified)
        || quantityEdits.IsPending(part.PartId)
        || displayIntents.TryGetValue(part.PartId, out var intended) && intended != part.Configuration;

    private InstantQuotationPartQuote? DisplayQuote(InstantQuotationWorkflowPartViewModel part) =>
        HasUnacceptedConfiguration(part) ? null : part.Quote;

    private InstantQuotationOrderQuote? OrderQuote =>
        Parts.Any(HasUnacceptedConfiguration) ? null : workflow?.OrderQuote;

    private bool HasConfigurationWarnings => ConfigurationParts.Any(HasManufacturingWarning);

    private bool HasManufacturingWarning(InstantQuotationWorkflowPartViewModel part)
    {
        var geometry = part.Geometry;
        var maximumDimension = Math.Max(geometry.DimensionXmm, Math.Max(geometry.DimensionYmm, geometry.DimensionZmm));
        return !geometry.TopologyChecked
            || geometry.NonWatertight
            || geometry.NonManifold
            || geometry.BodyCount > 1
            || maximumDimension is > 0 and < 3
            || maximumDimension > 350
            || (thicknessStatuses.TryGetValue(part.PartId, out var thickness)
                && (thickness.Warning || thickness.Incomplete));
    }

    private string LeadTime => OrderQuote is null
        ? "—"
        : Localizer["{0}–{1} business days", OrderQuote.LeadTimeMinimumDays, OrderQuote.LeadTimeMaximumDays];

    private string StatusMessage => State switch
    {
        InstantQuotationWorkflowState.Empty => Localizer["No file selected"],
        InstantQuotationWorkflowState.Uploading => Localizer["Uploading files…"],
        InstantQuotationWorkflowState.Uploaded => Localizer["File uploaded. Configure the part to continue."],
        InstantQuotationWorkflowState.Error => Localizer["The file could not be uploaded. Try again."],
        InstantQuotationWorkflowState.MultiPart => Localizer["Multiple parts are ready to configure."],
        InstantQuotationWorkflowState.Configured => Localizer["Part configuration is ready for review."],
        InstantQuotationWorkflowState.Review => Localizer["Review the order before entering customer details."],
        InstantQuotationWorkflowState.CustomerDetails => Localizer["Enter customer details to continue."],
        InstantQuotationWorkflowState.Submitted => CustomerModel.SubmissionStatus switch
        {
            InstantQuotationCustomerDisplayModel.PartialStatus =>
                Localizer["Your request was saved. File processing is pending. Do not resubmit."],
            InstantQuotationCustomerDisplayModel.RejectedStatus =>
                Localizer["Your request was not submitted."],
            _ => Localizer["Your quotation request was submitted."],
        },
        _ => throw new InvalidOperationException("Unknown instant quotation workflow state."),
    };

    private string PreviewStatusMessage => previewUnavailable
        ? Localizer["3D preview is unavailable. You can continue with your quotation."]
        : Localizer["Use arrow keys to rotate, plus or minus to zoom, 0 to reset, and Home to fit."];

    private string SubmittedHeading => CustomerModel.SubmissionStatus switch
    {
        InstantQuotationCustomerDisplayModel.PartialStatus => Localizer["Request saved"],
        InstantQuotationCustomerDisplayModel.RejectedStatus => Localizer["Request not submitted"],
        _ => Localizer["Ready for manufacturing"],
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Input binding can yield while a retry changes state before its new canvas renders.
        var renderedDevelopmentGeneration = developmentPreviewGeneration;
        var renderedDevelopmentCanvas = developmentPreviewCanvas;
        if (firstRender)
        {
            await EnsurePreviewInteropAsync();
        }

        if (disposed) return;
        await EnsurePreviewInputBoundAsync();
        if (disposed) return;
        selectedBulkParts.IntersectWith(BulkParts.Select(part => part.PartId));
        if (VisibleSections.Parts && previewInterop is not null)
        {
            await InvokePreviewAsync("setBulkSelectionState", bulkSelectAll, BulkSelectionMixed);
        }

        if (HasDevelopmentPreview && previewInterop is not null)
        {
            foreach (var part in developmentPreviews.Values.Where(part => !part.Attached).ToArray())
            {
                try
                {
                    var generation = renderedDevelopmentGeneration;
                    var canvas = renderedDevelopmentCanvas;
                    if (disposed || generation != developmentPreviewGeneration || !canvas.Equals(developmentPreviewCanvas))
                        break;
                    var attached = await previewInterop.InvokeAsync<bool>(
                        "retainDevelopmentPreview", part.Key, canvas, generation);
                    if (!disposed && developmentPreviews.TryGetValue(part.LocalId, out var current)
                        && ReferenceEquals(part, current))
                    {
                        if (generation == developmentPreviewGeneration && canvas.Equals(developmentPreviewCanvas))
                            part.Attached = attached;
                        else
                            await InvokeAsync(StateHasChanged);
                    }
                }
                catch (JSException)
                {
                    await ReportPreviewUnavailableAsync();
                }
            }
        }

        if (incompletePreviewKey is { } incompleteKey && !incompletePreviewAttached && previewInterop is not null)
        {
            try
            {
                var retained = await previewInterop.InvokeAsync<bool>(
                    "retainIncompletePreview", incompleteKey, incompletePreviewCanvas);
                if (!disposed && incompletePreviewKey == incompleteKey)
                {
                    var eligible = retained && await previewInterop.InvokeAsync<bool>("isIncompletePreview", incompleteKey);
                    if (!disposed && incompletePreviewKey == incompleteKey)
                    {
                        if (eligible)
                        {
                            incompletePreviewAttached = true;
                            if (SelectedLocalPreviewKey == incompleteKey)
                                await InvokePreviewAsync("selectLocalPreview", incompleteKey);
                        }
                        else
                        {
                            await RemoveIncompletePreviewAsync();
                            if (!disposed)
                            {
                                await InvokeAsync(StateHasChanged);
                            }
                        }
                    }
                }
            }
            catch (JSException)
            {
                if (!disposed && incompletePreviewKey == incompleteKey)
                {
                    await RemoveIncompletePreviewAsync();
                    if (!disposed)
                    {
                        await InvokeAsync(StateHasChanged);
                    }
                }
            }
        }

        if (incompletePreviewKey is { } thumbnailKey && incompletePreviewAttached
            && !localPreviewThumbnails.ContainsKey(thumbnailKey) && previewInterop is not null)
        {
            try
            {
                var source = await previewInterop.InvokeAsync<string?>("getLocalThumbnail", thumbnailKey);
                if (!disposed && incompletePreviewKey == thumbnailKey
                    && source is { Length: <= 262144 } && source.StartsWith("data:image/webp;base64,", StringComparison.Ordinal))
                {
                    localPreviewThumbnails[thumbnailKey] = source;
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (JSException) { /* The local error remains actionable without a thumbnail. */ }
        }

        if (!VisibleSections.Viewer && Parts.Count == 0 && previewAttached && previewInterop is not null)
        {
            try
            {
                var detached = await previewInterop.InvokeAsync<bool>("detachEmptyViewer");
                if (!disposed && detached)
                {
                    previewAttached = false;
                    if (VisibleSections.Viewer) await InvokeAsync(StateHasChanged);
                }
            }
            catch (JSException) { await ReportPreviewUnavailableAsync(); }
        }

        if (VisibleSections.Viewer && !previewAttached && previewInterop is not null)
        {
            try
            {
                await previewInterop.InvokeVoidAsync("attach", previewCanvas);
                previewAttached = true;
                await ReconcilePreviewsAsync();
            }
            catch (JSException)
            {
                await ReportPreviewUnavailableAsync();
            }
        }

        if (workflow is not null)
        {
            var milestones = GetVisibleAnalyticsMilestones(
                VisibleSections.Configuration,
                VisibleSections.Review,
                workflow.HasCompleteAuthoritativeEstimate,
                workflow.HasCompleteAuthoritativeQuote,
                workflow.AuthoritativeQuoteRevision);
            if (milestones.EstimateShown)
            {
                await analytics.RecordEstimateShownAsync(workflow.AuthoritativeQuoteRevision);
            }

            if (milestones.ReviewReached)
            {
                await analytics.RecordReviewReachedAsync(workflow.AuthoritativeQuoteRevision);
            }
        }

        if ((VisibleSections.Parts || VisibleSections.Review || VisibleSections.CustomerDetails) && previewInterop is not null)
        {
            try
            {
                await previewInterop.InvokeVoidAsync("renderReviewThumbnails", workflowSection);
            }
            catch (JSException)
            {
                await ReportPreviewUnavailableAsync();
            }
        }

        if (revealPreviewPartId is { } revealId && previewInterop is not null)
        {
            revealPreviewPartId = null;
            if (!disposed && selectedPreviewPartId == revealId && Parts.Any(part => part.PartId == revealId))
            {
                await InvokePreviewAsync("revealPart", workflowSection, revealId.ToString("D"));
            }
        }

        await FocusPendingSectionAsync();
    }

    protected override async Task OnInitializedAsync()
    {
        if (InitialState is InstantQuotationWorkflowState.Submitted
            or InstantQuotationWorkflowState.CustomerDetails)
        {
            return;
        }

        analytics = Services.GetService<IInstantQuotationAnalyticsTracker>()
            ?? NoOpInstantQuotationAnalyticsTracker.Instance;
        var sessionStore = Services.GetService<IInstantQuotationSessionStore>();
        var uploadClient = Services.GetService<IInstantQuotationUploadClient>();
        var pricingService = Services.GetService<IInstantQuotationPricingService>();
        var authoritativePricingService = Services.GetService<IInstantQuotationAuthoritativePricingService>();
        var quoteTicketService = Services.GetService<IInstantQuotationQuoteTicketService>();
        if (sessionStore is null && uploadClient is null && pricingService is null)
        {
            return;
        }

        if (sessionStore is null || uploadClient is null || pricingService is null
            || authoritativePricingService is null || quoteTicketService is null)
        {
            initializationFailed = true;
            return;
        }

        var authenticationStateProvider = Services.GetService<AuthenticationStateProvider>();
        var principal = authenticationStateProvider is null
            ? null
            : (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        workflow = new InstantQuotationWorkflowCoordinator(
            sessionStore,
            uploadClient,
            pricingService,
            ResolveOwnerIdentity(principal),
            analytics,
            quoteTicketService,
            authoritativePricingService);
        materialDisplaySubscription = workflow.SubscribeMaterialPriceDisplay(MaterialDisplayChangedAsync);
        try
        {
            var identityAccessor = Services.GetService<IInstantQuotationWorkflowSessionIdentityAccessor>();
            var protectedSessionIdentity = identityAccessor is null
                ? null
                : await identityAccessor.GetProtectedSessionIdentityAsync(default);
            await workflow.InitializeAsync(protectedSessionIdentity, default);
            if (identityAccessor is not null)
            {
                await identityAccessor.SetProtectedSessionIdentityAsync(
                    workflow.ProtectedSessionIdentity,
                    default);
            }
        }
        catch
        {
            initializationFailed = true;
        }
    }

    public static string? ResolveOwnerIdentity(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated is not true)
        {
            return null;
        }

        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }

    private async Task UploadFilesAsync(InputFileChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        if (workflow is null)
        {
            return;
        }

        if (!await uploadBatchGate.WaitAsync(0))
        {
            await InvokePreviewAsync("discardSelection");
            return;
        }

        batchInProgress = true;
        var selectionGeneration = InvalidatePreviewSelectionIntent();
        Exception? primary = null;
        try
        {
            uploadSelectionError = null;
            // A new accepted selection replaces the one local exceptional preview,
            // not previously admitted parts. Rejected/normal replacements also release it.
            await RemoveIncompletePreviewAsync();
            if (disposed) return;
            await ClearDevelopmentPreviewAsync();
            var selectedFiles = args.GetMultipleFiles(100).ToArray();
            var browserFiles = selectedFiles
                .Where(file => file.Size <= InstantQuotationWorkflowCoordinator.MaximumFileSize)
                .ToArray();
            if (browserFiles.Length != selectedFiles.Length)
            {
                uploadSelectionError = Localizer["Files larger than {0} MB were skipped. Choose a smaller file.",
                    InstantQuotationWorkflowCoordinator.MaximumFileSize / (1024 * 1024)];
            }

            if (browserFiles.Length == 0)
            {
                await InvokePreviewAsync("discardSelection");
                return;
            }

            var keys = await BeginPreviewSelectionAsync();
            if (disposed) return;
            if (keys.Length != browserFiles.Length)
            {
                for (var index = 0; index < browserFiles.Length; index++)
                {
                    var localKey = $"local-selection-{selectionGeneration}-{index}";
                    failedPreviewSelections[localKey] = new(browserFiles[index].Name, "unavailable");
                    if (Parts.Count == 0 && selectionGeneration == previewSelectionGeneration) selectedFailedPreviewKey ??= localKey;
                }
                foreach (var key in keys)
                {
                    await InvokePreviewAsync("release", key);
                }

                await ReportPreviewUnavailableAsync();
                return;
            }

            for (var index = 0; index < browserFiles.Length; index++)
            {
                pendingPreviewSelections[keys[index]] = new(browserFiles[index].Name, selectionGeneration);
                pendingConfigurations[keys[index]] = new(browserFiles[index].Name, selectionGeneration);
            }
            if (Parts.Count == 0 && selectionGeneration == previewSelectionGeneration) selectedPendingPreviewKey = keys.Last();

            // beginSelection already starts the parsers; these consumers only observe their existing completion.
            var claims = keys.Select(ReadGeometryClaimAsync).ToArray();
            var uploads = new List<Task>();
            var reservations = new Guid?[browserFiles.Length];
            Exception? processingFailure = null;
            try
            {
                if (selectionGeneration == previewSelectionGeneration && selectedPendingPreviewKey == keys.Last())
                    await InvokePreviewAsync("selectLocalPreview", selectedPendingPreviewKey);
                if (disposed) return;
                await InvokeAsync(StateHasChanged);
                var remaining = Enumerable.Range(0, browserFiles.Length).ToList();
                while (remaining.Count > 0)
                {
                    var index = remaining.FirstOrDefault(candidate => claims[candidate].IsCompleted, -1);
                    if (index < 0)
                    {
                        await Task.WhenAny(remaining.Select(candidate => claims[candidate]));
                        continue;
                    }
                    remaining.Remove(index);
                    if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                    try
                    {
                        var claim = await claims[index];
                        if (disposed) return;
                        if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                        var incomplete = await previewInterop!.InvokeAsync<bool>("isIncompletePreview", keys[index]);
                        if (disposed) return;
                        if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                        if (incomplete)
                        {
                            await RemoveIncompletePreviewAsync();
                            if (disposed) return;
                            if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                            incompletePreviewKey = keys[index];
                            incompletePreviewMeasured = false;
                            incompletePreviewRevision = 0;
                            await InvokeAsync(StateHasChanged);
                            if (disposed) return;
                        }
                        if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                        pendingPreviewSelections.Remove(keys[index]);
                        var file = browserFiles[index];
                        var reserved = workflow.ReserveUploads([new InstantQuotationWorkflowUploadFile(
                            file.Name, file.ContentType, file.Size, claim,
                            token => Task.FromResult<Stream>(file.OpenReadStream(
                                InstantQuotationWorkflowCoordinator.MaximumFileSize, token)))],
                            reservations.Skip(index + 1).FirstOrDefault(candidate => candidate is { } id
                                && Uploads.Any(upload => upload.LocalId == id)));
                        var localId = reserved.Single();
                        reservations[index] = localId;
                        if (selectionGeneration == previewSelectionGeneration && selectedPendingPreviewKey is null)
                            autoPreviewLocalId = reservations.LastOrDefault(candidate => candidate is not null);
                        previewKeys[localId] = keys[index];
                        if (DevelopmentPreviewEnabled) developmentClaims[localId] = claim;
                        if (pendingConfigurations.TryGetValue(keys[index], out var intent)) intent.LocalId = localId;
                        uploads.Add(UploadReadySelectionAsync(keys[index], localId, selectionGeneration));
                        await InvokeAsync(StateHasChanged);
                    }
                    catch (JSException)
                    {
                        if (disposed) return;
                        if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                        var category = await ReadPreviewFailureCategoryAsync(keys[index]);
                        if (disposed) return;
                        if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                        pendingPreviewSelections.Remove(keys[index]);
                        pendingConfigurations.Remove(keys[index]);
                        if (selectedPendingPreviewKey == keys[index])
                        {
                            selectedPendingPreviewKey = null;
                            selectedFailedPreviewKey = keys[index];
                        }
                        failedPreviewSelections[keys[index]] = new(browserFiles[index].Name, category);
                        if (Parts.Count == 0 && selectionGeneration == previewSelectionGeneration) selectedFailedPreviewKey ??= keys[index];
                        await InvokePreviewAsync("release", keys[index]);
                        await ReportPreviewUnavailableAsync();
                    }
                }

                await Task.WhenAll(uploads);
                if (!disposed && selectionGeneration == previewSelectionGeneration && autoPreviewLocalId is not null)
                {
                    autoPreviewLocalId = reservations.Reverse().FirstOrDefault(candidate =>
                        candidate is { } id && Uploads.Any(upload => upload.LocalId == id
                            && upload.Status is InstantQuotationWorkflowUploadStatus.Uploaded));
                    await ReconcilePreviewsAsync();
                }
            }
            catch (Exception exception) { processingFailure = exception; throw; }
            finally
            {
                Exception? drainFailure = null;
                if (processingFailure is not null || disposed)
                {
                    foreach (var key in keys.Where(key => IsPendingPreview(key, selectionGeneration)).ToArray())
                    {
                        pendingPreviewSelections.Remove(key);
                        pendingConfigurations.Remove(key);
                        try { await InvokePreviewAsync("release", key); }
                        catch (Exception exception) { drainFailure ??= exception; }
                    }
                }
                try { await Task.WhenAll(claims); }
                catch (JSException) when (claims.All(task => task.IsCompleted)) { }
                catch (ObjectDisposedException) when (disposed && claims.All(task => task.IsCompleted)) { }
                catch (Exception exception) { drainFailure ??= exception; }
                try { await Task.WhenAll(uploads); }
                catch (Exception exception) { drainFailure ??= exception; }
                if (processingFailure is null && drainFailure is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(drainFailure).Throw();
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            Exception? cleanupFailure = null;
            try
            {
                foreach (var pending in pendingPreviewSelections.Where(pair =>
                    pair.Value.Generation == selectionGeneration).Select(pair => pair.Key).ToArray())
                {
                    pendingPreviewSelections.Remove(pending);
                    pendingConfigurations.Remove(pending);
                    if (selectedPendingPreviewKey == pending) selectedPendingPreviewKey = null;
                    try { await InvokePreviewAsync("release", pending); }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                }
            }
            finally
            {
                batchInProgress = false;
                if (disposed) uploadBatchGate.Dispose();
                else
                {
                    uploadBatchGate.Release();
                    try { await InvokeAsync(() => { if (!disposed) StateHasChanged(); }); }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                }
            }
            if (primary is null && cleanupFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }


    private async Task<InstantQuotationGeometryClaim> ReadGeometryClaimAsync(string key) =>
        await previewInterop!.InvokeAsync<InstantQuotationGeometryClaim>("getGeometryClaim", key);

    private string? BrowserPreviewKey(Guid localId) => previewKeys.GetValueOrDefault(localId);

    private async Task UploadReadySelectionAsync(string key, Guid localId, long generation)
    {
        Exception? primary = null;
        try
        {
            await workflow!.UploadReservedAsync([localId], default);
            if (disposed) return;
            var part = Parts.SingleOrDefault(item => item.PreviewCorrelationId == localId);
            if (part is not null && pendingConfigurations.TryGetValue(key, out var intent)
                && intent.Generation == generation && intent.LocalId == localId
                && previewKeys.GetValueOrDefault(localId) == key)
            {
                intent.PartId = part.PartId;
                await ApplyPendingConfigurationAsync(key, intent);
            }
            else
            {
                pendingConfigurations.Remove(key);
                if (selectedPendingPreviewKey == key)
                {
                    selectedPendingPreviewKey = null;
                    selectedPreviewUploadId = localId;
                }
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            try { if (!disposed) await ReconcilePreviewsAsync(); }
            catch (Exception exception) when (primary is not null)
            {
                primary.Data["PendingPreviewCleanupFailure"] = exception;
            }
        }
    }

    private async Task SelectPendingPreviewAsync(string key)
    {
        if (bulkApplying || disposed) return;
        if (!pendingConfigurations.ContainsKey(key))
        {
            var existingPart = Parts.SingleOrDefault(item => BrowserPreviewKey(item.PreviewCorrelationId) == key);
            if (existingPart is not null) await SelectPreviewAsync(existingPart.PartId);
            return;
        }
        InvalidatePreviewSelectionIntent();
        selectedPendingPreviewKey = key;
        selectedPreviewUploadId = null;
        selectedFailedPreviewKey = null;
        var generation = previewSelectionGeneration;
        var intent = pendingConfigurations[key];
        var part = intent.LocalId is { } localId
            ? Parts.SingleOrDefault(item => item.PreviewCorrelationId == localId) : null;
        var selected = part is not null && previewInterop is not null
            && await previewInterop.InvokeAsync<bool>("select", ViewerPartKey(part.PartId));
        if (disposed || selectedPendingPreviewKey != key || previewSelectionGeneration != generation) return;
        if (!selected) await InvokePreviewAsync("selectLocalPreview", key);
    }

    private async Task ChangePendingMaterialAsync(string key, string materialKey)
    {
        if (bulkApplying || disposed) return;
        if (!pendingConfigurations.TryGetValue(key, out var intent))
        {
            var part = Parts.SingleOrDefault(item => BrowserPreviewKey(item.PreviewCorrelationId) == key);
            if (part is not null) await ChangeMaterialAsync(part.PartId, new ChangeEventArgs { Value = materialKey });
            return;
        }
        var material = PricingCatalog.ResolveMaterial(materialKey)
            ?? throw new ArgumentException("The selected material is not supported.", nameof(materialKey));
        var colors = GetColors(material.Key);
        var color = colors.Contains(intent.Configuration.Color) ? intent.Configuration.Color : colors.First();
        intent.Configuration = intent.Configuration with
        {
            MaterialKey = material.Key,
            Color = color,
            BuildPreference = material.Process is PrintProcess.Resin ? BuildPreference.Standard : intent.Configuration.BuildPreference,
        };
        intent.Modified = true;
        intent.Revision++;
        await ApplyPendingConfigurationAsync(key, intent);
    }

    private async Task ChangePendingQuantityAsync(string key, ChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        if (!pendingConfigurations.TryGetValue(key, out var intent))
        {
            var part = Parts.SingleOrDefault(item => BrowserPreviewKey(item.PreviewCorrelationId) == key);
            if (part is not null) await ChangeQuantityAsync(part.PartId, args);
            return;
        }
        intent.Quantity = args.Value?.ToString() ?? string.Empty;
        intent.Modified = true;
        intent.Revision++;
        if (int.TryParse(intent.Quantity, out var quantity) && quantity is >= 1 and <= PricingCatalog.MaximumAdditiveQuantity)
            intent.Configuration = intent.Configuration with { Quantity = quantity };
        await ApplyPendingConfigurationAsync(key, intent);
    }


    private async Task ChangePendingColorAsync(string key, ChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        if (!pendingConfigurations.TryGetValue(key, out var intent))
        {
            var part = Parts.SingleOrDefault(item => BrowserPreviewKey(item.PreviewCorrelationId) == key);
            if (part is not null) await ChangeColorAsync(part.PartId, args);
            return;
        }
        var color = args.Value?.ToString() ?? string.Empty;
        if (!PricingCatalog.IsColorSupported(intent.Configuration.MaterialKey, color))
            throw new ArgumentException("The selected color is not supported.", nameof(args));
        intent.Configuration = intent.Configuration with { Color = color };
        intent.Modified = true;
        intent.Revision++;
        await ApplyPendingConfigurationAsync(key, intent);
    }

    private async Task ChangePendingBuildPreferenceAsync(string key, BuildPreference preference)
    {
        if (bulkApplying || disposed) return;
        if (!pendingConfigurations.TryGetValue(key, out var intent))
        {
            var part = Parts.SingleOrDefault(item => BrowserPreviewKey(item.PreviewCorrelationId) == key);
            if (part is not null) await ChangeBuildPreferenceAsync(part.PartId, preference);
            return;
        }
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        intent.Configuration = intent.Configuration with
        {
            BuildPreference = PricingCatalog.ResolveMaterial(intent.Configuration.MaterialKey)?.Process is PrintProcess.Resin
                ? BuildPreference.Standard : preference,
        };
        intent.Modified = true;
        intent.Revision++;
        await ApplyPendingConfigurationAsync(key, intent);
    }

    private async Task ApplyPendingConfigurationAsync(string key, PendingLocalConfiguration intent)
    {
        if (intent.Applying || intent.PartId is not { } partId || disposed) return;
        intent.Applying = true;
        try
        {
            while (!disposed && pendingConfigurations.GetValueOrDefault(key) == intent
                && intent.LocalId is { } localId && previewKeys.GetValueOrDefault(localId) == key
                && Parts.Any(part => part.PartId == partId && part.PreviewCorrelationId == localId))
            {
                if (!int.TryParse(intent.Quantity, out var quantity) || quantity is < 1 or > PricingCatalog.MaximumAdditiveQuantity) return;
                var revision = intent.Revision;
                var configuration = intent.Configuration with { Quantity = quantity };
                if (intent.Modified)
                {
                    var coordinator = workflow ?? throw new InvalidOperationException("The quotation workflow is unavailable.");
                    coordinator.ValidateConfiguration(partId, configuration.MaterialKey, configuration.Color,
                        quantity, configuration.BuildPreference);
                    await RepriceAsync(() => coordinator.UpdateConfigurationAsync(partId, configuration.MaterialKey,
                        configuration.Color, quantity, configuration.BuildPreference, default), owner: partId);
                }
                if (disposed || pendingConfigurations.GetValueOrDefault(key) != intent) return;
                if (revision != intent.Revision) continue;
                pendingConfigurations.Remove(key);
                if (selectedPendingPreviewKey == key)
                {
                    selectedPendingPreviewKey = null;
                    await SelectPreviewAsync(partId);
                }
                return;
            }
        }
        finally { intent.Applying = false; }
    }

    private bool IsPendingPreview(string key, long generation) =>
        pendingPreviewSelections.TryGetValue(key, out var pending) && pending.Generation == generation;

    private async Task RemovePendingPreviewAsync(string key)
    {
        if (disposed || bulkApplying || !pendingPreviewSelections.Remove(key)) return;
        pendingConfigurations.Remove(key);
        var selected = selectedPendingPreviewKey == key;
        var generation = previewSelectionGeneration;
        if (selected) selectedPendingPreviewKey = null;
        // Release only this local selection; late parser completion cannot admit a removed key.
        if (incompletePreviewKey == key) await RemoveIncompletePreviewAsync();
        else await InvokePreviewAsync("release", key);
        if (!disposed && selected && previewSelectionGeneration == generation && !HasLocalPreviewSelection
            && selectedPreviewPartId is { } current && Parts.Any(part => part.PartId == current))
            await InvokePreviewAsync("select", ViewerPartKey(current));
        if (!disposed) await InvokeAsync(StateHasChanged);
    }

    private long InvalidatePreviewSelectionIntent()
    {
        autoPreviewLocalId = null;
        revealPreviewPartId = null;
        return checked(++previewSelectionGeneration);
    }

    private async Task RemoveFailedPreviewAsync(string key)
    {
        if (disposed || InputDisabled || IsRepricing || !failedPreviewSelections.Remove(key)) return;
        InvalidatePreviewSelectionIntent();
        localPreviewThumbnails.Remove(key);
        if (selectedFailedPreviewKey == key) selectedFailedPreviewKey = null;
        await InvokePreviewAsync("release", key);
        if (!disposed && !HasLocalPreviewSelection && selectedPreviewPartId is { } selectedId
            && Parts.Any(part => part.PartId == selectedId))
            await InvokePreviewAsync("select", ViewerPartKey(selectedId));
        if (!disposed) await InvokeAsync(StateHasChanged);
    }

    private async Task SelectFailedPreviewAsync(string key)
    {
        if (bulkApplying || disposed || !failedPreviewSelections.ContainsKey(key)) return;
        InvalidatePreviewSelectionIntent();
        selectedPendingPreviewKey = null;
        selectedFailedPreviewKey = key;
        selectedPreviewUploadId = null;
        await InvokePreviewAsync("selectLocalPreview", key);
    }

    private async Task SelectUploadPreviewAsync(Guid localId)
    {
        if (bulkApplying || disposed || Parts.Any(part => part.PreviewCorrelationId == localId)
            || !Uploads.Any(upload => upload.LocalId == localId
                && upload.Status is InstantQuotationWorkflowUploadStatus.Error or InstantQuotationWorkflowUploadStatus.Cancelled)) return;
        InvalidatePreviewSelectionIntent();
        selectedPendingPreviewKey = null;
        selectedPreviewUploadId = localId;
        selectedFailedPreviewKey = null;
        await InvokePreviewAsync("selectLocalPreview", previewKeys.GetValueOrDefault(localId));
    }

    private async Task RemoveFailedUploadAsync(Guid localId)
    {
        if (disposed || InputDisabled || IsRepricing || workflow?.DismissFailedUpload(localId) != true) return;
        developmentPreviews.Remove(localId);
        developmentClaims.Remove(localId);
        developmentRetries.Remove(localId);
        RepriceDevelopmentPreview();
        InvalidatePreviewSelectionIntent();
        if (selectedPreviewUploadId == localId) selectedPreviewUploadId = null;
        if (previewKeys.TryGetValue(localId, out var key)) localPreviewThumbnails.Remove(key);
        await ReleasePreviewAsync(localId);
        if (!disposed && !HasLocalPreviewSelection && selectedPreviewPartId is { } selectedId
            && Parts.Any(part => part.PartId == selectedId))
            await InvokePreviewAsync("select", ViewerPartKey(selectedId));
        if (!disposed) await InvokeAsync(StateHasChanged);
    }

    private async Task CancelUpload(Guid localId)
    {
        if (bulkApplying || disposed) return;
        workflow?.Cancel(localId);
        developmentPreviews.Remove(localId);
        developmentClaims.Remove(localId);
        RepriceDevelopmentPreview();
        await QuarantinePreviewAsync(localId);
    }

    private async Task RetryUploadAsync(Guid localId)
    {
        if (bulkApplying || disposed || InputDisabled || IsRepricing) return;
        if (workflow is null) return;
        var development = DevelopmentPreviewEnabled;
        var selection = developmentSelectionGeneration;
        if (development && !developmentRetries.Add(localId)) return;
        developmentPreviews.Remove(localId, out var previousDevelopment);
        developmentTotal = null;
        developmentLeadTime = null;
        developmentStep = DevelopmentPreviewStep.Configuration;
        RepriceDevelopmentPreview();
        try
        {
            var hasAuthoritativePart = Parts.Any(item => item.PreviewCorrelationId == localId);
            if (!hasAuthoritativePart
                && previewKeys.TryGetValue(localId, out var previousKey)
                && previewInterop is not null)
            {
                try
                {
                    var retryKey = await previewInterop.InvokeAsync<string>("retry", previousKey);
                    if (disposed || previewKeys.GetValueOrDefault(localId) != previousKey
                        || !Uploads.Any(upload => upload.LocalId == localId)
                        || Parts.Any(part => part.PreviewCorrelationId == localId))
                    {
                        await InvokePreviewAsync("release", retryKey);
                        return;
                    }
                    previewKeys[localId] = retryKey;
                    if (localPreviewThumbnails.Remove(previousKey, out var thumbnail))
                        localPreviewThumbnails[retryKey] = thumbnail;
                    if (development)
                    {
                        var claim = await previewInterop.InvokeAsync<InstantQuotationGeometryClaim>("getGeometryClaim", retryKey);
                        if (!disposed && selection == developmentSelectionGeneration)
                            developmentClaims[localId] = claim;
                    }
                }
                catch (JSException)
                {
                    developmentClaims.Remove(localId);
                    await ReportPreviewUnavailableAsync();
                }
            }

            await workflow.RetryAsync(localId, default);
            developmentRetries.Remove(localId);
            await ReconcilePreviewsAsync(selection == developmentSelectionGeneration ? previousDevelopment : null);
        }
        finally
        {
            developmentRetries.Remove(localId);
        }
    }

    private async Task DiscardUploadAsync(Guid localId)
    {
        if (workflow is null) return;
        try
        {
            await workflow.DiscardUploadAsync(localId, default);
        }
        catch (InstantQuotationWorkflowCoordinator.DiscardRefusedException)
        {
            return;
        }
        developmentPreviews.Remove(localId);
        developmentClaims.Remove(localId);
        developmentRetries.Remove(localId);
        RepriceDevelopmentPreview();
        await ReleasePreviewAsync(localId);
    }

    private async Task RemovePartAsync(Guid partId)
    {
        if (bulkApplying || disposed) return;
        InvalidatePreviewSelectionIntent();
        InvalidatePhysicalAnalysis(partId);
        if (workflow is not null)
        {
            var part = Parts.SingleOrDefault(item => item.PartId == partId);
            await workflow.RemoveAsync(partId, () => repricing.Forget(partId), default);
            if (part is not null && Parts.All(item => item.PartId != partId))
            {
                if (BrowserPreviewKey(part.PreviewCorrelationId) is { } key)
                {
                    pendingConfigurations.Remove(key);
                    if (selectedPendingPreviewKey == key) selectedPendingPreviewKey = null;
                }
                thicknessStatuses.Remove(partId);
                thicknessToggleRevisions.Remove(partId);
                quantityEdits.Forget(partId);
                selectedBulkParts.Remove(partId);
                repricing.Forget(partId);
                quantityDrafts.Remove(partId);
                displayIntents.Remove(partId);
                await ReleasePreviewAsync(part.PreviewCorrelationId);
                if (selectedPreviewPartId == partId)
                {
                    selectedPreviewPartId = Parts.FirstOrDefault()?.PartId;
                    if (!HasLocalPreviewSelection && selectedPreviewPartId is { } nextPartId)
                    {
                        await InvokePreviewAsync("select", ViewerPartKey(nextPartId));
                    }
                }
            }
        }
    }

    [JSInvokable]
    public ValueTask ReportQuoteStartedAsync() =>
        disposed ? ValueTask.CompletedTask : analytics.RecordQuoteStartedAsync(analyticsWorkflowId);

    private async Task<string[]> BeginPreviewSelectionAsync()
    {
        await EnsurePreviewInteropAsync();
        if (disposed || previewInterop is null || fileInput is null)
        {
            return [];
        }

        try
        {
            return await previewInterop.InvokeAsync<string[]>("beginSelection", fileInput.Element,
                InstantQuotationWorkflowCoordinator.MaximumFileSize);
        }
        catch (JSException)
        {
            await ReportPreviewUnavailableAsync();
            return [];
        }
    }

    private async Task EnsurePreviewInteropAsync()
    {
        if (disposed || previewInterop is not null || previewUnavailable)
        {
            return;
        }

        var initialization = previewInitialization ??= InitializePreviewInteropAsync();
        try
        {
            await initialization;
        }
        catch
        {
            if (ReferenceEquals(previewInitialization, initialization)) previewInitialization = null;
            throw;
        }
    }

    private async Task InitializePreviewInteropAsync()
    {
        IJSObjectReference? module = null;
        IJSObjectReference? interop = null;
        DotNetObjectReference<InstantQuotationWorkflow>? reporter = null;
        bool published = false;
        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "/dist/instant-quotation-workflow.mjs");
            if (disposed) return;
            reporter = DotNetObjectReference.Create(this);
            interop = await module.InvokeAsync<IJSObjectReference>(
                "createInstantQuotationWorkflowInterop",
                reporter);
            if (disposed) return;
            previewModule = module;
            previewStatusReporter = reporter;
            previewInterop = interop;
            published = true;
        }
        catch (JSException)
        {
            await ReportPreviewUnavailableAsync();
        }
        finally
        {
            if (!published)
            {
                try { await DisposePreviewInteropAsync(interop); }
                finally
                {
                    try { await DisposePreviewModuleAsync(module); }
                    finally { reporter?.Dispose(); }
                }
            }
        }
    }

    private async Task EnsurePreviewInputBoundAsync()
    {
        if (previewInterop is null || fileInput is null)
        {
            return;
        }

        try
        {
            await previewInterop.InvokeVoidAsync("bindInput", fileInput.Element);
        }
        catch (JSException)
        {
            await ReportPreviewUnavailableAsync();
        }
    }

    private async Task ReconcilePreviewsAsync(DevelopmentPreviewPart? retryPresentation = null)
    {
        var interop = previewInterop;
        if (disposed || interop is null)
        {
            return;
        }

        foreach (var (localId, key) in previewKeys.ToArray())
        {
            var upload = Uploads.SingleOrDefault(item => item.LocalId == localId);
            var part = Parts.SingleOrDefault(item => item.PreviewCorrelationId == localId);
            if (upload?.Status is InstantQuotationWorkflowUploadStatus.Uploaded && part is not null)
            {
                bool IsCurrentAdmission() => !disposed
                    && previewKeys.TryGetValue(localId, out var currentKey) && currentKey == key
                    && Parts.Any(item => item.PartId == part.PartId && item.PreviewCorrelationId == localId)
                    && Uploads.Any(item => item.LocalId == localId
                        && item.Status is InstantQuotationWorkflowUploadStatus.Uploaded);
                try
                {
                    if (!IsCurrentAdmission()) continue;
                    await interop.InvokeVoidAsync(
                        "admit", key, ViewerPartKey(part.PartId), part.Configuration.MaterialKey);
                    developmentPreviews.Remove(localId);
                    developmentClaims.Remove(localId);
                    if (!IsCurrentAdmission()) continue;
                    if (autoPreviewLocalId == localId)
                    {
                        autoPreviewLocalId = null;
                        selectedPreviewUploadId = null;
                        selectedFailedPreviewKey = null;
                        selectedPreviewPartId = part.PartId;
                        revealPreviewPartId = part.PartId;
                        await InvokePreviewAsync("select", ViewerPartKey(part.PartId));
                        if (!disposed) await InvokeAsync(StateHasChanged);
                    }
                    else
                    {
                        selectedPreviewPartId ??= part.PartId;
                    }
                    if (!IsCurrentAdmission()) continue;
                    await analytics.RecordStageResultAsync(
                        InstantQuotationStage.Thumbnail,
                        InstantQuotationStageResult.Success,
                        null,
                        FileType(part.DisplayFileName));
                }
                catch (JSException)
                {
                    if (!IsCurrentAdmission()) continue;
                    await analytics.RecordStageResultAsync(
                        InstantQuotationStage.Thumbnail,
                        InstantQuotationStageResult.Failure,
                        InstantQuotationStageFailure.SnapshotUnavailable,
                        FileType(part.DisplayFileName));
                    await ReportPreviewUnavailableAsync();
                }
            }
            else if (upload?.Status is InstantQuotationWorkflowUploadStatus.Error or InstantQuotationWorkflowUploadStatus.Cancelled)
            {
                if (upload.Status is InstantQuotationWorkflowUploadStatus.Error
                    && await KeepDevelopmentPreviewAsync(upload, key, retryPresentation))
                    continue;
                if (Parts.Count == 0 && selectedFailedPreviewKey is null && selectedPreviewUploadId is null)
                {
                    selectedPreviewUploadId = localId;
                    await InvokePreviewAsync("selectLocalPreview", key);
                    if (disposed) return;
                }
                if (upload.Status is InstantQuotationWorkflowUploadStatus.Error && key != incompletePreviewKey
                    && previewInterop is not null && !localPreviewThumbnails.ContainsKey(key))
                {
                    try
                    {
                        var source = await interop.InvokeAsync<string?>("captureFailedThumbnail", key, previewCanvas);
                        if (!disposed && previewKeys.GetValueOrDefault(localId) == key
                            && Uploads.Any(item => item.LocalId == localId && item.Status is InstantQuotationWorkflowUploadStatus.Error)
                            && source is { Length: <= 262144 } && source.StartsWith("data:image/webp;base64,", StringComparison.Ordinal))
                            localPreviewThumbnails[key] = source;
                    }
                    catch (JSException) { /* Preserve the controlled upload failure if rendering is unavailable. */ }
                    if (disposed || previewKeys.GetValueOrDefault(localId) != key) continue;
                }
                if (key != incompletePreviewKey || upload.Status is InstantQuotationWorkflowUploadStatus.Cancelled)
                {
                    await QuarantinePreviewAsync(localId);
                }
            }
            else if (upload is null)
            {
                await ReleasePreviewAsync(localId);
            }
        }
    }

    private async Task QuarantinePreviewAsync(Guid localId)
    {
        if (autoPreviewLocalId == localId) InvalidatePreviewSelectionIntent();
        if (previewKeys.TryGetValue(localId, out var key))
        {
            if (key == incompletePreviewKey)
            {
                await RemoveIncompletePreviewAsync();
            }
            await InvokePreviewAsync("quarantine", key);
        }
    }

    private async Task ReleasePreviewAsync(Guid localId)
    {
        if (autoPreviewLocalId == localId) InvalidatePreviewSelectionIntent();
        if (previewKeys.Remove(localId, out var key))
        {
            if (key == incompletePreviewKey)
            {
                await RemoveIncompletePreviewAsync();
            }
            await InvokePreviewAsync("release", key);
        }
    }

    private async Task SelectPreviewAsync(Guid partId)
    {
        if (bulkApplying || disposed || Parts.All(part => part.PartId != partId)) return;
        var part = Parts.Single(item => item.PartId == partId);
        if (BrowserPreviewKey(part.PreviewCorrelationId) is { } pendingKey && pendingConfigurations.ContainsKey(pendingKey))
        {
            await SelectPendingPreviewAsync(pendingKey);
            return;
        }
        InvalidatePreviewSelectionIntent();
        selectedPendingPreviewKey = null;
        selectedPreviewUploadId = null;
        selectedFailedPreviewKey = null;
        selectedPreviewPartId = partId;
        revealPreviewPartId = partId;
        await InvokePreviewAsync("select", ViewerPartKey(partId));
    }

    private bool IsPreviewSelected(Guid partId) => !HasLocalPreviewSelection && selectedPreviewPartId == partId;

    private Task ResetPreviewAsync() => InvokePreviewAsync("reset");

    private Task FitPreviewAsync() => InvokePreviewAsync("fit");

    private Task FullscreenPreviewAsync() => InvokePreviewAsync("fullscreen");

    private async Task ToggleWallThicknessAsync()
    {
        if (selectedPreviewPartId is not { } id || previewInterop is null
            || SelectedThickness?.Available != true)
        {
            return;
        }

        var revision = thicknessToggleRevisions.TryGetValue(id, out var previous) ? previous + 1 : 1;
        thicknessToggleRevisions[id] = revision;
        try
        {
            var visible = await previewInterop.InvokeAsync<bool>("toggleThickness", ViewerPartKey(id));
            if (thicknessToggleRevisions.TryGetValue(id, out var current) && current == revision)
            {
                ApplyThicknessVisibility(id, visible);
            }
        }
        catch (JSException)
        {
            if (thicknessToggleRevisions.TryGetValue(id, out var current) && current == revision)
            {
                ApplyThicknessVisibility(id, false);
            }
        }
    }

    private void ApplyThicknessVisibility(Guid id, bool visible)
    {
        if (Parts.Any(part => part.PartId == id)
            && thicknessStatuses.TryGetValue(id, out var status))
        {
            thicknessStatuses[id] = status with { Visible = visible };
        }
    }

    [JSInvokable]
    public Task ReportThicknessStateAsync(
        string partId,
        bool available,
        bool warning,
        bool incomplete,
        string process,
        double limitMm,
        double? minMm,
        long revision)
    {
        if (!Guid.TryParseExact(partId, "N", out var id)
            || Parts.All(part => part.PartId != id))
        {
            return Task.CompletedTask;
        }

        return InvokeAsync(() =>
        {
            if (!ShouldAcceptThicknessReport(
                Parts.Any(part => part.PartId == id),
                thicknessStatuses.TryGetValue(id, out var current) ? current.Revision : null,
                revision))
            {
                return;
            }

            var visible = thicknessStatuses.TryGetValue(id, out var previous) && previous.Visible;
            thicknessStatuses[id] = new ThicknessStatus(
                available, warning, incomplete, process, limitMm, minMm, available && visible, revision);
            StateHasChanged();
        });
    }

    internal static bool ShouldAcceptThicknessReport(bool partExists, long? currentRevision, long revision)
        => partExists && revision > 0 && (!currentRevision.HasValue || revision > currentRevision.Value);

    [JSInvokable]
    public Task ReportIncompletePreviewAsync(string key, long revision, bool incomplete)
        => InvokeAsync(() =>
        {
            if (disposed || key != incompletePreviewKey || revision <= incompletePreviewRevision || !incomplete)
            {
                return;
            }
            incompletePreviewRevision = revision;
            incompletePreviewMeasured = true;
            StateHasChanged();
        });

    private Task RemoveIncompletePreviewSelectionAsync()
    {
        var localId = previewKeys.FirstOrDefault(pair => pair.Value == incompletePreviewKey).Key;
        return localId != Guid.Empty ? RemoveFailedUploadAsync(localId) : RemoveIncompletePreviewAsync();
    }

    private async Task RemoveIncompletePreviewAsync()
    {
        var key = incompletePreviewKey;
        incompletePreviewKey = null;
        incompletePreviewAttached = false;
        incompletePreviewMeasured = false;
        incompletePreviewRevision = 0;
        if (key is not null)
        {
            await InvokePreviewAsync("removeIncompletePreview", key);
            await InvokePreviewAsync("release", key);
        }
    }

    private async Task InvokePreviewAsync(string identifier, params object?[] arguments)
    {
        if (previewInterop is null)
        {
            return;
        }

        try
        {
            await previewInterop.InvokeVoidAsync(identifier, arguments);
        }
        catch (JSException)
        {
            await ReportPreviewUnavailableAsync();
        }
    }

    [JSInvokable]
    public Task ReportPreviewUnavailableAsync()
    {
        if (disposed) return Task.CompletedTask;
        previewUnavailable = true;
        return InvokeAsync(StateHasChanged);
    }

    private static string? FileType(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.');
        return extension.Length == 0 ? null : extension;
    }

    private async Task ChangeMaterialAsync(Guid partId, ChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        InvalidatePhysicalAnalysis(partId);
        var part = Parts.Single(item => item.PartId == partId);
        var material = args.Value?.ToString() ?? part.Configuration.MaterialKey;
        var colors = GetColors(material);
        var color = part.Geometry.BodyCount > 1 ? "Any"
            : colors.Contains(part.Configuration.Color, StringComparer.Ordinal)
                ? part.Configuration.Color : colors.First();
        await UpdateConfigurationAsync(part, material, color, part.Configuration.Quantity, async current =>
        {
            if (!current()) return;
            var accepted = Parts.SingleOrDefault(item => item.PartId == partId);
            if (accepted is null) return;
            await InvokePreviewAsync("setThicknessMaterial", ViewerPartKey(partId), accepted.Configuration.MaterialKey);
            if (current()) await UpdatePartAppearanceAsync(partId);
        });
    }

    private async Task ChangeColorAsync(Guid partId, ChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        InvalidatePhysicalAnalysis(partId);
        var part = Parts.Single(item => item.PartId == partId);
        var color = args.Value?.ToString() ?? part.Configuration.Color;
        await UpdateConfigurationAsync(
            part,
            part.Configuration.MaterialKey,
            color,
            part.Configuration.Quantity,
            current => current() ? UpdatePartAppearanceAsync(partId) : Task.CompletedTask);
    }

    private void OnQuantityInput(Guid partId, ChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        var part = Parts.SingleOrDefault(part => part.PartId == partId);
        if (part is null) return;
        var raw = args.Value?.ToString() ?? "";
        quantityDrafts[partId] = (checked(++quantityDraftRevision), raw);
        if (!IsRepricing && workflow?.State is InstantQuotationWorkflowState.Configured
            && part.Quote is { } accepted
            && accepted.MaterialKey == part.Configuration.MaterialKey && accepted.Color == part.Configuration.Color
            && accepted.Quantity == part.Configuration.Quantity && accepted.BuildPreference == part.Configuration.BuildPreference
            && int.TryParse(raw, out var quantity) && quantity == part.Configuration.Quantity)
        {
            // Native change need not fire when an edit returns to the original input value.
            quantityDrafts.Remove(partId);
            quantityEdits.RestoreAccepted(partId);
            displayIntents.Remove(partId);
            return;
        }
        minimumDisplayGeneration = workflow?.MaterialPriceDisplay.Generation ?? 0;
        displayIntents.Remove(partId);
        quantityEdits.Begin(partId);
    }

    private async Task ChangeQuantityAsync(Guid partId, ChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        InvalidatePhysicalAnalysis(partId);
        var part = Parts.Single(item => item.PartId == partId);
        var quantity = int.TryParse(args.Value?.ToString(), out var value) ? value : part.Configuration.Quantity;
        var revision = quantityDrafts.GetValueOrDefault(partId).Revision;
        await quantityEdits.RepriceAsync(partId,
            () => UpdateConfigurationAsync(part, part.Configuration.MaterialKey, part.Configuration.Color, quantity));
        if (quantityDrafts.TryGetValue(partId, out var current) && current.Revision == revision)
            quantityDrafts.Remove(partId);
    }

    private Task ChangeBuildPreferenceAsync(Guid partId, BuildPreference buildPreference)
    {
        if (bulkApplying || disposed) return Task.CompletedTask;
        InvalidatePhysicalAnalysis(partId);
        var part = Parts.Single(item => item.PartId == partId);
        workflow?.ValidateConfiguration(partId, part.Configuration.MaterialKey, part.Configuration.Color,
            part.Configuration.Quantity, buildPreference);
        displayIntents[partId] = part.Configuration with { BuildPreference = buildPreference };
        return RepriceAsync(() => workflow?.UpdateConfigurationAsync(
            part.PartId,
            part.Configuration.MaterialKey,
            part.Configuration.Color,
            part.Configuration.Quantity,
            buildPreference,
            default) ?? Task.CompletedTask, owner: partId);
    }

    private int PartNumber(Guid partId)
    {
        for (var index = 0; index < Parts.Count; index++)
        {
            if (Parts[index].PartId == partId)
            {
                return index + 1;
            }
        }

        return 0;
    }

    private string MaterialDisplayName(string materialKey) => Materials
        .SingleOrDefault(item => string.Equals(item.Key, materialKey, StringComparison.Ordinal))?.DisplayName
        ?? materialKey;

    private string FormatPrintDuration(double printTimeMinutes)
    {
        var minutes = Math.Max(1, (long)Math.Round(printTimeMinutes, MidpointRounding.AwayFromZero));
        var days = minutes / 1440;
        var hours = minutes % 1440 / 60;
        var remainder = minutes % 60;
        var duration = new List<string>(3);
        if (days > 0)
        {
            duration.Add($"{days:N0} {Localizer[days == 1 ? "day" : "days"]}");
        }

        if (hours > 0)
        {
            duration.Add($"{hours:N0} {Localizer[hours == 1 ? "hour" : "hours"]}");
        }

        if (remainder > 0 || duration.Count == 0)
        {
            duration.Add($"{remainder:N0} {Localizer[remainder == 1 ? "minute" : "minutes"]}");
        }

        return string.Join(" ", duration);
    }

    private Task UpdateConfigurationAsync(
        InstantQuotationWorkflowPartViewModel part,
        string material,
        string color,
        int quantity,
        Func<Func<bool>, Task>? afterUpdate = null)
    {
        var selected = workflow?.ValidateConfiguration(part.PartId, material, color, quantity);
        var key = selected?.Key ?? material;
        displayIntents[part.PartId] = part.Configuration with { MaterialKey = material, Color = color, Quantity = quantity };
        bool HasCurrentBinding() => !disposed && Parts.Any(current => current.PartId == part.PartId
            && current.Configuration.MaterialKey == key && current.Configuration.Color == color
            && current.Configuration.Quantity == quantity);
        return RepriceAsync(() => workflow?.UpdateConfigurationAsync(part.PartId, material, color, quantity, default)
            ?? Task.CompletedTask, afterUpdate is null ? null
                : current => afterUpdate(() => current() && HasCurrentBinding()), part.PartId);
    }

    private async Task RepriceAsync(Func<Task> update, Func<Func<bool>, Task>? afterUpdate = null, Guid? owner = null)
    {
        Task NotifyAsync() => InvokeAsync(() =>
        {
            if (!disposed) StateHasChanged();
        });

        minimumDisplayGeneration = workflow?.MaterialPriceDisplay.Generation ?? 0;
        try
        {
            if (owner is { } partId) await repricing.RunAsync(partId, update, NotifyAsync, afterUpdate);
            else await repricing.RunAsync(update, NotifyAsync, afterUpdate is null ? null : () => afterUpdate(() => !disposed));
        }
        finally
        {
            if (!repricing.IsActive) displayIntents.Clear();
            if (!disposed) await NotifyAsync();
        }
    }

    private void InvalidatePhysicalAnalysis(Guid partId)
    {
        physicalAnalysisGeneration++;
        physicalAnalyses.Remove(partId);
        if (physicalAnalysisPartId == partId)
        {
            physicalAnalysisCancellation?.Cancel();
        }
    }

    private async Task AnalyzePhysicalAsync(Guid partId)
    {
        if (workflow is null
            || Services.GetService<InstantQuotationBoundPhysicalAnalysisService>() is not { } service
            || Services.GetService<FdmRuntimeProfileCatalog>() is not { } catalog)
        {
            return;
        }

        InstantQuotationPhysicalAnalysisBinding? binding = workflow.CurrentPhysicalAnalysisBinding(
            partId, catalog.ProfileVersion);
        if (binding is null)
        {
            physicalAnalyses[partId] = InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.UploadUnavailable);
            return;
        }

        physicalAnalysisCancellation?.Cancel();
        var generation = ++physicalAnalysisGeneration;
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        physicalAnalysisCancellation = cancellation;
        physicalAnalysisPartId = partId;
        physicalAnalyses.Remove(partId);
        try
        {
            InstantQuotationBoundPhysicalAnalysisResult result = await service.AnalyzeAsync(
                binding, cancellation.Token);
            if (!cancellation.IsCancellationRequested
                && physicalAnalysisGeneration == generation
                && Equals(workflow.CurrentPhysicalAnalysisBinding(partId, catalog.ProfileVersion), binding))
            {
                physicalAnalyses[partId] = result;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(physicalAnalysisCancellation, cancellation)
                && physicalAnalysisGeneration == generation
                && Equals(workflow.CurrentPhysicalAnalysisBinding(partId, catalog.ProfileVersion), binding))
            {
                physicalAnalyses[partId] = InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                    InstantQuotationBoundPhysicalAnalysisFailure.AnalysisUnavailable);
            }
        }
        finally
        {
            if (ReferenceEquals(physicalAnalysisCancellation, cancellation))
            {
                physicalAnalysisCancellation = null;
                physicalAnalysisPartId = null;
            }
        }
    }

    private async Task UpdatePartAppearanceAsync(Guid partId)
    {
        var accepted = Parts.SingleOrDefault(part => part.PartId == partId);
        if (disposed || accepted is null || accepted.Geometry.BodyCount > 1) return;
        await InvokePreviewAsync("updatePartAppearance", ViewerPartKey(partId), accepted.Configuration.Color);
        if (disposed || HasLocalPreviewSelection) return;
        var selected = Parts.FirstOrDefault(part => part.PartId == selectedPreviewPartId) ?? Parts.FirstOrDefault();
        selectedPreviewPartId = selected?.PartId;
        if (selected is not null)
        {
            await InvokePreviewAsync("select", ViewerPartKey(selected.PartId));
        }
    }

    private Task SelectReviewPartAsync(Guid partId) => SelectPreviewAsync(partId);

    private IReadOnlyList<string> GetReviewThicknessWarnings(Guid partId)
    {
        if (!thicknessStatuses.TryGetValue(partId, out var status))
        {
            return [];
        }

        var warnings = new List<string>();
        if (status.Warning)
        {
            warnings.Add(Localizer["Thin walls may print with defects"] + " — "
                + Localizer["Some walls are below {0:N2} mm for {1} and may print with holes, weak walls, or missing details.",
                    status.LimitMm, status.Process.ToUpperInvariant()]);
        }

        if (status.Incomplete)
        {
            warnings.Add(Localizer["Thickness analysis is incomplete"] + " — "
                + Localizer["Some regions could not be measured reliably."]);
        }

        return warnings;
    }

    private async Task ChangeReviewMaterialAsync((Guid PartId, string Value) change)
    {
        if (bulkApplying || disposed) return;
        var update = ChangeMaterialAsync(change.PartId, new ChangeEventArgs { Value = change.Value });
        var current = repricing.CaptureOwnership(change.PartId);
        var navigation = reviewNavigationRevision;
        await update;
        if (current() && navigation == reviewNavigationRevision && CanEnterReview) workflow?.EnterReview();
    }

    private async Task ChangeReviewColorAsync((Guid PartId, string Value) change)
    {
        if (bulkApplying || disposed) return;
        var update = ChangeColorAsync(change.PartId, new ChangeEventArgs { Value = change.Value });
        var current = repricing.CaptureOwnership(change.PartId);
        var navigation = reviewNavigationRevision;
        await update;
        if (current() && navigation == reviewNavigationRevision && CanEnterReview) workflow?.EnterReview();
    }

    private async Task ChangeReviewQuantityAsync((Guid PartId, string Value) change)
    {
        if (bulkApplying || disposed) return;
        if (!int.TryParse(change.Value, out var quantity)
            || quantity < 1
            || quantity > PricingCatalog.MaximumAdditiveQuantity)
        {
            return;
        }

        var update = ChangeQuantityAsync(change.PartId, new ChangeEventArgs { Value = quantity });
        var current = repricing.CaptureOwnership(change.PartId);
        var navigation = reviewNavigationRevision;
        await update;
        if (current() && navigation == reviewNavigationRevision && CanEnterReview) workflow?.EnterReview();
    }

    private async Task ChangeReviewBuildPreferenceAsync((Guid PartId, string Value) change)
    {
        if (bulkApplying || disposed) return;
        if (!Enum.TryParse<BuildPreference>(change.Value, true, out var preference)
            || !Enum.IsDefined(preference))
        {
            return;
        }

        var update = ChangeBuildPreferenceAsync(change.PartId, preference);
        var current = repricing.CaptureOwnership(change.PartId);
        var navigation = reviewNavigationRevision;
        await update;
        if (current() && navigation == reviewNavigationRevision && CanEnterReview) workflow?.EnterReview();
    }

    private async Task EditReviewPartAsync(Guid partId)
    {
        checked { reviewNavigationRevision++; }
        await SelectPreviewAsync(partId);
        workflow?.ReturnToConfiguration();
        pendingFocus = PendingWorkflowFocus.Configuration;
    }

    private void EnterReview()
    {
        if (!CanEnterReview) return;
        workflow?.EnterReview();
        pendingFocus = PendingWorkflowFocus.Review;
    }

    private void EnterCustomerDetails()
    {
        if (disposed || InputDisabled || IsRepricing || failedPreviewSelections.Count > 0) return;
        checked { reviewNavigationRevision++; }
        workflow?.EnterCustomerDetails();
        pendingFocus = PendingWorkflowFocus.CustomerDetails;
    }

    private void ReturnToConfiguration()
    {
        checked { reviewNavigationRevision++; }
        workflow?.ReturnToConfiguration();
        pendingFocus = PendingWorkflowFocus.Configuration;
    }

    private void ReturnToReview()
    {
        workflow?.ReturnToReview();
        pendingFocus = PendingWorkflowFocus.Review;
    }

    private async Task FocusPendingSectionAsync()
    {
        var target = pendingFocus switch
        {
            PendingWorkflowFocus.Configuration => configurationSection,
            PendingWorkflowFocus.Review => reviewSection,
            PendingWorkflowFocus.CustomerDetails => customerDetailsSection,
            _ => default,
        };
        if (pendingFocus is PendingWorkflowFocus.None)
        {
            return;
        }

        pendingFocus = PendingWorkflowFocus.None;
        try
        {
            await target.FocusAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }

    private IReadOnlyList<string> GetColors(string material) => workflow?.GetColors(material) ?? [];

    private IReadOnlyList<string> GetPartColors(InstantQuotationWorkflowPartViewModel part)
    {
        if (part.Geometry.BodyCount > 1) return ["Any"];
        var colors = GetColors(part.Configuration.MaterialKey);
        return PricingCatalog.IsColorSupported(part.Configuration.MaterialKey, part.Configuration.Color)
            && !colors.Contains(part.Configuration.Color, StringComparer.Ordinal)
                ? [.. colors, part.Configuration.Color]
                : colors;
    }

    private string UploadStatus(InstantQuotationWorkflowUploadStatus status) => status switch
    {
        InstantQuotationWorkflowUploadStatus.Pending => Localizer["Waiting"],
        InstantQuotationWorkflowUploadStatus.Uploading => Localizer["Uploading files…"],
        InstantQuotationWorkflowUploadStatus.Uploaded => Localizer["File uploaded. Configure the part to continue."],
        InstantQuotationWorkflowUploadStatus.Error => Localizer["The file could not be uploaded. Try again."],
        InstantQuotationWorkflowUploadStatus.Cancelled => Localizer["Upload cancelled"],
        _ => throw new InvalidOperationException("Unknown upload status."),
    };

    private static string Money(double? amount) => amount is null ? "—" : $"฿{amount.Value:N2}";

    private static string Measurement(double value) => value.ToString("N2");

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        InvalidatePreviewSelectionIntent();
        failedPreviewSelections.Clear();
        selectedPreviewUploadId = null;
        selectedFailedPreviewKey = null;
        repricing.ClearOwnership();
        pendingPreviewSelections.Clear();
        pendingConfigurations.Clear();
        selectedPendingPreviewKey = null;
        localPreviewThumbnails.Clear();
        materialDisplaySubscription?.Dispose();
        materialDisplaySubscription = null;
        quantityDrafts.Clear();
        displayIntents.Clear();
        incompletePreviewKey = null;
        developmentPreviews.Clear();
        developmentClaims.Clear();
        developmentRetries.Clear();
        developmentTotal = null;
        developmentLeadTime = null;
        physicalAnalysisCancellation?.Cancel();
        try
        {
            if (previewInitialization is { } initialization) await initialization;
        }
        finally
        {
            try
            {
                await DisposePreviewInteropAsync(previewInterop);
            }
            finally
            {
                try
                {
                    await DisposePreviewModuleAsync(previewModule);
                }
                finally
                {
                    previewStatusReporter?.Dispose();
                    if (!batchInProgress) uploadBatchGate.Dispose();

                    if (workflow is not null)
                    {
                        await workflow.DisposeAsync();
                    }
                }
            }
        }
    }

    private static async Task DisposePreviewInteropAsync(IJSObjectReference? interop)
    {
        if (interop is null)
        {
            return;
        }

        try
        {
            await interop.InvokeVoidAsync("dispose");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }

        try
        {
            await interop.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }

    private static async Task DisposePreviewModuleAsync(IJSObjectReference? module)
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }

    internal static WorkflowSectionVisibility GetVisibleSections(InstantQuotationWorkflowState state) => state switch
    {
        InstantQuotationWorkflowState.Empty => new(Upload: true),
        InstantQuotationWorkflowState.Uploading => new(Upload: true, Viewer: true, Parts: true, Configuration: true),
        InstantQuotationWorkflowState.Uploaded => new(Upload: true, Viewer: true, Parts: true, Configuration: true),
        InstantQuotationWorkflowState.Error => new(Upload: true, Error: true),
        InstantQuotationWorkflowState.MultiPart => new(Upload: true, Viewer: true, Parts: true, Configuration: true),
        InstantQuotationWorkflowState.Configured => new(Upload: true, Viewer: true, Parts: true, Configuration: true),
        InstantQuotationWorkflowState.Review => new(Viewer: true, Review: true),
        InstantQuotationWorkflowState.CustomerDetails => new(Viewer: true, CustomerDetails: true),
        InstantQuotationWorkflowState.Submitted => new(Submitted: true),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown instant quotation workflow state."),
    };

    public static string ViewerPartKey(Guid partId) => partId.ToString("N");

    public static VisibleAnalyticsMilestones GetVisibleAnalyticsMilestones(
        bool configurationVisible,
        bool reviewVisible,
        bool estimateComplete,
        bool reviewQuoteComplete,
        long authoritativeQuoteRevision) => authoritativeQuoteRevision > 0
            ? new(
                configurationVisible && estimateComplete,
                reviewVisible && reviewQuoteComplete)
            : new(false, false);

    public readonly record struct VisibleAnalyticsMilestones(bool EstimateShown, bool ReviewReached);

    private sealed record ThicknessStatus(
        bool Available,
        bool Warning,
        bool Incomplete,
        string Process,
        double LimitMm,
        double? MinMm,
        bool Visible,
        long Revision);

    private async Task<bool> KeepDevelopmentPreviewAsync(
        InstantQuotationWorkflowUploadViewModel upload, string key, DevelopmentPreviewPart? retryPresentation)
    {
        if (!DevelopmentPreviewEnabled || disposed || developmentRetries.Contains(upload.LocalId)
            || upload.Status is not InstantQuotationWorkflowUploadStatus.Error
            || upload.ProblemCategory is not (InstantQuotationProblemCategory.DependencyUnavailable
                or InstantQuotationProblemCategory.Unexpected)
            || Parts.Any(part => part.PreviewCorrelationId == upload.LocalId)
            || !developmentClaims.TryGetValue(upload.LocalId, out var claim)
            || !claim.IsValid()
            || !AdditiveGeometryValidator.Validate(DevelopmentGeometry(claim), 1).IsValid)
            return false;
        if (!developmentPreviews.ContainsKey(upload.LocalId))
        {
            if (developmentPreviews.Count == 0)
            {
                developmentPreviewGeneration++;
                if (Parts.Count == 0) previewAttached = false;
            }
            var part = new DevelopmentPreviewPart(upload.LocalId, key, upload.DisplayFileName, claim);
            if (retryPresentation?.LocalId == upload.LocalId)
            {
                part.MaterialKey = retryPresentation.MaterialKey;
                part.Color = retryPresentation.Color;
                part.Preference = retryPresentation.Preference;
                part.Quantity = retryPresentation.Quantity;
            }
            developmentPreviews[upload.LocalId] = part;
            developmentStep = DevelopmentPreviewStep.Configuration;
            RepriceDevelopmentPreview();
            await InvokeAsync(StateHasChanged);
        }
        return true;
    }

    private static GeometryInput DevelopmentGeometry(InstantQuotationGeometryClaim claim) => new()
    {
        HeightMm = claim.DimensionZmm,
        VolumeMm3 = claim.VolumeMm3,
        FootprintMm2 = claim.DimensionXmm * claim.DimensionYmm,
        AreaProfileMm2 = claim.AreaProfileMm2 ?? [],
        PerimeterProfileMm = claim.PerimeterProfileMm ?? [],
        UnsupportedAreaProfileMm2 = claim.UnsupportedAreaProfileMm2 ?? [],
    };

    private void RepriceDevelopmentPreview()
    {
        developmentTotal = null;
        developmentLeadTime = null;
        foreach (var part in developmentPreviews.Values)
        {
            part.Price = null;
            var material = PricingCatalog.ResolveMaterial(part.MaterialKey);
            var geometry = DevelopmentGeometry(part.Claim);
            if (material is null || (part.Claim.BodyCount == 1 && !PricingCatalog.IsColorSupported(material.Key, part.Color))
                || !AdditiveGeometryValidator.Validate(geometry, part.Quantity).IsValid)
                continue;
            try
            {
                // Existing geometry-only kernel. These values never authorize physical pricing.
                var quote = PricingEngine.QuoteItem(geometry, material, part.Quantity, part.Preference);
                if (new[] { quote.UnitPrice, quote.Subtotal, quote.PrintTimeMinutesPerUnit,
                    quote.WeightGramsPerUnit, quote.BoundingCm3PerUnit }.All(double.IsFinite)
                    && quote.UnitPrice > 0 && quote.Subtotal > 0 && quote.PrintTimeMinutesPerUnit > 0)
                    part.Price = quote;
            }
            catch (ArgumentException)
            {
                // Invalid or unsupported advisory geometry remains visibly unavailable.
            }
            catch (OverflowException)
            {
                // Do not turn out-of-range local data into an authoritative or zero price.
            }
        }
        if (developmentPreviews.Count == 0 || developmentPreviews.Values.Any(part => part.Price is null)) return;
        var lines = developmentPreviews.Values.ToArray();
        try
        {
            var shipping = ShippingCalculator.CustomerShippingThb(
                lines.Sum(part => part.Price!.WeightGramsPerUnit * part.Quantity),
                lines.Sum(part => part.Price!.BoundingCm3PerUnit * part.Quantity));
            var total = PricingEngine.QuoteOrder(lines.Select(part => new OrderLine
            {
                Process = part.Price!.Process,
                Subtotal = part.Price.Subtotal,
            }), shipping);
            var leadTime = AdditiveLeadTimeCalculator.Calculate(lines.Select(part => new AdditiveLeadTimeLine
            {
                MinutesPerUnit = Convert.ToDecimal(part.Price!.PrintTimeMinutesPerUnit),
                Quantity = part.Quantity,
            }));
            if (double.IsFinite(total.FinalOrderPrice) && total.FinalOrderPrice > 0)
            {
                developmentTotal = total;
                developmentLeadTime = leadTime;
            }
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }
    }

    private async Task ChangeDevelopmentSettingAsync(Guid localId, string setting, ChangeEventArgs args)
    {
        if (!DevelopmentPreviewEnabled || !developmentPreviews.TryGetValue(localId, out var part)) return;
        var value = args.Value?.ToString() ?? string.Empty;
        switch (setting)
        {
            case "material" when PricingCatalog.ResolveMaterial(value) is not null:
                part.MaterialKey = value;
                part.Color = GetColors(value).FirstOrDefault() ?? "Any";
                if (PricingCatalog.ResolveMaterial(value)?.Process is PrintProcess.Resin)
                    part.Preference = BuildPreference.Standard;
                break;
            case "color" when part.Claim.BodyCount == 1
                && PricingCatalog.IsColorSupported(part.MaterialKey, value):
                part.Color = value;
                break;
            case "quantity" when int.TryParse(value, out var quantity)
                && quantity >= 1 && quantity <= PricingCatalog.MaximumAdditiveQuantity:
                part.Quantity = quantity;
                break;
            case "preference" when Enum.TryParse<BuildPreference>(value, true, out var preference)
                && Enum.IsDefined(preference):
                part.Preference = preference;
                break;
            default:
                return;
        }
        if (part.Claim.BodyCount > 1) part.Color = "Any";
        RepriceDevelopmentPreview();
        await InvokePreviewAsync("updateDevelopmentPreview", part.Key, part.Color);
    }

    private string DevelopmentInputId(string field, DevelopmentPreviewPart part) =>
        developmentPreviews.Keys.First() == part.LocalId ? "demo-" + field : "demo-" + field + "-" + part.LocalId.ToString("N");

    private void ShowDevelopmentReview()
    {
        if (HasDevelopmentPreview)
            developmentStep = DevelopmentPreviewStep.Review;
    }

    private void ShowDevelopmentContact()
    {
        if (HasDevelopmentPreview)
            developmentStep = DevelopmentPreviewStep.Contact;
    }

    private void ShowDevelopmentConfiguration() => developmentStep = DevelopmentPreviewStep.Configuration;

    private void ReturnDevelopmentReview() => developmentStep = DevelopmentPreviewStep.Review;

    private async Task ClearDevelopmentPreviewAsync(bool release = true)
    {
        developmentSelectionGeneration++;
        var keys = developmentPreviews.Values.Select(part => part.Key).ToArray();
        developmentPreviews.Clear();
        developmentClaims.Clear();
        developmentTotal = null;
        developmentLeadTime = null;
        developmentStep = DevelopmentPreviewStep.Configuration;
        if (release)
        {
            foreach (var key in keys)
                await InvokePreviewAsync("release", key);
        }
    }

    private async Task RetryDevelopmentUploadAsync(Guid localId)
    {
        if (!DevelopmentPreviewEnabled || !developmentPreviews.ContainsKey(localId)) return;
        await RetryUploadAsync(localId);
    }

    private enum DevelopmentPreviewStep
    {
        Configuration,
        Review,
        Contact,
    }

    private sealed class DevelopmentPreviewPart(
        Guid localId, string key, string name, InstantQuotationGeometryClaim claim)
    {
        public Guid LocalId { get; } = localId;
        public string Key { get; } = key;
        public string Name { get; } = name;
        public InstantQuotationGeometryClaim Claim { get; } = claim;
        public string MaterialKey { get; set; } = "PLA";
        public string Color { get; set; } = "Any";
        public BuildPreference Preference { get; set; } = BuildPreference.Standard;
        public int Quantity { get; set; } = 1;
        public ItemQuote? Price { get; set; }
        public bool Attached { get; set; }
    }

    private enum PendingWorkflowFocus
    {
        None,
        Configuration,
        Review,
        CustomerDetails,
    }

    internal sealed record WorkflowSectionVisibility(
        bool Upload = false,
        bool Error = false,
        bool Viewer = false,
        bool Parts = false,
        bool Configuration = false,
        bool Review = false,
        bool CustomerDetails = false,
        bool Submitted = false);
}
