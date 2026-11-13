namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Carries localized form choices, customer details and bounded submission feedback for quotation rendering.</summary>
/// <param name="AntiforgeryFieldName">The form field name expected by antiforgery validation.</param>
/// <param name="AntiforgeryRequestToken">The request token rendered into the customer form.</param>
/// <param name="FirstName">The customer's given name for correspondence.</param>
/// <param name="LastName">The customer's family name for correspondence.</param>
/// <param name="Email">The email address used for quotation correspondence.</param>
/// <param name="Telephone">The optional office or landline contact value.</param>
/// <param name="Country">The billing country value submitted by the form.</param>
/// <param name="Company">The company name used for business correspondence.</param>
/// <param name="TaxNumber">The customer's tax identification value.</param>
/// <param name="Description">The customer's additional quotation instructions.</param>
/// <param name="Countries">The canonical country choices with optional localized labels.</param>
/// <param name="SubmissionStatus">The completed, partial or rejected outcome, or null before submission.</param>
/// <param name="RequestReference">The saved request reference, or null when no reference is available.</param>
/// <param name="ProblemCategory">The bounded submission problem category, or null when absent.</param>
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
    /// <summary>Identifies a fully completed customer submission.</summary>
    public const string CompletedStatus = "completed";
    /// <summary>Identifies a saved submission whose remaining processing is incomplete.</summary>
    public const string PartialStatus = "partial";
    /// <summary>Identifies a rejected customer submission.</summary>
    public const string RejectedStatus = "rejected";

    /// <summary>Gets the form field names that require validation feedback.</summary>
    public IReadOnlyList<string> InvalidFields { get; init; } = [];
    // InteractiveServer parameters cross a JSON boundary. IReadOnlySet cannot
    // be materialized by System.Text.Json; a concrete set retains ordinal locks.
    /// <summary>Gets the ordinal field-name set locked by the authenticated customer profile.</summary>
    public HashSet<string> LockedFields { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Gets whether authenticated profile data was unavailable to populate the form.</summary>
    public bool ProfileUnavailable { get; init; }
    /// <summary>Reports whether the authenticated profile prevents editing a named form field.</summary>
    /// <param name="field">The ordinal form field name to check.</param>
    /// <returns>True when the field is in the profile's locked-field set.</returns>
    public bool IsLocked(string field) => LockedFields.Contains(field);

    /// <summary>Copies authenticated customer details and locks into an existing form model while retaining its rendering metadata.</summary>
    /// <param name="model">The existing form model containing antiforgery, country-choice and submission metadata.</param>
    /// <param name="profile">The authenticated profile supplying customer values and field locks.</param>
    /// <returns>A copied model populated from the profile, retaining existing countries when profile country values are blank.</returns>
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
    /// <summary>Gets building-field names whose values exceed the supported input length.</summary>
    public IReadOnlyList<string> OverlengthBuildingFields { get; init; } = [];

    /// <summary>Gets the mobile contact entered or supplied by the authenticated profile for quotation follow-up.</summary>
    public string Mobile { get; init; } = string.Empty;
    /// <summary>Gets the billing premises text retained with the customer's quotation billing address.</summary>
    public string BillingBuilding { get; init; } = string.Empty;
    /// <summary>Gets the principal street-address line used to prepare the customer's billing details.</summary>
    public string BillingStreet1 { get; init; } = string.Empty;
    /// <summary>Gets supplementary billing-address text, such as a unit or local address detail.</summary>
    public string BillingStreet2 { get; init; } = string.Empty;
    /// <summary>Gets the billing locality retained separately from an alternative shipping destination.</summary>
    public string BillingCity { get; init; } = string.Empty;
    /// <summary>Gets the region associated with the customer's billing address in the quotation form.</summary>
    public string BillingProvince { get; init; } = string.Empty;
    /// <summary>Gets the postal-routing value associated with the customer's billing address.</summary>
    public string BillingPostalCode { get; init; } = string.Empty;
    /// <summary>Gets whether customer tax registration refers to head office or a branch; the form defaults to head office.</summary>
    public string TaxBranch { get; init; } = "head-office";
    /// <summary>Gets the branch identifier carried with branch-specific customer tax-registration details.</summary>
    public string TaxBranchCode { get; init; } = string.Empty;
    /// <summary>Gets whether the customer form reuses billing details instead of collecting a separate delivery destination.</summary>
    public bool ShipToBillingAddress { get; init; } = true;
    /// <summary>Gets delivery-premises text for the separate shipping destination when billing details are not reused.</summary>
    public string ShippingBuilding { get; init; } = string.Empty;
    /// <summary>Gets the principal street-address line for delivery to a destination different from the billing address.</summary>
    public string ShippingStreet1 { get; init; } = string.Empty;
    /// <summary>Gets supplementary delivery-address text for the customer's separate shipping destination.</summary>
    public string ShippingStreet2 { get; init; } = string.Empty;
    /// <summary>Gets the delivery locality selected independently when the customer does not ship to the billing address.</summary>
    public string ShippingCity { get; init; } = string.Empty;
    /// <summary>Gets the delivery region retained for the customer's separate shipping destination.</summary>
    public string ShippingProvince { get; init; } = string.Empty;
    /// <summary>Gets the postal-routing value for delivery when a separate shipping address is selected.</summary>
    public string ShippingPostalCode { get; init; } = string.Empty;
    /// <summary>Gets the country used with the separate delivery address rather than the form's billing-country value.</summary>
    public string ShippingCountry { get; init; } = string.Empty;

    /// <summary>Gets a blank form model with Thailand selected and no submission outcome.</summary>
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

/// <summary>Pairs a canonical submitted country value with an optional localized display label.</summary>
/// <param name="Name">The canonical country value retained in submitted data.</param>
/// <param name="LocalizedName">An optional label for the active UI language.</param>
public sealed record InstantQuotationCountryOption(string Name, string? LocalizedName = null)
{
    /// <summary>Gets the localized country label when present, otherwise the canonical submitted name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(LocalizedName) ? Name : LocalizedName;
}
