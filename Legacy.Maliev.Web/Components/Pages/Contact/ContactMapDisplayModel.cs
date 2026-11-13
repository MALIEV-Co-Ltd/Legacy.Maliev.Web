using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Components.Pages.Contact;

/// <summary>
/// Carries an optional embedded map URL and the public external location link.
/// </summary>
/// <param name="EmbedUrl">The configured Google Maps embed URL, or null when the key or place identifier is absent.</param>
/// <param name="OpenUrl">The public Google Maps link used when opening the business location externally.</param>
public sealed record ContactMapDisplayModel(string? EmbedUrl, string OpenUrl)
{
    /// <summary>
    /// Builds the optional embedded location map from configured public mapping options.
    /// </summary>
    /// <param name="options">The mapping options containing the embed key and business place identifier.</param>
    /// <returns>The optional embed URL and always-available public location link.</returns>
    public static ContactMapDisplayModel Create(IOptions<GoogleMapsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        var embedUrl = string.IsNullOrWhiteSpace(value.EmbedApiKey)
            || string.IsNullOrWhiteSpace(value.PlaceId)
                ? null
                : $"https://www.google.com/maps/embed/v1/place?q=place_id:{Uri.EscapeDataString(value.PlaceId.Trim())}&key={Uri.EscapeDataString(value.EmbedApiKey.Trim())}";
        return new(embedUrl, Application.SocialNetworks.GoogleMaps);
    }
}
