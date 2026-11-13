using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Converts selected THB response fields for display using a protected, session-bound exchange-rate snapshot.</summary>
/// <param name="rates">The typed client supplying expiring exchange-rate observations.</param>
/// <param name="cache">The distributed cache storing protected display-rate snapshots.</param>
/// <param name="protection">The data-protection provider binding cached rate snapshots to this application.</param>
/// <param name="clock">The clock used to reject future or expired observations.</param>
/// <param name="logger">The logger for bounded FX failure categories.</param>
public sealed class InstantQuotationFxHandler(
    IInstantQuotationExchangeRateClient rates,
    IDistributedCache cache,
    IDataProtectionProvider protection,
    TimeProvider clock,
    ILogger<InstantQuotationFxHandler> logger)
{
    private readonly IDataProtector protector = protection.CreateProtector("Legacy.Maliev.Web.AdditiveFxSnapshot.v1");
    private static readonly HashSet<string> MonetaryFields = new(StringComparer.Ordinal)
    {
        "unitPrice", "subtotal", "technicalFilamentMinimumPrice", "technicalFilamentMinimumAdjustment",
        "printing", "itemsSubtotal", "commercialSubtotal", "minimumOrderPrice", "minimumOrderSurcharge",
        "shipping", "vat", "priceBeforeVat", "finalOrderPrice",
    };

    /// <summary>Leaves failed or THB responses unchanged and converts eligible monetary display fields using a valid rate.</summary>
    /// <param name="context">The request carrying the protected session identity and cancellation token.</param>
    /// <param name="payload">The calculator response whose successful monetary fields may be converted.</param>
    /// <param name="requestedCurrency">The requested three-letter display currency; blank selects THB.</param>
    /// <returns>The original or converted payload with HTTP 200, or a bounded unavailable payload with HTTP 503.</returns>
    public async Task<InstantQuotationFxHandlerResult> ConvertAsync(HttpContext context, object payload, string? requestedCurrency)
    {
        var currency = string.IsNullOrWhiteSpace(requestedCurrency) ? "THB" : requestedCurrency.Trim().ToUpperInvariant();
        var baseline = JsonSerializer.SerializeToElement(payload);
        if (!baseline.GetProperty("success").GetBoolean() || currency == "THB")
        {
            return new(payload, StatusCodes.Status200OK);
        }
        if (currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z'))
        {
            return Unavailable();
        }

        try
        {
            var identity = context.User.FindFirstValue(InstantQuotationSessionIdentityClaim.Type);
            if (!InstantQuotationSessionIdentityCookie.IsValidIdentity(identity))
            {
                return Unavailable();
            }
            var key = $"legacy:web:additive-fx:{identity}:{currency}";
            var snapshot = ReadSnapshot(await cache.GetStringAsync(key, context.RequestAborted), identity!, currency);
            if (snapshot is null)
            {
                var observation = await rates.GetAsync(currency, context.RequestAborted);
                if (observation is null)
                {
                    return Unavailable();
                }
                snapshot = new(identity!, observation);
                await cache.SetStringAsync(key, protector.Protect(JsonSerializer.Serialize(snapshot)),
                    new DistributedCacheEntryOptions { AbsoluteExpiration = observation.ExpiresAtUtc }, context.RequestAborted);
            }

            var converted = ConvertFields(baseline, snapshot.Observation.Rate);
            converted["currency"] = currency;
            if (baseline.TryGetProperty("tiers", out var tiers))
            {
                converted["tiers"] = tiers.EnumerateArray().Select(tier => ConvertFields(tier, snapshot.Observation.Rate)).ToArray();
            }
            return new(converted, StatusCodes.Status200OK);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Additive FX display was unavailable ({FailureType}).", exception.GetType().Name);
            return Unavailable();
        }
    }

    private CachedSnapshot? ReadSnapshot(string? serialized, string identity, string currency)
    {
        if (string.IsNullOrEmpty(serialized))
        {
            return null;
        }
        try
        {
            var snapshot = JsonSerializer.Deserialize<CachedSnapshot>(protector.Unprotect(serialized));
            var observation = snapshot?.Observation;
            var now = clock.GetUtcNow();
            return snapshot?.Identity == identity && observation is not null && observation.Currency == currency
                && observation.Rate > 0 && observation.RetrievedAtUtc <= now
                && observation.ExpiresAtUtc == observation.RetrievedAtUtc.AddMinutes(30)
                && now < observation.ExpiresAtUtc ? snapshot : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static Dictionary<string, object> ConvertFields(JsonElement source, decimal rate)
    {
        var fields = source.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone(), StringComparer.Ordinal);
        foreach (var name in MonetaryFields)
        {
            if (source.TryGetProperty(name, out var amount))
            {
                fields[name] = Math.Round(amount.GetDecimal() * rate, 2);
            }
        }
        return fields;
    }

    private static InstantQuotationFxHandlerResult Unavailable() => new(
        new { success = false, code = "pricing_unavailable" }, StatusCodes.Status503ServiceUnavailable);

    private sealed record CachedSnapshot(string Identity, InstantQuotationExchangeRate Observation);
}

/// <summary>Carries the compatibility response body and status selected by display currency conversion.</summary>
/// <param name="Payload">The original, converted or bounded failure response object.</param>
/// <param name="StatusCode">The HTTP status code to apply to that response.</param>
public sealed record InstantQuotationFxHandlerResult(object Payload, int StatusCode);
