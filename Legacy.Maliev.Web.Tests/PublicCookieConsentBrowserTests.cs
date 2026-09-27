using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class PublicCookieConsentBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(1280, 800, "en", ColorScheme.Light)]
    [InlineData(1280, 800, "en", ColorScheme.Dark)]
    [InlineData(1280, 800, "th", ColorScheme.Light)]
    [InlineData(1280, 800, "th", ColorScheme.Dark)]
    [InlineData(375, 667, "en", ColorScheme.Light)]
    [InlineData(375, 667, "en", ColorScheme.Dark)]
    [InlineData(375, 667, "th", ColorScheme.Light)]
    [InlineData(375, 667, "th", ColorScheme.Dark)]
    public async Task ConsentDialog_StaysOperableAtDesktopAndMobileWidths(int width, int height, string culture, ColorScheme colorScheme)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = colorScheme,
        });
        await using var page = await context.NewPageAsync();
        var quotationUrl = new Uri(new Uri(fixture.CncQuotationUrl), $"/instantquotation/3d-printing?culture={culture}").ToString();

        await page.GotoAsync(quotationUrl);
        var dialog = page.Locator("#cookieConsent");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.open && element.matches(':modal')"));
        Assert.Equal(culture == "en" ? "Your privacy choices" : "ตัวเลือกความเป็นส่วนตัวของคุณ", await dialog.Locator("h2").InnerTextAsync());
        Assert.Contains(culture == "en" ? "Essential cookies" : "คุกกี้ที่จำเป็น", await dialog.Locator("p").InnerTextAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('privacy-consent-open')"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#cookieConsent').contains(document.activeElement)"));
        Assert.True(await dialog.EvaluateAsync<bool>("element => { const box = element.getBoundingClientRect(); return box.left >= 0 && box.right <= innerWidth && box.top >= 0 && box.bottom <= innerHeight; }"));
        Assert.True(await dialog.EvaluateAsync<bool>("element => { const box = element.getBoundingClientRect(); return Math.abs(box.left + box.width / 2 - innerWidth / 2) < 2 && Math.abs(box.top + box.height / 2 - innerHeight / 2) < 2; }"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.Equal("rgba(0, 0, 0, 0.72)", await dialog.EvaluateAsync<string>("element => getComputedStyle(element, '::backdrop').backgroundColor"));
        Assert.Equal("0px", await dialog.Locator("[data-consent-action='reject']").EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth"));
        Assert.True(await dialog.Locator("a[href='/legal/privacypolicy']").IsVisibleAsync());
        Assert.True(await dialog.Locator("[data-consent-action='reject']").IsVisibleAsync());
        Assert.True(await dialog.Locator("[data-consent-action='accept']").IsVisibleAsync());
        Assert.False(await page.EvaluateAsync<bool>("""() => !!document.querySelector('script[src*="googletagmanager.com/gtm.js"]')"""));

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(Path.GetTempPath(), $"legacy-web-284-consent-{culture}-{width}-{colorScheme}.png"),
        });

        await page.Keyboard.PressAsync("Escape");
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.matches(':modal')"));
        await page.Mouse.ClickAsync(5, 5);
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.matches(':modal')"));
        var focusStates = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            await page.Keyboard.PressAsync("Tab");
            focusStates.Add(await dialog.EvaluateAsync<string>("element => document.activeElement === document.body ? 'body' : element.contains(document.activeElement) ? 'dialog' : 'background'"));
        }
        // Chromium briefly focuses body at the native dialog's wrap boundary; the next Tab
        // must return inside the modal without reaching a background control.
        Assert.Equal(["dialog", "dialog", "dialog", "body", "dialog"], focusStates);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const backgroundLink = [...document.querySelectorAll('a')].find(link => !link.closest('#cookieConsent'));
                if (!backgroundLink) return false;
                backgroundLink.focus();
                return document.activeElement !== backgroundLink && document.querySelector('#cookieConsent').matches(':modal');
            }
            """));

        await dialog.Locator("[data-consent-action='reject']").ClickAsync();
        Assert.Equal(0, await dialog.CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('privacy-consent-open')"));
        Assert.Contains("maliev_tracking_consent=denied", (await context.CookiesAsync()).Select(cookie => $"{cookie.Name}={cookie.Value}"));
        await page.ReloadAsync();
        Assert.Equal(0, await page.Locator("#cookieConsent").CountAsync());
        Assert.True(await page.Locator(".instant-quote__workflow").CountAsync() > 0);
    }

    [Fact]
    public async Task AcceptingOptionalCookies_PersistsChoiceAndDismissesDialog()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var legalUrl = new Uri(new Uri(fixture.CncQuotationUrl), "/legal?culture=en").ToString();

        await page.GotoAsync(legalUrl);
        var dialog = page.Locator("#cookieConsent");
        await dialog.Locator("[data-consent-action='accept']").ClickAsync();
        Assert.Equal(0, await dialog.CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('privacy-consent-open')"));
        await page.ReloadAsync();
        Assert.Equal(0, await dialog.CountAsync());
    }
}
