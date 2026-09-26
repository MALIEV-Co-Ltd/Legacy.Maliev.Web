using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCustomerCheckboxBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public void ShippingAddressCheckboxFixtureMatchesProductionMarkupAndCss()
    {
        var project = BrowserHostIdentityVerifier.SourceProjectDirectory();
        var form = File.ReadAllText(Path.Combine(project, "Components", "Pages", "InstantQuotation", "InstantQuotationCustomerForm.razor"));
        var css = File.ReadAllText(Path.Combine(project, "wwwroot", "src", "app", "css", "instant-quotation.css"));

        Assert.Contains("<input id=\"instant-quote-ship-to-billing\" type=\"checkbox\"", form, StringComparison.Ordinal);
        Assert.Contains("<label for=\"instant-quote-ship-to-billing\">", form, StringComparison.Ordinal);
        Assert.Matches(@"\.instant-quote__checkbox-field input\[type=""checkbox""\]\s*\{[^}]*width:\s*1\.25rem;[^}]*height:\s*1\.25rem;[^}]*min-height:\s*1\.25rem;", css);
        Assert.Matches(@"\.instant-quote__checkbox-field label\s*\{[^}]*min-height:\s*44px;", css);
    }

    [Theory]
    [InlineData(320, 700, "en")]
    [InlineData(375, 667, "th")]
    [InlineData(820, 800, "en")]
    [InlineData(1280, 800, "th")]
    public async Task ShippingAddressLabelHasTouchTargetWithoutOversizedCheckbox(int width, int height, string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        await page.EvaluateAsync("""
            labelText => {
              const field = document.createElement('div');
              field.className = 'instant-quote__checkbox-field';
              field.innerHTML = '<input id="instant-quote-ship-to-billing" type="checkbox" />'
                + '<label for="instant-quote-ship-to-billing"></label>';
              field.querySelector('label').textContent = labelText;
              document.querySelector('.instant-quote__workflow').appendChild(field);
            }
            """, culture == "th" ? "ที่อยู่จัดส่งตรงกับที่อยู่เรียกเก็บเงิน" : "Shipping address is the same as billing");

        var checkbox = page.Locator("#instant-quote-ship-to-billing");
        var label = page.Locator("label[for='instant-quote-ship-to-billing']");
        Assert.True(await label.EvaluateAsync<bool>("element => element.getBoundingClientRect().width >= 44 && element.getBoundingClientRect().height >= 44"));
        Assert.True(await checkbox.EvaluateAsync<bool>("element => element.getBoundingClientRect().width <= 22 && element.getBoundingClientRect().height <= 22"));
        await label.ClickAsync();
        Assert.True(await checkbox.IsCheckedAsync());
        await checkbox.FocusAsync();
        Assert.True(await checkbox.EvaluateAsync<bool>("element => document.activeElement === element"));
        await page.Keyboard.PressAsync("Space");
        Assert.False(await checkbox.IsCheckedAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }
}
