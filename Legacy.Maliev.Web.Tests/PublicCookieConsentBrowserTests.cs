using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class PublicCookieConsentBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(375, 667)]
    public async Task ConsentDialog_StaysOperableAtDesktopAndMobileWidths(int width, int height)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await using var page = await context.NewPageAsync();
        var legalUrl = new Uri(new Uri(fixture.CncQuotationUrl), "/legal?culture=en").ToString();

        await page.GotoAsync(legalUrl);
        var dialog = page.Locator("#cookieConsent");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('privacy-consent-open')"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#cookieConsent').contains(document.activeElement)"));
        Assert.True(await dialog.EvaluateAsync<bool>("element => { const box = element.getBoundingClientRect(); return box.left >= 0 && box.right <= innerWidth && box.top >= 0 && box.bottom <= innerHeight; }"));
        Assert.Equal("rgba(0, 0, 0, 0.72)", await dialog.EvaluateAsync<string>("element => getComputedStyle(element, '::backdrop').backgroundColor"));

        await page.Keyboard.PressAsync("Escape");
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.open"));
        await dialog.Locator("[data-consent-action='reject']").ClickAsync();
        Assert.Equal(0, await dialog.CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('privacy-consent-open')"));
        Assert.Contains("maliev_tracking_consent=denied", (await context.CookiesAsync()).Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }
}
