using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Legacy.Maliev.Web.Pages.Shared;

/// <summary>Queues one persisted, non-PII customer journey event across post/redirect/get.</summary>
public static class CustomerJourneyAnalyticsEventQueue
{
    private const string TempDataKey = "Maliev.CustomerJourneyAnalyticsEvent";

    internal static bool TryQueueQuotationDecision(
        ITempDataDictionary tempData,
        int quotationId,
        string decision,
        out Exception? failure)
    {
        try
        {
            if (quotationId <= 0) throw new ArgumentOutOfRangeException(nameof(quotationId));
            if (decision is not ("accepted" or "declined"))
            {
                throw new ArgumentException("The quotation decision is not allowlisted.", nameof(decision));
            }

            ArgumentNullException.ThrowIfNull(tempData);
            tempData[TempDataKey] = JsonSerializer.Serialize(
                new CustomerJourneyAnalyticsEvent(
                    string.Create(CultureInfo.InvariantCulture, $"quotation-{quotationId}"),
                    decision));
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }
    }

    public static bool TryConsume(
        ITempDataDictionary tempData,
        out CustomerJourneyAnalyticsEvent? journeyEvent)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        journeyEvent = null;
        if (!tempData.TryGetValue(TempDataKey, out var value)) return false;

        tempData.Remove(TempDataKey);
        if (value is not string serializedEvent) return false;
        try
        {
            var candidate = JsonSerializer.Deserialize<CustomerJourneyAnalyticsEvent>(serializedEvent);
            if (candidate is null || !candidate.IsAllowed()) return false;
            journeyEvent = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The exact, controlled customer quotation-decision data-layer payload.</summary>
public sealed class CustomerJourneyAnalyticsEvent
{
    [JsonConstructor]
    public CustomerJourneyAnalyticsEvent(string transactionId, string decision)
    {
        TransactionId = transactionId;
        Decision = decision;
    }

    [JsonPropertyName("event")]
    public string Event { get; } = "maliev_quote_decision";

    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; }

    [JsonPropertyName("decision")]
    public string Decision { get; }

    [JsonPropertyName("source")]
    public string Source { get; } = "customer_portal";

    internal bool IsAllowed()
    {
        const string prefix = "quotation-";
        return Event == "maliev_quote_decision"
            && Source == "customer_portal"
            && Decision is "accepted" or "declined"
            && TransactionId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                TransactionId.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var quotationId)
            && quotationId > 0;
    }
}
