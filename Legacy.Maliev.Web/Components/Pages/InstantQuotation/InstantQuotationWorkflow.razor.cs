using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public partial class InstantQuotationWorkflow : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IServiceProvider Services { get; set; } = null!;

    [Parameter]
    public InstantQuotationWorkflowState InitialState { get; set; } = InstantQuotationWorkflowState.Empty;

    private InstantQuotationWorkflowCoordinator? workflow;
    private bool initializationFailed;

    private InstantQuotationWorkflowState State => initializationFailed
        ? InstantQuotationWorkflowState.Error
        : workflow?.State ?? InitialState;

    private WorkflowSectionVisibility VisibleSections =>
        State is InstantQuotationWorkflowState.Error && Parts.Count > 0
            ? new(Upload: true, Error: true, Viewer: true, Parts: true, Configuration: true)
            : GetVisibleSections(State);

    private bool IsBusy => State is InstantQuotationWorkflowState.Uploading;

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

        var principal = Services.GetService<IHttpContextAccessor>()?.HttpContext?.User;
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

        var files = args.GetMultipleFiles(100)
            .Select(file => new InstantQuotationWorkflowUploadFile(
                file.Name,
                file.ContentType,
                file.Size,
                cancellationToken => Task.FromResult<Stream>(file.OpenReadStream(
                    InstantQuotationWorkflowCoordinator.MaximumFileSize,
                    cancellationToken))))
            .ToArray();
        await workflow.UploadAsync(files, default);
    }

    private void CancelUpload(Guid localId) => workflow?.Cancel(localId);

    private async Task RetryUploadAsync(Guid localId)
    {
        if (workflow is not null)
        {
            await workflow.RetryAsync(localId, default);
        }
    }

    private async Task RemovePartAsync(Guid partId)
    {
        if (workflow is not null)
        {
            await workflow.RemoveAsync(partId, default);
        }
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
        if (workflow is not null)
        {
            await workflow.DisposeAsync();
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
