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

    /// <summary>Removes a pending quotation-decision event and returns it only when its serialized payload is allowlisted.</summary>
    /// <param name="tempData">The redirect-scoped temporary data containing the pending analytics event.</param>
    /// <param name="journeyEvent">The validated event when consumption succeeds; otherwise, null.</param>
    /// <returns>True when a valid event was consumed; otherwise, false.</returns>
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
    /// <summary>Initializes the controlled quotation-decision payload from its transaction and decision values.</summary>
    /// <param name="transactionId">The controlled quotation-prefixed transaction identifier for the persisted decision.</param>
    /// <param name="decision">The accepted or declined decision associated with the quotation.</param>
    [JsonConstructor]
    public CustomerJourneyAnalyticsEvent(string transactionId, string decision)
    {
        TransactionId = transactionId;
        Decision = decision;
    }

    /// <summary>The fixed data-layer event name for a persisted customer quotation decision.</summary>
    [JsonPropertyName("event")]
    public string Event { get; } = "maliev_quote_decision";

    /// <summary>The controlled quotation-prefixed transaction identifier used to correlate the persisted decision.</summary>
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; }

    /// <summary>The accepted or declined decision value validated before the event is consumed.</summary>
    [JsonPropertyName("decision")]
    public string Decision { get; }

    /// <summary>The fixed customer-portal origin marker for the quotation-decision event.</summary>
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
