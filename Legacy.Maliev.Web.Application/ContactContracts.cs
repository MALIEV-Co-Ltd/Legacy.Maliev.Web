namespace Legacy.Maliev.Web.Application;

public sealed record Country(
    int Id,
    string Name,
    string? Continent,
    string? CountryCode,
    string? Iso2,
    string? Iso3,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record ContactSubmission(
    string FirstName,
    string LastName,
    string? Company,
    string Email,
    string? Telephone,
    string Country,
    string MessageContent);

public sealed record ContactSubmissionResult(
    int? ReferenceNumber,
    bool ServiceAvailable,
    bool Authorized);

public interface ICountryClient
{
    Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken);
}

public interface IContactClient
{
    Task<ContactSubmissionResult> SubmitAsync(
        ContactSubmission submission,
        CancellationToken cancellationToken);
}

public interface IServiceAccessTokenProvider
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    void Invalidate(string token);
}

public interface IAntiBotVerifier
{
    Task<bool> VerifyAsync(
        string? token,
        string expectedAction,
        CancellationToken cancellationToken);
}
