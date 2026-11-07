using System.Text.Json;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Legacy.Maliev.Web.Components.Analytics;

/// <summary>
/// Carries consent state and one-time queued event scripts for the public analytics component.
/// </summary>
/// <param name="ConsentState">The granted or denied tracking consent state reported to the browser.</param>
/// <param name="CanTrack">Whether the request's consent feature currently permits tracking.</param>
/// <param name="QueuedEventScript">Scripts for validated queued events whose removal was persisted.</param>
public sealed record PublicGoogleTagManagerDisplayModel(
    string ConsentState,
    bool CanTrack,
    string QueuedEventScript)
{
    /// <summary>Gets safely serialized server-owned identity only for a consented authenticated request.</summary>
    public string UserIdConfigurationJson { get; init; } = string.Empty;

    /// <summary>
    /// Consumes queued analytics events and resolves tracking consent for this request.
    /// </summary>
    /// <param name="context">The current request containing consent features and temporary event data.</param>
    /// <param name="tempDataDictionaryFactory">The factory used to load and persist consumption of queued events.</param>
    /// <returns>The consent display state and event scripts; scripts are empty if consumption could not be persisted.</returns>
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
