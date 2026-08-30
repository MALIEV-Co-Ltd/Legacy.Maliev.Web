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

    public IReadOnlyList<string> InvalidFields { get; init; } = [];

    public string Mobile { get; init; } = string.Empty;
    public string BillingBuilding { get; init; } = string.Empty;
    public string BillingStreet1 { get; init; } = string.Empty;
    public string BillingStreet2 { get; init; } = string.Empty;
    public string BillingCity { get; init; } = string.Empty;
    public string BillingProvince { get; init; } = string.Empty;
    public string BillingPostalCode { get; init; } = string.Empty;
    public string TaxBranch { get; init; } = "head-office";
    public string TaxBranchCode { get; init; } = string.Empty;
    public bool ShipToBillingAddress { get; init; } = true;
    public string ShippingBuilding { get; init; } = string.Empty;
    public string ShippingStreet1 { get; init; } = string.Empty;
    public string ShippingStreet2 { get; init; } = string.Empty;
    public string ShippingCity { get; init; } = string.Empty;
    public string ShippingProvince { get; init; } = string.Empty;
    public string ShippingPostalCode { get; init; } = string.Empty;
    public string ShippingCountry { get; init; } = string.Empty;

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

public sealed record InstantQuotationCountryOption(string Name, string? LocalizedName = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(LocalizedName) ? Name : LocalizedName;
}
