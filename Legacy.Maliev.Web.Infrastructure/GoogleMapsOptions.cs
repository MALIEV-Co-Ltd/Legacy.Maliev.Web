using System;

namespace Legacy.Maliev.Web.Infrastructure;

public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    public string EmbedApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The public Google place identifier for the MALIEV workshop.
    /// </summary>
    public string PlaceId { get; set; } = "ChIJZ9VSFP2F4jARdmoz755rwQU";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(EmbedApiKey)
        && !string.IsNullOrWhiteSpace(PlaceId);

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
