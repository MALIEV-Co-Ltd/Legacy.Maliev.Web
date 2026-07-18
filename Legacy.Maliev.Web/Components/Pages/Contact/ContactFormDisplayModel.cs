namespace Legacy.Maliev.Web.Components.Pages.Contact;

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
    bool CountryServiceAvailable,
    IReadOnlyList<ContactCountryOption> Countries,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors)
{
    public IReadOnlyList<string> ErrorsFor(string fieldName) =>
        ValidationErrors.TryGetValue(fieldName, out var errors) ? errors : [];

    public string? FirstErrorFor(string fieldName) => ErrorsFor(fieldName).FirstOrDefault();
}

public sealed record ContactCountryOption(string? Name);
