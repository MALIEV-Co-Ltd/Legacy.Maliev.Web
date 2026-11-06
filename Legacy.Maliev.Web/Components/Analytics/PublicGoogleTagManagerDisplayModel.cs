using System.Text.Json;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Legacy.Maliev.Web.Components.Analytics;

public sealed record PublicGoogleTagManagerDisplayModel(
    string ConsentState,
    bool CanTrack,
    string QueuedEventScript)
{
    /// <summary>Gets safely serialized server-owned identity only for a consented authenticated request.</summary>
    public string UserIdConfigurationJson { get; init; } = string.Empty;

    public static PublicGoogleTagManagerDisplayModel Create(
        HttpContext context,
        ITempDataDictionaryFactory tempDataDictionaryFactory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tempDataDictionaryFactory);

        var canTrack = context.Features.Get<ITrackingConsentFeature>()?.CanTrack == true;
        var tempData = tempDataDictionaryFactory.GetTempData(context);
        var queuedEventScript = BuildQueuedEventScript(tempData);
        try
        {
            tempData.Save();
        }
        catch
        {
            // Do not emit an event whose removal could not be persisted: that
            // would make a later request replay the same conversion event.
            queuedEventScript = string.Empty;
        }

        return new PublicGoogleTagManagerDisplayModel(
            canTrack ? "granted" : "denied",
            canTrack,
            queuedEventScript)
        {
            UserIdConfigurationJson = GoogleAnalyticsUserId.TryBuildConfiguration(context.User, canTrack, out var userIdJson)
                ? userIdJson : string.Empty,
        };
    }

    private static string BuildQueuedEventScript(ITempDataDictionary tempData)
    {
        var scripts = new List<string>();
        if (LeadAnalyticsEventQueue.TryConsume(tempData, out var leadEvent) && leadEvent is not null)
        {
            if (leadEvent.IsPersistedQuotation)
            {
                scripts.Add($"window.malievAnalytics.emit({JsonSerializer.Serialize(leadEvent)});");
                scripts.Add($"window.malievAnalytics.emit({JsonSerializer.Serialize(leadEvent.BuildGenerateLeadConversionEvent())});");
            }
            else if (leadEvent.Event == "maliev_contact_submitted")
            {
                var diagnostic = new
                {
                    @event = "maliev_contact_submitted",
                    service = "general_contact",
                    submission_status = "persisted",
                };
                scripts.Add($"window.malievAnalytics.emit({JsonSerializer.Serialize(diagnostic)});");
            }
        }

        if (CustomerJourneyAnalyticsEventQueue.TryConsume(tempData, out var journeyEvent)
            && journeyEvent is not null)
        {
            scripts.Add($"window.malievAnalytics.emit({JsonSerializer.Serialize(journeyEvent)});");
        }

        return string.Join('\n', scripts);
    }
}
