using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationExchangeRateClient(
    IHttpClientFactory clients,
    IServiceAccessTokenProvider tokens,
    TimeProvider clock,
    ILogger<InstantQuotationExchangeRateClient> logger) : IInstantQuotationExchangeRateClient
{
    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNameCaseInsensitive = false };

    public async Task<InstantQuotationExchangeRate?> GetAsync(string currency, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"currencies/exchangerates?baseCurrency=THB&targetCurrency={Uri.EscapeDataString(currency)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await clients.CreateClient("catalog").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    tokens.Invalidate(token);
                }
                logger.LogWarning("Catalog exchange-rate request failed with status {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var wire = await response.Content.ReadFromJsonAsync<CatalogExchangeRateResponse>(WireOptions, cancellationToken);
            if (wire is null || wire.Base != "THB" || wire.Rates is null
                || !wire.Rates.TryGetValue(currency, out var rawRate)
                || !decimal.TryParse(rawRate, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
                || rate <= 0)
            {
                return null;
            }

            var retrieved = clock.GetUtcNow();
            var provider = wire.Date == default
                ? retrieved
                : new DateTimeOffset(DateTime.SpecifyKind(wire.Date, DateTimeKind.Utc));
            return new(currency, rate, provider, retrieved, retrieved.AddMinutes(30));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TimeoutRejectedException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Catalog exchange-rate transport or response was unavailable ({FailureType}).", exception.GetType().Name);
            return null;
        }
    }

    private sealed record CatalogExchangeRateResponse(string Base, DateTime Date, Dictionary<string, string>? Rates);
}
