using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCustomerCheckboxBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(375, 667)]
    [InlineData(1280, 800)]
    public async Task ShippingAddressLabelHasTouchTargetWithoutOversizedCheckbox(int width, int height)
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
              field.className = 'instant-quote__checkbox-field';
              field.innerHTML = '<input id="instant-quote-ship-to-billing" type="checkbox" />'
                + '<label for="instant-quote-ship-to-billing">Shipping address is the same as billing</label>';
              document.querySelector('main').appendChild(field);
            }
            """);

        var checkbox = page.Locator("#instant-quote-ship-to-billing");
        var label = page.Locator("label[for='instant-quote-ship-to-billing']");
        Assert.True(await label.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
        Assert.True(await checkbox.EvaluateAsync<bool>("element => element.getBoundingClientRect().width <= 24"));
        await label.ClickAsync();
        Assert.True(await checkbox.IsCheckedAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }
}
