using System.Security.Claims;
using Legacy.Maliev.Web.Application;
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
    private ElementReference previewCanvas;
    private ElementReference configurationSection;
    private ElementReference reviewSection;
    private ElementReference customerDetailsSection;
    private IJSObjectReference? previewModule;
    private IJSObjectReference? previewInterop;
    private DotNetObjectReference<InstantQuotationWorkflow>? previewStatusReporter;
    private readonly Dictionary<Guid, string> previewKeys = [];
    private readonly SemaphoreSlim uploadBatchGate = new(1, 1);
    private IInstantQuotationAnalyticsTracker analytics = NoOpInstantQuotationAnalyticsTracker.Instance;
    private bool previewAttached;
    private bool previewUnavailable;
    private bool batchInProgress;
    private Guid? selectedPreviewPartId;
    private PendingWorkflowFocus pendingFocus;

    private InstantQuotationWorkflowState State => initializationFailed
        ? InstantQuotationWorkflowState.Error
        : workflow?.State ?? InitialState;

    private WorkflowSectionVisibility VisibleSections =>
        State is InstantQuotationWorkflowState.Error && Parts.Count > 0
            ? new(Upload: true, Error: true, Viewer: true, Parts: true, Configuration: true)
            : GetVisibleSections(State);

    private bool IsBusy => State is InstantQuotationWorkflowState.Uploading;

    private bool InputDisabled => IsBusy || batchInProgress;

    private bool CanEnterReview => workflow?.State is InstantQuotationWorkflowState.Configured
        && workflow.OrderQuote is not null;

    private IReadOnlyList<InstantQuotationWorkflowUploadViewModel> Uploads => workflow?.Uploads ?? [];

    private IReadOnlyList<InstantQuotationWorkflowPartViewModel> Parts => workflow?.Parts ?? [];

    private IReadOnlyList<InstantQuotationWorkflowMaterialOption> Materials => workflow?.Materials ?? [];

    private InstantQuotationOrderQuote? OrderQuote => workflow?.OrderQuote;

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
        if (sessionStore is null && uploadClient is null && pricingService is null)
        {
            return;
        }

        if (sessionStore is null || uploadClient is null || pricingService is null)
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
            analytics);
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
            var browserFiles = args.GetMultipleFiles(100).ToArray();
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
        await QuarantinePreviewAsync(localId);
    }

    private async Task RetryUploadAsync(Guid localId)
    {
        if (workflow is not null)
        {
            var hasAuthoritativePart = Parts.Any(item => item.PreviewCorrelationId == localId);
            if (!hasAuthoritativePart
                && previewKeys.TryGetValue(localId, out var previousKey)
                && previewInterop is not null)
            {
                try
                {
                    previewKeys[localId] = await previewInterop.InvokeAsync<string>("retry", previousKey);
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
        if (workflow is not null)
        {
            var part = Parts.SingleOrDefault(item => item.PartId == partId);
            await workflow.RemoveAsync(partId, default);
            if (part is not null && Parts.All(item => item.PartId != partId))
            {
                await ReleasePreviewAsync(part.PreviewCorrelationId);
                if (selectedPreviewPartId == partId)
                {
                    selectedPreviewPartId = Parts.FirstOrDefault()?.PartId;
                    if (selectedPreviewPartId is { } nextPartId)
                    {
                        await InvokePreviewAsync("select", nextPartId.ToString("N"));
                    }
                }
            }
        }
    }

    private async Task<string[]> BeginPreviewSelectionAsync()
    {
        await EnsurePreviewInteropAsync();
        if (previewInterop is null || fileInput is null)
        {
            return [];
        }

        try
        {
            return await previewInterop.InvokeAsync<string[]>("beginSelection", fileInput.Element);
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
                await InvokePreviewAsync("admit", key, part.PartId.ToString("N"));
                selectedPreviewPartId ??= part.PartId;
            }
            else if (upload?.Status is InstantQuotationWorkflowUploadStatus.Error or InstantQuotationWorkflowUploadStatus.Cancelled)
            {
                await QuarantinePreviewAsync(localId);
            }
            else if (upload is null)
            {
                await ReleasePreviewAsync(localId);
            }
        }
    }

    private Task QuarantinePreviewAsync(Guid localId) =>
        previewKeys.TryGetValue(localId, out var key)
            ? InvokePreviewAsync("quarantine", key)
            : Task.CompletedTask;

    private async Task ReleasePreviewAsync(Guid localId)
    {
        if (previewKeys.Remove(localId, out var key))
        {
            await InvokePreviewAsync("release", key);
        }
    }

    private async Task SelectPreviewAsync(Guid partId)
    {
        selectedPreviewPartId = partId;
        await InvokePreviewAsync("select", partId.ToString("N"));
    }

    private bool IsPreviewSelected(Guid partId) => selectedPreviewPartId == partId;

    private Task ResetPreviewAsync() => InvokePreviewAsync("reset");

    private Task FitPreviewAsync() => InvokePreviewAsync("fit");

    private Task FullscreenPreviewAsync() => InvokePreviewAsync("fullscreen");

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

    private async Task ChangeMaterialAsync(Guid partId, ChangeEventArgs args)
    {
        var part = Parts.Single(item => item.PartId == partId);
        var material = args.Value?.ToString() ?? part.Configuration.MaterialKey;
        var colors = GetColors(material);
        var color = colors.Contains(part.Configuration.Color, StringComparer.Ordinal)
            ? part.Configuration.Color
            : colors.First();
        await UpdateConfigurationAsync(part, material, color, part.Configuration.Quantity);
    }

    private Task ChangeColorAsync(Guid partId, ChangeEventArgs args)
    {
        var part = Parts.Single(item => item.PartId == partId);
        return UpdateConfigurationAsync(
            part,
            part.Configuration.MaterialKey,
            args.Value?.ToString() ?? part.Configuration.Color,
            part.Configuration.Quantity);
    }

    private Task ChangeQuantityAsync(Guid partId, ChangeEventArgs args)
    {
        var part = Parts.Single(item => item.PartId == partId);
        var quantity = int.TryParse(args.Value?.ToString(), out var value) ? value : part.Configuration.Quantity;
        return UpdateConfigurationAsync(part, part.Configuration.MaterialKey, part.Configuration.Color, quantity);
    }

    private Task UpdateConfigurationAsync(
        InstantQuotationWorkflowPartViewModel part,
        string material,
        string color,
        int quantity) => workflow?.UpdateConfigurationAsync(part.PartId, material, color, quantity, default)
            ?? Task.CompletedTask;

    private void EnterReview()
    {
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
        InstantQuotationWorkflowState.Uploading => new(Upload: true),
        InstantQuotationWorkflowState.Uploaded => new(Viewer: true, Parts: true, Configuration: true),
        InstantQuotationWorkflowState.Error => new(Upload: true, Error: true),
        InstantQuotationWorkflowState.MultiPart => new(Viewer: true, Parts: true, Configuration: true),
        InstantQuotationWorkflowState.Configured => new(Viewer: true, Parts: true, Configuration: true),
        InstantQuotationWorkflowState.Review => new(Review: true),
        InstantQuotationWorkflowState.CustomerDetails => new(CustomerDetails: true),
        InstantQuotationWorkflowState.Submitted => new(Submitted: true),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown instant quotation workflow state."),
    };

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
