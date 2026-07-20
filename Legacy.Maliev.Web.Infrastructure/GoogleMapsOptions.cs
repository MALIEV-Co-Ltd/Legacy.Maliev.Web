namespace Legacy.Maliev.Web.Infrastructure;

public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    public string EmbedApiKey { get; set; } = string.Empty;

    public string PlaceId { get; set; } = string.Empty;
}
