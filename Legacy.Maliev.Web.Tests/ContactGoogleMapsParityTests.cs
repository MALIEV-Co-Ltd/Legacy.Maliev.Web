using Legacy.Maliev.Web.Components.Pages.Contact;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactGoogleMapsParityTests
{
    private const string LegacyPlaceId = "ChIJZ9VSFP2F4jARdmoz755rwQU";

    [Fact]
    public void ConfiguredMapUsesExactPlaceEmbedWithoutExposingConfigurationShape()
    {
        var model = ContactMapDisplayModel.Create(
            Options.Create(new GoogleMapsOptions
            {
                EmbedApiKey = "configured-at-runtime",
                PlaceId = LegacyPlaceId,
            }));

        Assert.Equal(
            $"https://www.google.com/maps/embed/v1/place?q=place_id:{LegacyPlaceId}&key=configured-at-runtime",
            model.EmbedUrl);
        Assert.Equal(Legacy.Maliev.Web.Application.SocialNetworks.GoogleMaps, model.OpenUrl);
    }

    [Fact]
    public void MissingMapKeyFailsClosed()
    {
        var model = ContactMapDisplayModel.Create(
            Options.Create(new GoogleMapsOptions { PlaceId = LegacyPlaceId }));

        Assert.Null(model.EmbedUrl);
        Assert.Equal(Legacy.Maliev.Web.Application.SocialNetworks.GoogleMaps, model.OpenUrl);
    }

    [Fact]
    public void ContactSourceRetainsOriginalInquiryStructureAndSafeMapContract()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactPage.razor"));
        var details = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactDetailsContent.razor"));
        var form = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactFormFields.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Contact", "Index.cshtml"));
        var settings = File.ReadAllText(Path.Combine(web, "appsettings.json"));

        Assert.Contains("inquiry-page", route, StringComparison.Ordinal);
        Assert.Contains("contact-method-grid", route, StringComparison.Ordinal);
        Assert.Contains("inquiry-shell", route, StringComparison.Ordinal);
        Assert.Contains("inquiry-map", details, StringComparison.Ordinal);
        Assert.Contains("loading=\"lazy\"", details, StringComparison.Ordinal);
        Assert.Contains("allowfullscreen", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inquiry-form-card", fallback, StringComparison.Ordinal);
        Assert.Contains("asp-route-culture", fallback, StringComparison.Ordinal);
        Assert.Contains("inquiry-customer-card", form, StringComparison.Ordinal);
        Assert.Contains("GoogleMaps", settings, StringComparison.Ordinal);
        Assert.Contains(LegacyPlaceId, settings, StringComparison.Ordinal);
        Assert.Contains("\"EmbedApiKey\": \"\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("maps.google.com", details, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
