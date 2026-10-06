namespace Legacy.Maliev.Web.Application;

public sealed record InstantQuotationExchangeRate(
    string Currency,
    decimal Rate,
    DateTimeOffset ProviderTimestampUtc,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public interface IInstantQuotationExchangeRateClient
{
    Task<InstantQuotationExchangeRate?> GetAsync(string currency, CancellationToken cancellationToken);
}
