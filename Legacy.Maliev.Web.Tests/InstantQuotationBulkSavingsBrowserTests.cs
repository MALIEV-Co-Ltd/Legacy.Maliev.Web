using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationBulkSavingsBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(375, 667)]
    [InlineData(820, 800)]
    [InlineData(1280, 800)]
    public async Task QuantityHeadingFitsSavingsAndRetainsKeyboardAccess(int width, int height)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), "/instantquotation/3d-printing?culture=en").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        await page.EvaluateAsync("""
            () => {
              const field = document.createElement('div');
              field.className = 'instant-quote__quantity-field';
              field.innerHTML = '<div class="instant-quote__quantity-heading"><label for="bulk-test-quantity">Quantity</label>'
                + '<span data-workflow-bulk-savings>Saved ฿10,000.00</span></div>'
                + '<input id="bulk-test-quantity" type="number" value="10000" />';
              document.querySelector('.instant-quote__workflow').appendChild(field);
            }
            """);

        var heading = page.Locator(".instant-quote__quantity-heading");
        var badge = page.Locator("[data-workflow-bulk-savings]");
        Assert.True(await badge.IsVisibleAsync());
        Assert.True(await heading.EvaluateAsync<bool>("element => getComputedStyle(element).display === 'flex'"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

        await page.Keyboard.PressAsync("Tab");
        await page.Locator("#bulk-test-quantity").FocusAsync();
        Assert.True(await page.Locator("#bulk-test-quantity").EvaluateAsync<bool>("element => document.activeElement === element"));
        await badge.EvaluateAsync("element => element.hidden = true");
        Assert.False(await badge.IsVisibleAsync());
    }
}
