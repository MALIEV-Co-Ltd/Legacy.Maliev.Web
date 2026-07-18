using Microsoft.AspNetCore.Http.Features;

namespace Legacy.Maliev.Web.Components.Layout;

public sealed record PublicCookieConsentDisplayModel(bool ShowBanner, string? ConsentCookie)
{
    private const string TrackingConsentCookieName = "maliev_tracking_consent";

    public static PublicCookieConsentDisplayModel Create(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var consentFeature = context.Features.Get<ITrackingConsentFeature>();
        var hasRecordedDenial = string.Equals(
            context.Request.Cookies[TrackingConsentCookieName],
            "denied",
            StringComparison.Ordinal);

        return new PublicCookieConsentDisplayModel(
            consentFeature?.CanTrack != true && !hasRecordedDenial,
            consentFeature?.CreateConsentCookie());
    }
}
