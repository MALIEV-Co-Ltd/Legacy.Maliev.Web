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
            queuedEventScript);
    }

    private static string BuildQueuedEventScript(ITempDataDictionary tempData)
    {
        if (!LeadAnalyticsEventQueue.TryConsume(tempData, out var leadEvent) || leadEvent is null)
        {
            return string.Empty;
        }

        var script = $"window.malievAnalytics.emit({JsonSerializer.Serialize(leadEvent)});";
        if (!leadEvent.FileUploadCompleted)
        {
            return script;
        }

        var fileUploadEvent = JsonSerializer.Serialize(new
        {
            @event = "file_upload_complete",
            service = leadEvent.Service,
            transaction_id = leadEvent.TransactionId
        });
        return $"{script}\nwindow.malievAnalytics.emit({fileUploadEvent});";
    }
}
