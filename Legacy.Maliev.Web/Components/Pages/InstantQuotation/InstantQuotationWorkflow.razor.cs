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
    private readonly RepricingActivity repricing = new();
    private Guid? selectedPreviewPartId;
    private PendingWorkflowFocus pendingFocus;

    private ThicknessStatus? SelectedThickness => selectedPreviewPartId is { } id
        && thicknessStatuses.TryGetValue(id, out var status) ? status : null;

    private bool ThicknessNeedsAttention => thicknessStatuses.Values.Any(
        status => status.Warning || status.Incomplete);

    private InstantQuotationWorkflowState State => initializationFailed
        ? InstantQuotationWorkflowState.Error
        : workflow?.State ?? InitialState;

    private WorkflowSectionVisibility VisibleSections
    {
        get
        {
            if (HasDevelopmentPreview && Parts.Count == 0)
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
            StateHasChanged();
        }));
    }

    private bool InputDisabled => IsBusy || batchInProgress || developmentRetries.Count > 0;

    private bool CanEnterReview
    {
        get
        {
            if (disposed || IsRepricing || workflow?.State is not InstantQuotationWorkflowState.Configured
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

        if (HasDevelopmentPreview && previewInterop is not null)
        {
            foreach (var part in developmentPreviews.Values.Where(part => !part.Attached).ToArray())
            {
                try
                {
                    var attached = await previewInterop.InvokeAsync<bool>(
                        "retainDevelopmentPreview", part.Key, developmentPreviewCanvas);
                    if (!disposed && developmentPreviews.TryGetValue(part.LocalId, out var current)
                        && ReferenceEquals(part, current))
                        part.Attached = attached;
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
        try
        {
            uploadSelectionError = null;
            // A new accepted selection replaces the one local exceptional preview,
            // not previously admitted parts. Rejected/normal replacements also release it.
            await RemoveIncompletePreviewAsync();
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
            if (keys.Length != browserFiles.Length)
            {
                foreach (var key in keys)
                {
                    await InvokePreviewAsync("release", key);
                }

                await ReportPreviewUnavailableAsync();
                return;
            }

            var analyzed = new List<(IBrowserFile File, string PreviewKey, InstantQuotationGeometryClaim Claim)>();
            for (var index = 0; index < browserFiles.Length; index++)
            {
                try
                {
                    var claim = await previewInterop!.InvokeAsync<InstantQuotationGeometryClaim>(
                        "getGeometryClaim",
                        keys[index]);
                    if (await previewInterop!.InvokeAsync<bool>("isIncompletePreview", keys[index]))
                    {
                        await RemoveIncompletePreviewAsync();
                        incompletePreviewKey = keys[index];
                        incompletePreviewMeasured = false;
                        incompletePreviewRevision = 0;
                        await InvokeAsync(StateHasChanged);
                    }
                    analyzed.Add((browserFiles[index], keys[index], claim));
                }
                catch (JSException)
                {
                    await InvokePreviewAsync("quarantine", keys[index]);
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
                if (DevelopmentPreviewEnabled)
                    developmentClaims[reservedIds[index]] = analyzed[index].Claim;
            }

            var uploadTask = workflow.UploadReservedAsync(reservedIds, default);
            await InvokeAsync(StateHasChanged);
            try
            {
                await uploadTask;
            }
            finally
            {
                await ReconcilePreviewsAsync();
            }
        }
        finally
        {
            batchInProgress = false;
            uploadBatchGate.Release();
        }
    }

    private async Task CancelUpload(Guid localId)
    {
        workflow?.Cancel(localId);
        developmentPreviews.Remove(localId);
        developmentClaims.Remove(localId);
        RepriceDevelopmentPreview();
        await QuarantinePreviewAsync(localId);
    }

    private async Task RetryUploadAsync(Guid localId)
    {
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
                    var key = await previewInterop.InvokeAsync<string>("retry", previousKey);
                    previewKeys[localId] = key;
                    if (development)
                    {
                        var claim = await previewInterop.InvokeAsync<InstantQuotationGeometryClaim>("getGeometryClaim", key);
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

    private async Task RemovePartAsync(Guid partId)
    {
        InvalidatePhysicalAnalysis(partId);
        if (workflow is not null)
        {
            var part = Parts.SingleOrDefault(item => item.PartId == partId);
            await workflow.RemoveAsync(partId, default);
            if (part is not null && Parts.All(item => item.PartId != partId))
            {
                thicknessStatuses.Remove(partId);
                thicknessToggleRevisions.Remove(partId);
                quantityEdits.Forget(partId);
                quantityDrafts.Remove(partId);
                displayIntents.Remove(partId);
                await ReleasePreviewAsync(part.PreviewCorrelationId);
                if (selectedPreviewPartId == partId)
                {
                    selectedPreviewPartId = Parts.FirstOrDefault()?.PartId;
                    if (selectedPreviewPartId is { } nextPartId)
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
        if (previewInterop is null || fileInput is null)
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

    private async Task ReconcilePreviewsAsync(DevelopmentPreviewPart? retryPresentation = null)
    {
        if (previewInterop is null)
        {
            return;
        }

        foreach (var (localId, key) in previewKeys.ToArray())
        {
            var upload = Uploads.SingleOrDefault(item => item.LocalId == localId);
            var part = Parts.SingleOrDefault(item => item.PreviewCorrelationId == localId);
            if (upload?.Status is InstantQuotationWorkflowUploadStatus.Uploaded && part is not null)
            {
                try
                {
                    await previewInterop.InvokeVoidAsync(
                        "admit", key, ViewerPartKey(part.PartId), part.Configuration.MaterialKey);
                    developmentPreviews.Remove(localId);
                    developmentClaims.Remove(localId);
                    selectedPreviewPartId ??= part.PartId;
                    await analytics.RecordStageResultAsync(
                        InstantQuotationStage.Thumbnail,
                        InstantQuotationStageResult.Success,
                        null,
                        FileType(part.DisplayFileName));
                }
                catch (JSException)
                {
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
        selectedPreviewPartId = partId;
        await InvokePreviewAsync("select", ViewerPartKey(partId));
    }

    private bool IsPreviewSelected(Guid partId) => selectedPreviewPartId == partId;

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
        InvalidatePhysicalAnalysis(partId);
        var part = Parts.Single(item => item.PartId == partId);
        var material = args.Value?.ToString() ?? part.Configuration.MaterialKey;
        var colors = GetColors(material);
        var color = part.Geometry.BodyCount > 1 ? "Any" : colors.First();
        await UpdateConfigurationAsync(part, material, color, part.Configuration.Quantity, async () =>
        {
            var accepted = Parts.SingleOrDefault(item => item.PartId == partId);
            if (accepted is null) return;
            await InvokePreviewAsync("setThicknessMaterial", ViewerPartKey(partId), accepted.Configuration.MaterialKey);
            await UpdatePartAppearanceAsync(partId);
        });
    }

    private async Task ChangeColorAsync(Guid partId, ChangeEventArgs args)
    {
        InvalidatePhysicalAnalysis(partId);
        var part = Parts.Single(item => item.PartId == partId);
        var color = args.Value?.ToString() ?? part.Configuration.Color;
        await UpdateConfigurationAsync(
            part,
            part.Configuration.MaterialKey,
            color,
            part.Configuration.Quantity);
        await UpdatePartAppearanceAsync(partId);
    }

    private void OnQuantityInput(Guid partId, ChangeEventArgs args)
    {
        if (Parts.All(part => part.PartId != partId)) return;
        quantityDrafts[partId] = (checked(++quantityDraftRevision), args.Value?.ToString() ?? "");
        minimumDisplayGeneration = workflow?.MaterialPriceDisplay.Generation ?? 0;
        displayIntents.Remove(partId);
        quantityEdits.Begin(partId);
    }

    private async Task ChangeQuantityAsync(Guid partId, ChangeEventArgs args)
    {
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
        InvalidatePhysicalAnalysis(partId);
        var part = Parts.Single(item => item.PartId == partId);
        displayIntents[partId] = part.Configuration with { BuildPreference = buildPreference };
        return RepriceAsync(() => workflow?.UpdateConfigurationAsync(
            part.PartId,
            part.Configuration.MaterialKey,
            part.Configuration.Color,
            part.Configuration.Quantity,
            buildPreference,
            default) ?? Task.CompletedTask);
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
        Func<Task>? afterUpdate = null)
    {
        displayIntents[part.PartId] = part.Configuration with { MaterialKey = material, Color = color, Quantity = quantity };
        return RepriceAsync(() => workflow?.UpdateConfigurationAsync(part.PartId, material, color, quantity, default)
            ?? Task.CompletedTask, afterUpdate);
    }

    private async Task RepriceAsync(Func<Task> update, Func<Task>? afterUpdate = null)
    {
        minimumDisplayGeneration = workflow?.MaterialPriceDisplay.Generation ?? 0;
        try { await repricing.RunAsync(update, () => InvokeAsync(StateHasChanged), afterUpdate); }
        finally
        {
            if (!repricing.IsActive) displayIntents.Clear();
            if (!disposed) await InvokeAsync(StateHasChanged);
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

    private Task UpdatePartAppearanceAsync(Guid partId)
    {
        var accepted = Parts.SingleOrDefault(part => part.PartId == partId);
        return accepted is null || accepted.Geometry.BodyCount > 1
            ? Task.CompletedTask
            : InvokePreviewAsync("updatePartAppearance", ViewerPartKey(partId), accepted.Configuration.Color);
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
        await ChangeMaterialAsync(change.PartId, new ChangeEventArgs { Value = change.Value });
        workflow?.EnterReview();
    }

    private async Task ChangeReviewColorAsync((Guid PartId, string Value) change)
    {
        await ChangeColorAsync(change.PartId, new ChangeEventArgs { Value = change.Value });
        workflow?.EnterReview();
    }

    private async Task ChangeReviewQuantityAsync((Guid PartId, string Value) change)
    {
        if (!int.TryParse(change.Value, out var quantity)
            || quantity < 1
            || quantity > PricingCatalog.MaximumAdditiveQuantity)
        {
            return;
        }

        await ChangeQuantityAsync(change.PartId, new ChangeEventArgs { Value = quantity });
        workflow?.EnterReview();
    }

    private async Task ChangeReviewBuildPreferenceAsync((Guid PartId, string Value) change)
    {
        if (!Enum.TryParse<BuildPreference>(change.Value, true, out var preference)
            || !Enum.IsDefined(preference))
        {
            return;
        }

        await ChangeBuildPreferenceAsync(change.PartId, preference);
        workflow?.EnterReview();
    }

    private async Task EditReviewPartAsync(Guid partId)
    {
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
        workflow?.EnterCustomerDetails();
        pendingFocus = PendingWorkflowFocus.CustomerDetails;
    }

    private void ReturnToConfiguration()
    {
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
        disposed = true;
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
                uploadBatchGate.Dispose();

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
