namespace Legacy.Maliev.Web.Components.Pages.Quotation;

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
    IReadOnlyList<QuotationCountryOption> Countries,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors)
{
    public IReadOnlyList<string> ErrorsFor(string fieldName) =>
        ValidationErrors.TryGetValue(fieldName, out var errors) ? errors : [];

    public string? FirstErrorFor(string fieldName) => ErrorsFor(fieldName).FirstOrDefault();
}

public sealed record QuotationCountryOption(string? Name);
