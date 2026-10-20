using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
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
    private readonly Dictionary<Guid, InstantQuotationPartConfiguration> displayIntents = [];
    private readonly Dictionary<Guid, (long Revision, string Raw)> quantityDrafts = [];
    private long quantityDraftRevision;
    private ElementReference configurationSection;
    private ElementReference reviewSection;
    private ElementReference customerDetailsSection;
    private IJSObjectReference? previewModule;
    private IJSObjectReference? previewInterop;
    private DotNetObjectReference<InstantQuotationWorkflow>? previewStatusReporter;
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

    private bool HasLocalPreviewSelection => selectedPreviewUploadId is not null || selectedFailedPreviewKey is not null;

    private string? SelectedLocalPreviewKey => selectedFailedPreviewKey
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
            return category is "unreadable" or "complexity" ? category : "unavailable";
        }
        catch (JSException) { return "unavailable"; }
    }

    private string? LocalThumbnail(string? key) => key is not null ? localPreviewThumbnails.GetValueOrDefault(key) : null;

    private WorkflowSectionVisibility VisibleSections => pendingPreviewSelections.Count > 0 || HasLocalFileFailures || incompletePreviewKey is not null
        ? new(Upload: true, Error: false, Viewer: true, Parts: true, Configuration: Parts.Count > 0)
        : (State is InstantQuotationWorkflowState.Error && Parts.Count > 0)
            || (State is InstantQuotationWorkflowState.Empty && HasMaterialDisplay && Parts.Count > 0)
            ? new(Upload: true, Error: State is InstantQuotationWorkflowState.Error, Viewer: true, Parts: true, Configuration: true)
            : GetVisibleSections(State);

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
            StateHasChanged();
        }));
    }

    private bool InputDisabled => IsBusy || batchInProgress || bulkApplying;

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
                    await UpdatePartAppearanceAsync(id, color);
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
            if (disposed || InputDisabled || IsRepricing || failedPreviewSelections.Count > 0
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
            var selected = selectedPreviewPartId is { } selectedId
                ? Parts.FirstOrDefault(part => part.PartId == selectedId)
                : null;
            return selected is null ? Parts.Take(1).ToArray() : [selected];
        }
    }

    private IReadOnlyList<InstantQuotationWorkflowMaterialOption> Materials => workflow?.Materials ?? [];

    private bool HasUnacceptedConfiguration(InstantQuotationWorkflowPartViewModel part) =>
        quantityEdits.IsPending(part.PartId)
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
        if (firstRender)
        {
            await EnsurePreviewInteropAsync();
        }

        await EnsurePreviewInputBoundAsync();
        selectedBulkParts.IntersectWith(BulkParts.Select(part => part.PartId));
        if (VisibleSections.Parts && previewInterop is not null)
        {
            await InvokePreviewAsync("setBulkSelectionState", bulkSelectAll, BulkSelectionMixed);
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
                    && source is { Length: <= 262144 } && source.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
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

        if ((VisibleSections.Review || VisibleSections.CustomerDetails) && previewInterop is not null)
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
                pendingPreviewSelections[keys[index]] = new(browserFiles[index].Name, selectionGeneration);
            await InvokeAsync(StateHasChanged);

            var analyzed = new List<(IBrowserFile File, string PreviewKey, InstantQuotationGeometryClaim Claim)>();
            for (var index = 0; index < browserFiles.Length; index++)
            {
                if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                try
                {
                    var claim = await previewInterop!.InvokeAsync<InstantQuotationGeometryClaim>(
                        "getGeometryClaim",
                        keys[index]);
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
                    analyzed.Add((browserFiles[index], keys[index], claim));
                }
                catch (JSException)
                {
                    if (disposed) return;
                    if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                    var category = await ReadPreviewFailureCategoryAsync(keys[index]);
                    if (disposed) return;
                    if (!IsPendingPreview(keys[index], selectionGeneration)) continue;
                    pendingPreviewSelections.Remove(keys[index]);
                    failedPreviewSelections[keys[index]] = new(browserFiles[index].Name, category);
                    if (Parts.Count == 0 && selectionGeneration == previewSelectionGeneration) selectedFailedPreviewKey ??= keys[index];
                    await InvokePreviewAsync("release", keys[index]);
                    await ReportPreviewUnavailableAsync();
                }
            }

            if (analyzed.Count == 0)
            {
                return;
            }

            await analytics.RecordUploadStartAsync(
                $"batch-{Guid.NewGuid():N}",
                analyzed.Count);
            if (disposed) return;

            var files = analyzed
                .Select(item => new InstantQuotationWorkflowUploadFile(
                    item.File.Name,
                    item.File.ContentType,
                    item.File.Size,
                    item.Claim,
                    cancellationToken => Task.FromResult<Stream>(item.File.OpenReadStream(
                        InstantQuotationWorkflowCoordinator.MaximumFileSize,
                        cancellationToken))))
                .ToArray();
            var reservedIds = workflow.ReserveUploads(files);

            for (var index = 0; index < reservedIds.Count; index++)
            {
                previewKeys[reservedIds[index]] = analyzed[index].PreviewKey;
            }

            if (selectionGeneration == previewSelectionGeneration)
            {
                autoPreviewLocalId = reservedIds.LastOrDefault();
            }

            var uploadTask = workflow.UploadReservedAsync(reservedIds, default);
            await InvokeAsync(StateHasChanged);
            try
            {
                await uploadTask;
            }
            finally
            {
                if (!disposed)
                {
                    if (selectionGeneration == previewSelectionGeneration && autoPreviewLocalId is not null)
                    {
                        autoPreviewLocalId = reservedIds.Reverse()
                            .Where(localId => Uploads.Any(upload => upload.LocalId == localId
                                && upload.Status is InstantQuotationWorkflowUploadStatus.Uploaded))
                            .Select(static localId => (Guid?)localId).FirstOrDefault();
                    }
                    await ReconcilePreviewsAsync();
                }
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

    private bool IsPendingPreview(string key, long generation) =>
        pendingPreviewSelections.TryGetValue(key, out var pending) && pending.Generation == generation;

    private async Task RemovePendingPreviewAsync(string key)
    {
        if (disposed || bulkApplying || !pendingPreviewSelections.Remove(key)) return;
        // Release only this local selection; late parser completion cannot admit a removed key.
        if (incompletePreviewKey == key) await RemoveIncompletePreviewAsync();
        else await InvokePreviewAsync("release", key);
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
        selectedPreviewUploadId = localId;
        selectedFailedPreviewKey = null;
        await InvokePreviewAsync("selectLocalPreview", previewKeys.GetValueOrDefault(localId));
    }

    private async Task RemoveFailedUploadAsync(Guid localId)
    {
        if (disposed || InputDisabled || IsRepricing || workflow?.DismissFailedUpload(localId) != true) return;
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
        await QuarantinePreviewAsync(localId);
    }

    private async Task RetryUploadAsync(Guid localId)
    {
        if (bulkApplying || disposed || InputDisabled || IsRepricing) return;
        if (workflow is not null)
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
                }
                catch (JSException)
                {
                    await ReportPreviewUnavailableAsync();
                }
            }

            await workflow.RetryAsync(localId, default);
            await ReconcilePreviewsAsync();
        }
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
        if (previewInterop is not null || previewUnavailable)
        {
            return;
        }

        try
        {
            previewModule = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "/dist/instant-quotation-workflow.mjs");
            previewStatusReporter = DotNetObjectReference.Create(this);
            previewInterop = await previewModule.InvokeAsync<IJSObjectReference>(
                "createInstantQuotationWorkflowInterop",
                previewStatusReporter);
        }
        catch (JSException)
        {
            await ReportPreviewUnavailableAsync();
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

    private async Task ReconcilePreviewsAsync()
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
                            && source is { Length: <= 262144 } && source.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
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
        InvalidatePreviewSelectionIntent();
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
        var color = colors.Contains(part.Configuration.Color, StringComparer.Ordinal)
            ? part.Configuration.Color
            : colors.First();
        await UpdateConfigurationAsync(part, material, color, part.Configuration.Quantity, async current =>
        {
            if (!current()) return;
            await InvokePreviewAsync("setThicknessMaterial", ViewerPartKey(partId), material);
            if (current()) await UpdatePartAppearanceAsync(partId, color);
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
            current => current() ? UpdatePartAppearanceAsync(partId, color) : Task.CompletedTask);
    }

    private void OnQuantityInput(Guid partId, ChangeEventArgs args)
    {
        if (bulkApplying || disposed) return;
        if (Parts.All(part => part.PartId != partId)) return;
        quantityDrafts[partId] = (checked(++quantityDraftRevision), args.Value?.ToString() ?? "");
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

    private async Task UpdatePartAppearanceAsync(Guid partId, string color)
    {
        if (disposed || Parts.All(part => part.PartId != partId)) return;
        await InvokePreviewAsync("updatePartAppearance", ViewerPartKey(partId), color);
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
        disposed = true;
        InvalidatePreviewSelectionIntent();
        failedPreviewSelections.Clear();
        selectedPreviewUploadId = null;
        selectedFailedPreviewKey = null;
        repricing.ClearOwnership();
        pendingPreviewSelections.Clear();
        localPreviewThumbnails.Clear();
        materialDisplaySubscription?.Dispose();
        materialDisplaySubscription = null;
        quantityDrafts.Clear();
        displayIntents.Clear();
        incompletePreviewKey = null;
        physicalAnalysisCancellation?.Cancel();
        try
        {
            await DisposePreviewInteropAsync();
        }
        finally
        {
            try
            {
                await DisposePreviewModuleAsync();
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

    private async Task DisposePreviewInteropAsync()
    {
        if (previewInterop is null)
        {
            return;
        }

        try
        {
            await previewInterop.InvokeVoidAsync("dispose");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }

        try
        {
            await previewInterop.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
    }

    private async Task DisposePreviewModuleAsync()
    {
        if (previewModule is null)
        {
            return;
        }

        try
        {
            await previewModule.DisposeAsync();
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
