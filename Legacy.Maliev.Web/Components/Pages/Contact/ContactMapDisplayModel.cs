using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Components.Pages.Contact;

public sealed record ContactMapDisplayModel(string? EmbedUrl, string OpenUrl)
{
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
