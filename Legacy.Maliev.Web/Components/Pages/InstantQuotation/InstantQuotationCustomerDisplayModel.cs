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
    // InteractiveServer parameters cross a JSON boundary. IReadOnlySet cannot
    // be materialized by System.Text.Json; a concrete set retains ordinal locks.
    public HashSet<string> LockedFields { get; init; } = new(StringComparer.Ordinal);
    public bool ProfileUnavailable { get; init; }
    public bool IsLocked(string field) => LockedFields.Contains(field);

    public static InstantQuotationCustomerDisplayModel FromProfile(InstantQuotationCustomerDisplayModel model,
        Legacy.Maliev.Web.Application.InstantQuotationAuthenticatedProfile profile) => model with
        {
            FirstName = profile.Details.FirstName,
            LastName = profile.Details.LastName,
            Email = profile.Details.Email,
            Mobile = profile.Details.Mobile,
            Telephone = profile.Details.Telephone,
            Company = profile.Details.Company,
            TaxNumber = profile.Details.TaxNumber,
            TaxBranch = profile.Details.TaxBranch,
            TaxBranchCode = profile.Details.TaxBranchCode,
            BillingBuilding = profile.Details.BillingBuilding,
            BillingStreet1 = profile.Details.BillingStreet1,
            BillingStreet2 = profile.Details.BillingStreet2,
            BillingCity = profile.Details.BillingCity,
            BillingProvince = profile.Details.BillingProvince,
            BillingPostalCode = profile.Details.BillingPostalCode,
            Country = string.IsNullOrWhiteSpace(profile.Details.Country) ? model.Country : profile.Details.Country,
            ShippingBuilding = profile.Details.ShippingBuilding,
            ShippingStreet1 = profile.Details.ShippingStreet1,
            ShippingStreet2 = profile.Details.ShippingStreet2,
            ShippingCity = profile.Details.ShippingCity,
            ShippingProvince = profile.Details.ShippingProvince,
            ShippingPostalCode = profile.Details.ShippingPostalCode,
            ShippingCountry = string.IsNullOrWhiteSpace(profile.Details.ShippingCountry) ? model.ShippingCountry : profile.Details.ShippingCountry,
            ShipToBillingAddress = profile.Details.ShipToBillingAddress,
            LockedFields = new HashSet<string>(profile.LockedFields, StringComparer.Ordinal),
        };
    public IReadOnlyList<string> OverlengthBuildingFields { get; init; } = [];

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
