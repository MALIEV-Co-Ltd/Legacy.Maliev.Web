using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class GoogleMapsConfigurationTests
{
    [Fact]
    public void BuildEmbedUrl_UsesServerConfiguredEmbedKeyAndPlace()
    {
        var options = new GoogleMapsOptions
        {
            EmbedApiKey = "browser-key",
            PlaceId = "place-id"
        };

        Assert.Equal(
            "https://www.google.com/maps/embed/v1/place?q=place_id:place-id&key=browser-key",
            options.BuildEmbedUrl());
    }

    [Fact]
    public void MissingKeyFailsClosed()
    {
        var options = new GoogleMapsOptions { PlaceId = "place-id" };

        Assert.False(options.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => options.BuildEmbedUrl());
    }

    [Fact]
    public void ContactDetailsDoesNotEmbedTheLegacyKeylessMapsUrl()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? sourcePath = null;
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "Legacy.Maliev.Web",
                "Components",
                "Pages",
                "Contact",
                "ContactDetailsContent.razor");
            if (File.Exists(candidate))
            {
                sourcePath = candidate;
                break;
            }

            current = current.Parent;
        }

        Assert.NotNull(sourcePath);
        var source = File.ReadAllText(sourcePath!);

        Assert.DoesNotContain("maps.google.com/maps?q=", source, StringComparison.Ordinal);
        Assert.Contains("BuildEmbedUrl", source, StringComparison.Ordinal);
        Assert.Contains("GoogleMapsOptions", source, StringComparison.Ordinal);
    }
}
