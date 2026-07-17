namespace Legacy.Maliev.Web.Components.Layout;

/// <summary>Server-derived display state for the public browser compatibility notice.</summary>
public sealed record PublicBrowserCompatibilityNoticeDisplayModel(bool ShowNotice, string UserAgent)
{
    /// <summary>Creates display-only compatibility state from the current request.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The display state preserving the legacy case-sensitive Trident detection contract.</returns>
    public static PublicBrowserCompatibilityNoticeDisplayModel Create(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var userAgent = context.Request.Headers.UserAgent.ToString();
        return new PublicBrowserCompatibilityNoticeDisplayModel(
            userAgent.Contains("Trident", StringComparison.Ordinal),
            userAgent);
    }
}
