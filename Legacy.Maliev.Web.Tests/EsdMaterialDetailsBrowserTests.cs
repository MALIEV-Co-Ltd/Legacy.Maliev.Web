using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class EsdMaterialDetailsBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", "PA612-ESD", "Fiberon", "1.10 g/cm³", "not a PA612-ESD material sample")]
    [InlineData("th", "PA612-ESD", "Fiberon", "1.10 g/cm³", "ไม่ใช่ตัวอย่างวัสดุ PA612-ESD")]
    [InlineData("en", "ABS-ESD", "eSUN", "0.97 g/cm³", "not an ABS-ESD material sample")]
    [InlineData("th", "ABS-ESD", "eSUN", "0.97 g/cm³", "ไม่ใช่ตัวอย่างวัสดุ ABS-ESD")]
    public async Task CurrentEsdOfferLoadsBilingualDetailsAndIllustrativeImageWithoutOptionalConsent(
        string culture, string material, string manufacturer, string density, string disclaimer)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = culture == "th" ? 390 : 1360, Height = 912 },
            ColorScheme = culture == "th" ? ColorScheme.Dark : ColorScheme.Light,
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        var toggle = page.Locator($"[data-material-key='{material}'] [data-material-toggle]");
        await toggle.ClickAsync();
        var details = page.Locator($"[data-material-detail-for='{material}']");
        await details.Locator(".service-material-detail-card__description").WaitForAsync();
        Assert.Contains(manufacturer, await details.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Contains(density, await details.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Contains(disclaimer, await details.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await details.Locator(".service-material-detail-status-error").CountAsync());
        var image = details.Locator("img");
        await image.ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("image => image.complete && image.naturalWidth > 0", await image.ElementHandleAsync());
        var evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "esd-material-browser");
        Directory.CreateDirectory(evidenceDirectory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(evidenceDirectory, $"{material}-{culture}.png"), FullPage = true });
        await details.Locator(".service-material-detail-close").ClickAsync();
        Assert.False(await details.IsVisibleAsync());
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await toggle.EvaluateAsync<bool>("element => element === document.activeElement"));
    }
}
