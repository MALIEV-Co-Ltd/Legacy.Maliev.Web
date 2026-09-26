using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class ThreeDimensionalPrintingGalleryBrowserTests(PublicContactBrowserFixture fixture)
{
    [Fact]
    public async Task PartGallery_RejectingOptionalCookiesAllowsExpansionAndLoadsDeferredImages()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, "/services/3d-printing?culture=en").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");

        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var toggle = page.Locator("[data-part-gallery-toggle]");
        var gallery = page.Locator("#printing-part-gallery-extra");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.False(await gallery.IsVisibleAsync());

        await toggle.ClickAsync();

        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await gallery.IsVisibleAsync());
        Assert.False(await toggle.IsVisibleAsync());
        Assert.Equal(12, await gallery.Locator("[data-part-gallery-extra]").CountAsync());
        Assert.Equal(0, await gallery.Locator("img[data-src], img[data-srcset]").CountAsync());
        Assert.Equal(0, await gallery.Locator("[data-part-gallery-extra][aria-hidden='true']").CountAsync());
    }
}
