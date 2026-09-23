using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class ScanningProofBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(375, 667)]
    public async Task ProofTabs_StayUsableAtDesktopAndMobileWidths(int width, int height)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await using var page = await context.NewPageAsync();
        var scanningUrl = new Uri(new Uri(fixture.CncQuotationUrl), "/services/3d-scanning?culture=en").ToString();

        await page.GotoAsync(scanningUrl);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        var tabs = page.Locator("#scanning-sample [data-proof-tab]");
        await tabs.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal(3, await tabs.CountAsync());
        Assert.True(await page.Locator("#scanning-cad-panel").IsVisibleAsync());
        Assert.False(await page.Locator("#scanning-report-panel").IsVisibleAsync());

        await tabs.Nth(1).ClickAsync();
        Assert.Equal("true", await tabs.Nth(1).GetAttributeAsync("aria-selected"));
        Assert.True(await page.Locator("#scanning-report-panel").IsVisibleAsync());
        Assert.Equal(3, await page.Locator("#scanning-report-panel img").CountAsync());
        for (var index = 0; index < 3; index++)
        {
            var image = page.Locator("#scanning-report-panel img").Nth(index);
            await image.ScrollIntoViewIfNeededAsync();
            await image.EvaluateAsync("image => image.decode()");
            Assert.True(await image.EvaluateAsync<bool>("image => image.naturalWidth > 0"));
        }

        await tabs.Nth(1).PressAsync("End");
        Assert.Equal("true", await tabs.Nth(2).GetAttributeAsync("aria-selected"));
        Assert.True(await page.Locator("#scanning-color-panel").IsVisibleAsync());
        var colorComparison = page.Locator("#scanning-color-panel [data-scanning-comparison]");
        Assert.Equal("compare", await colorComparison.GetAttributeAsync("data-mode"));
        var slider = colorComparison.Locator("[role='slider']");
        await slider.FocusAsync();
        await slider.PressAsync("Home");
        Assert.Equal("0", await slider.GetAttributeAsync("aria-valuenow"));
        Assert.Contains("Raw scan", await slider.GetAttributeAsync("aria-valuetext"));
        await colorComparison.Locator("[data-comparison-mode='side']").ClickAsync();
        Assert.Equal("side", await colorComparison.GetAttributeAsync("data-mode"));
        Assert.True(await page.Locator("#scanning-sample").EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().width <= innerWidth + 1"));
    }
}
