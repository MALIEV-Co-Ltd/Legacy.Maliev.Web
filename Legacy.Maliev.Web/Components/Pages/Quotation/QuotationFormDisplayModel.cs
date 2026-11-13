namespace Legacy.Maliev.Web.Components.Pages.Quotation;

/// <summary>
/// Carries a quotation-request draft, lookup availability, and service-finder context for public rendering.
/// </summary>
/// <param name="SubmissionId">The idempotent submission identifier retained by the quotation form.</param>
/// <param name="ServiceContext">The normalized manufacturing service associated with the request.</param>
/// <param name="FirstName">The enquirer's given name retained for correction.</param>
/// <param name="LastName">The enquirer's family name retained for correction.</param>
/// <param name="Email">The email address supplied for quotation correspondence.</param>
/// <param name="Phone">The optional telephone number supplied with the enquiry.</param>
/// <param name="Company">The optional company name supplied with the enquiry.</param>
/// <param name="TaxNumber">The optional company tax identifier supplied with the enquiry.</param>
/// <param name="Country">The selected country name retained by the form.</param>
/// <param name="Message">The manufacturing requirements and enquiry text retained by the form.</param>
/// <param name="RecaptchaToken">The supplied reCAPTCHA response token, or null before verification.</param>
/// <param name="RecaptchaSiteKey">The public site key used to render the reCAPTCHA challenge.</param>
/// <param name="CountryServiceAvailable">Whether the country lookup is available.</param>
/// <param name="IsAuthenticatedCustomer">Whether the visitor is recognized as an authenticated customer.</param>
/// <param name="CustomerProfileAvailable">Whether the authenticated customer's profile could be loaded.</param>
/// <param name="Countries">The country choices offered to the visitor.</param>
/// <param name="ValidationErrors">The server validation messages grouped by quotation field.</param>
/// <param name="FinderFiles">The optional file-context summary carried from the service finder.</param>
/// <param name="FinderService">The optional service selection carried from the service finder.</param>
/// <param name="FinderMaterial">The optional material requirements carried from the service finder.</param>
/// <param name="FinderQuantity">The optional quantity requirements carried from the service finder.</param>
/// <param name="FinderEndUse">The optional intended-use context carried from the service finder.</param>
/// <param name="FinderPerformance">The optional performance requirements carried from the service finder.</param>
/// <param name="FinderEnvironment">The optional operating-environment context carried from the service finder.</param>
/// <param name="FinderRecommendations">The optional recommendation summary carried from the service finder.</param>
/// <param name="FinderPath">The optional selection-path summary carried from the service finder.</param>
public sealed record QuotationFormDisplayModel(
    Guid SubmissionId,
    string ServiceContext,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    string? TaxNumber,
    string Country,
    string Message,
    string? RecaptchaToken,
    string RecaptchaSiteKey,
    bool CountryServiceAvailable,
    bool IsAuthenticatedCustomer,
    bool CustomerProfileAvailable,
    IReadOnlyList<QuotationCountryOption> Countries,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors,
    string? FinderFiles = null,
    string? FinderService = null,
    string? FinderMaterial = null,
    string? FinderQuantity = null,
    string? FinderEndUse = null,
    string? FinderPerformance = null,
    string? FinderEnvironment = null,
    string? FinderRecommendations = null,
    string? FinderPath = null)
{
    /// <summary>
    /// Looks up validation messages for one quotation-request field.
    /// </summary>
    /// <param name="fieldName">The quotation field key used by server validation.</param>
    /// <returns>The field's messages, or an empty list when none are recorded.</returns>
    public IReadOnlyList<string> ErrorsFor(string fieldName) =>
        ValidationErrors.TryGetValue(fieldName, out var errors) ? errors : [];

    /// <summary>
    /// Selects inline validation feedback for one quotation-request field.
    /// </summary>
    /// <param name="fieldName">The quotation field key used by server validation.</param>
    /// <returns>The first field message, or null when the field has no errors.</returns>
    public string? FirstErrorFor(string fieldName) => ErrorsFor(fieldName).FirstOrDefault();
}

/// <summary>
/// Represents a country choice offered by the public quotation-request form.
/// </summary>
/// <param name="Name">The country name displayed and submitted by the form.</param>
public sealed record QuotationCountryOption(string? Name);
