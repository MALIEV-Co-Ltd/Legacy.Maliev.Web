namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Identifies the quotation workflow stage used to select visible sections and allowed transitions.</summary>
public enum InstantQuotationWorkflowState
{
    /// <summary>No admitted files are available in the workflow.</summary>
    Empty,
    /// <summary>At least one selected file is being uploaded.</summary>
    Uploading,
    /// <summary>An admitted file is ready for configuration.</summary>
    Uploaded,
    /// <summary>An upload or part operation requires error feedback.</summary>
    Error,
    /// <summary>Multiple admitted parts are ready for configuration.</summary>
    MultiPart,
    /// <summary>All admitted parts have accepted configurations.</summary>
    Configured,
    /// <summary>The complete configured quotation is being reviewed.</summary>
    Review,
    /// <summary>Customer details are being collected after review.</summary>
    CustomerDetails,
    /// <summary>Submission feedback is being rendered from the customer model.</summary>
    Submitted,
}
