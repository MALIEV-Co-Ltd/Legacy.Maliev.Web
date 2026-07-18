using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
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

    private InstantQuotationWorkflowCoordinator? workflow;
    private bool initializationFailed;
    private InputFile? fileInput;
    private ElementReference previewCanvas;
    private IJSObjectReference? previewModule;
    private IJSObjectReference? previewInterop;
    private DotNetObjectReference<InstantQuotationWorkflow>? previewStatusReporter;
    private readonly Dictionary<Guid, string> previewKeys = [];
    private readonly SemaphoreSlim uploadBatchGate = new(1, 1);
    private bool previewAttached;
    private bool previewUnavailable;
    private bool batchInProgress;

    private InstantQuotationWorkflowState State => initializationFailed
        ? InstantQuotationWorkflowState.Error
        : workflow?.State ?? InitialState;

    private WorkflowSectionVisibility VisibleSections =>
        State is InstantQuotationWorkflowState.Error && Parts.Count > 0
            ? new(Upload: true, Error: true, Viewer: true, Parts: true, Configuration: true)
            : GetVisibleSections(State);

    private bool IsBusy => State is InstantQuotationWorkflowState.Uploading;

    private bool InputDisabled => IsBusy || batchInProgress;

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
        InstantQuotationWorkflowState.Submitted => Localizer["Your quotation request was submitted."],
        _ => throw new InvalidOperationException("Unknown instant quotation workflow state."),
    };

    private string PreviewStatusMessage => previewUnavailable
        ? Localizer["3D preview is unavailable. You can continue with your quotation."]
        : Localizer["Use arrow keys to rotate, plus or minus to zoom, 0 to reset, and Home to fit."];

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
    }

    protected override async Task OnInitializedAsync()
    {
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
            ResolveOwnerIdentity(principal));
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
            var files = browserFiles
                .Select(file => new InstantQuotationWorkflowUploadFile(
                    file.Name,
                    file.ContentType,
                    file.Size,
                    cancellationToken => Task.FromResult<Stream>(file.OpenReadStream(
                        InstantQuotationWorkflowCoordinator.MaximumFileSize,
                        cancellationToken))))
                .ToArray();
            var reservedIds = workflow.ReserveUploads(files);

            var previewTask = BeginPreviewSelectionAsync();
            var uploadTask = workflow.UploadReservedAsync(reservedIds, default);
            await InvokeAsync(StateHasChanged);
            var keys = await previewTask;
            for (var index = 0; index < Math.Min(keys.Length, reservedIds.Count); index++)
            {
                previewKeys[reservedIds[index]] = keys[index];
            }

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

    private Task SelectPreviewAsync(Guid partId) => InvokePreviewAsync("select", partId.ToString("N"));

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
        InstantQuotationWorkflowState.Review => new(Viewer: true, Parts: true, Configuration: true, Review: true),
        InstantQuotationWorkflowState.CustomerDetails => new(CustomerDetails: true),
        InstantQuotationWorkflowState.Submitted => new(Submitted: true),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown instant quotation workflow state."),
    };

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
