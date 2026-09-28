using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCustomerLayoutBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(320, 700, "en", ColorScheme.Light)]
    [InlineData(320, 700, "en", ColorScheme.Dark)]
    [InlineData(320, 700, "th", ColorScheme.Light)]
    [InlineData(320, 700, "th", ColorScheme.Dark)]
    [InlineData(375, 667, "en", ColorScheme.Light)]
    [InlineData(375, 667, "en", ColorScheme.Dark)]
    [InlineData(375, 667, "th", ColorScheme.Light)]
    [InlineData(375, 667, "th", ColorScheme.Dark)]
    [InlineData(820, 800, "en", ColorScheme.Light)]
    [InlineData(820, 800, "en", ColorScheme.Dark)]
    [InlineData(820, 800, "th", ColorScheme.Light)]
    [InlineData(820, 800, "th", ColorScheme.Dark)]
    [InlineData(1280, 800, "en", ColorScheme.Light)]
    [InlineData(1280, 800, "en", ColorScheme.Dark)]
    [InlineData(1280, 800, "th", ColorScheme.Light)]
    [InlineData(1280, 800, "th", ColorScheme.Dark)]
    public async Task CustomerLedgerAndFormRemainReadableAtRepresentativeWidths(int width, int height, string culture, ColorScheme colorScheme)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = colorScheme,
            HasTouch = width <= 375,
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        // The synthetic customer step must not be replaced by Blazor's first interactive render.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId !== undefined");

        await page.EvaluateAsync("""
            () => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'customerdetails';
              workflow.innerHTML = `
                <section data-workflow-viewer><canvas></canvas></section>
                <aside class="instant-quote__customer-order" data-workflow-customer-order>
                  <header><h2>Parts</h2></header>
                  <div class="instant-quote__customer-order-scroll"><table>
                    <thead><tr><th>Part</th><th>Quantity</th><th>Subtotal</th></tr></thead>
                    <tbody>${Array.from({length: 12}, (_, index) => `<tr><td><span class="instant-quote__customer-order-part"><img alt="" src="/src/images/3d-canvas-placeholder.svg"><span class="instant-quote__customer-order-identity"><strong>Long customer part name ${index}</strong><small>Material</small></span></span></td><td>12</td><td>฿12,345.67</td></tr>`).join('')}</tbody>
                  </table></div>
                  <section class="instant-quote__pricing-summary"><h3>Order summary</h3><dl><div><dt>Shipping</dt><dd>฿100.00</dd></div><div><dt>VAT</dt><dd>฿864.00</dd></div><div><dt>Total</dt><dd>฿12,964.00</dd></div></dl></section>
                </aside>
                <div data-workflow-customer-details><section data-workflow-customer-details-content>
                  <header class="instant-quote__panel-header"><h3>Customer details</h3></header>
                  <span class="instant-quote__flow-step">Step 3 of 3</span>
                  <form id="instant-quotation-form">${Array.from({length: 16}, (_, index) => `<div><label for="field-${index}">Customer field ${index}</label><input id="field-${index}" value="A sample value"></div>`).join('')}</form>
                  <div class="instant-quote__actions instant-quote__customer-actions"><p role="note">Our team will review your files, then send an official quotation with payment instructions.</p><button type="button">Back</button><button type="submit" form="instant-quotation-form">Submit</button></div>
                </section></div>`;
              document.querySelector('#instant-quotation-form').addEventListener('submit', event => {
                event.preventDefault();
                event.currentTarget.dataset.submitted = 'true';
              });
            }
            """);

        Assert.False(await page.Locator("[data-workflow-viewer]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-workflow-customer-order]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-workflow-customer-details]").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.True(await page.Locator(".instant-quote__customer-actions button[type=submit]").EvaluateAsync<bool>(
            "button => button.form?.id === 'instant-quotation-form' && button.getBoundingClientRect().height >= 44"));
        Assert.True(await page.Locator(".instant-quote__customer-order").EvaluateAsync<bool>(
            "element => { const box = element.getBoundingClientRect(); return box.left >= -1 && box.right <= innerWidth + 1; }"));

        if (width >= 992)
        {
            Assert.True(await page.EvaluateAsync<bool>(
                "() => document.querySelector('.instant-quote__customer-order').getBoundingClientRect().right <= document.querySelector('[data-workflow-customer-details]').getBoundingClientRect().left + 1"));
            Assert.True(await page.Locator("[data-workflow-customer-details-content] form").EvaluateAsync<bool>(
                "element => element.scrollHeight > element.clientHeight"));
            Assert.True(await page.Locator(".instant-quote__customer-actions").EvaluateAsync<bool>(
                "footer => { const pane = footer.closest('[data-workflow-customer-details]').getBoundingClientRect(); const action = footer.getBoundingClientRect(); return action.top >= pane.top && action.bottom <= pane.bottom + 1; }"));
            await page.Locator("#field-15").ScrollIntoViewIfNeededAsync();
            Assert.True(await page.Locator(".instant-quote__customer-actions button[type=submit]").IsVisibleAsync());
            Assert.True(await page.Locator(".instant-quote__customer-order-scroll").EvaluateAsync<bool>(
                "element => element.scrollHeight > element.clientHeight"));
            var lastPart = page.Locator(".instant-quote__customer-order-scroll tbody tr").Last;
            await lastPart.ScrollIntoViewIfNeededAsync();
            Assert.True(await lastPart.EvaluateAsync<bool>("""
                element => {
                  const row = element.getBoundingClientRect();
                  const rail = element.closest('.instant-quote__customer-order-scroll').getBoundingClientRect();
                  return row.top >= rail.top - 1 && row.bottom <= rail.bottom + 1;
                }
                """));
            Assert.True(await page.Locator(".instant-quote__pricing-summary").IsVisibleAsync());
        }
        else
        {
            Assert.True(await page.EvaluateAsync<bool>(
                "() => document.querySelector('.instant-quote__customer-order').getBoundingClientRect().bottom <= document.querySelector('[data-workflow-customer-details]').getBoundingClientRect().top + 1"));
        }
        await page.Locator(".instant-quote__customer-actions button[type=submit]").ClickAsync();
        Assert.Equal("true", await page.Locator("#instant-quotation-form").GetAttributeAsync("data-submitted"));
        if (Environment.GetEnvironmentVariable("MALIEV_WEB_VALIDATION_SCREENSHOTS") == "1"
            && width is 320 or 1280 && culture == "th" && colorScheme == ColorScheme.Light)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(Path.GetTempPath(), $"legacy-web-276-customer-{culture}-{width}.png"),
            });
        }
    }
}
