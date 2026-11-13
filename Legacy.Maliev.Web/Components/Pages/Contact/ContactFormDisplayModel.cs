namespace Legacy.Maliev.Web.Components.Pages.Contact;

/// <summary>
/// Carries submitted contact fields, lookup choices, and validation state for the public enquiry form.
/// </summary>
/// <param name="FirstName">The enquirer's given name redisplayed after submission.</param>
/// <param name="LastName">The enquirer's family name redisplayed after submission.</param>
/// <param name="Email">The email address supplied for replying to the enquiry.</param>
/// <param name="Phone">The optional telephone number supplied by the enquirer.</param>
/// <param name="Company">The optional company name supplied with the enquiry.</param>
/// <param name="Country">The selected country name retained by the form.</param>
/// <param name="Message">The enquiry text retained for correction after validation.</param>
/// <param name="RecaptchaToken">The supplied reCAPTCHA response token, or null before verification.</param>
/// <param name="RecaptchaSiteKey">The public site key used to render the reCAPTCHA challenge.</param>
/// <param name="IsAuthenticatedCustomer">Whether the visitor is recognized as an authenticated customer.</param>
/// <param name="CountryServiceAvailable">Whether the country lookup supplied an available response.</param>
/// <param name="Countries">The country choices available to the contact form.</param>
/// <param name="ValidationErrors">The server validation messages grouped by contact field.</param>
public sealed record ContactFormDisplayModel(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    string Country,
    string Message,
    string? RecaptchaToken,
    string RecaptchaSiteKey,
    bool IsAuthenticatedCustomer,
    bool CountryServiceAvailable,
    IReadOnlyList<ContactCountryOption> Countries,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors)
{
    /// <summary>
    /// Looks up validation messages for one contact-form field.
    /// </summary>
    /// <param name="fieldName">The contact field key used by server validation.</param>
    /// <returns>The field's messages, or an empty list when no errors are recorded.</returns>
    public IReadOnlyList<string> ErrorsFor(string fieldName) =>
        ValidationErrors.TryGetValue(fieldName, out var errors) ? errors : [];

    /// <summary>
    /// Selects inline validation feedback for one contact-form field.
    /// </summary>
    /// <param name="fieldName">The contact field key used by server validation.</param>
    /// <returns>The first field message, or null when the field has no errors.</returns>
    public string? FirstErrorFor(string fieldName) => ErrorsFor(fieldName).FirstOrDefault();
}

/// <summary>
/// Represents a country choice offered by the public contact form.
/// </summary>
/// <param name="Name">The country name displayed and submitted by the form.</param>
public sealed record ContactCountryOption(string? Name);
