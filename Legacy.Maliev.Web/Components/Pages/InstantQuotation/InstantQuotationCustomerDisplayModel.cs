namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public sealed record InstantQuotationCustomerDisplayModel(
    string AntiforgeryFieldName,
    string AntiforgeryRequestToken,
    string FirstName,
    string LastName,
    string Email,
    string Telephone,
    string Country,
    string Company,
    string TaxNumber,
    string Description,
    IReadOnlyList<InstantQuotationCountryOption> Countries,
    string? SubmissionStatus,
    int? RequestReference,
    string? ProblemCategory)
{
    public const string CompletedStatus = "completed";
    public const string PartialStatus = "partial";
    public const string RejectedStatus = "rejected";

    public static InstantQuotationCustomerDisplayModel Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        "Thailand",
        string.Empty,
        string.Empty,
        string.Empty,
        [],
        null,
        null,
        null);
}

public sealed record InstantQuotationCountryOption(string Name);
