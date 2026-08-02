using System;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>
/// Server-provided configuration for the Google Maps Embed API.
/// </summary>
public sealed class GoogleMapsOptions
{
    /// <summary>
    /// The configuration section containing the browser Maps key and place.
    /// </summary>
    public const string SectionName = "GoogleMaps";

    /// <summary>
    /// The browser key accepted by the Google Maps Embed API.
    /// </summary>
    public string EmbedApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The public Google place identifier for the MALIEV workshop.
    /// </summary>
    public string PlaceId { get; set; } = "ChIJZ9VSFP2F4jARdmoz755rwQU";

    /// <summary>
    /// Gets a value indicating whether a usable browser key and place are configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(EmbedApiKey)
        && !string.IsNullOrWhiteSpace(PlaceId);

    /// <summary>
    /// Builds the same server-configured Maps Embed URL used by the original site.
    /// </summary>
    public string BuildEmbedUrl()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Google Maps browser configuration is unavailable. Configure GoogleMaps:EmbedApiKey.");
        }

        return "https://www.google.com/maps/embed/v1/place?q=place_id:"
            + Uri.EscapeDataString(PlaceId)
            + "&key="
            + Uri.EscapeDataString(EmbedApiKey);
    }
}
