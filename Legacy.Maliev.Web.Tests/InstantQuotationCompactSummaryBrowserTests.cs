using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCompactSummaryBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(375, 667)]
    [InlineData(820, 800)]
    [InlineData(1280, 800)]
    public async Task CompactDockKeepsTotalAndReviewReachableWhenDetailsToggle(int width, int height)
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
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'configuration';
              workflow.innerHTML = `<section data-workflow-configuration>
                <div style="height: 1100px">Part configuration</div>
                <div class="instant-quote__configuration-footer">
                  <details class="instant-quote__summary-dock" data-workflow-summary-dock>
                    <summary><span class="instant-quote__summary-dock-title">Part and price summary</span>
                      <span>2 parts</span><strong>฿12,964.00</strong><span aria-hidden="true">⌄</span></summary>
                    <dl data-workflow-order-summary><div><dt>Subtotal</dt><dd>฿12,000.00</dd></div>
                      <div><dt>Shipping</dt><dd>฿100.00</dd></div><div><dt>VAT</dt><dd>฿864.00</dd></div>
                      <div><dt>Total</dt><dd>฿12,964.00</dd></div></dl>
                  </details>
                  <p class="instant-quote__price-confidence">Preliminary price</p>
                  <div class="instant-quote__configuration-actions"><button type="button">Review</button></div>
                </div></section>`;
            }
            """);

        var dock = page.Locator("[data-workflow-summary-dock]");
        var summary = dock.Locator("summary");
        var review = page.Locator(".instant-quote__configuration-actions button");
        Assert.False(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await summary.IsVisibleAsync());
        Assert.True(await review.IsVisibleAsync());
        Assert.True(await summary.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
        Assert.True(await summary.EvaluateAsync<bool>("element => getComputedStyle(element).display === 'grid'"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

        await summary.ClickAsync();
        Assert.True(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await dock.Locator("[data-workflow-order-summary]").IsVisibleAsync());
        Assert.True(await review.IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }
}
