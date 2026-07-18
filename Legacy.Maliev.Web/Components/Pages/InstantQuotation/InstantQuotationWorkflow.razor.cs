using Microsoft.AspNetCore.Components;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public partial class InstantQuotationWorkflow : ComponentBase
{
    [Parameter]
    public InstantQuotationWorkflowState InitialState { get; set; } = InstantQuotationWorkflowState.Empty;

    private InstantQuotationWorkflowState State => InitialState;

    private WorkflowSectionVisibility VisibleSections => GetVisibleSections(State);

    private bool IsBusy => State is InstantQuotationWorkflowState.Uploading;

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
