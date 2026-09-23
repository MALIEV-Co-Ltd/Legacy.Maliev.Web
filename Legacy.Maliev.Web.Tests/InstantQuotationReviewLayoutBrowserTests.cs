using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationReviewLayoutBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(390, 844)]
    [InlineData(1522, 949)]
    public async Task ReviewControlsAndForwardActionsRemainReadable(int width, int height)
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
              workflow.dataset.workflowState = 'review';
              workflow.innerHTML = `<div data-workflow-review><section data-workflow-review-content>
                <article data-workflow-review-part>
                  <div class="instant-quote__review-identity"><h4>part.stl</h4></div>
                  <div class="instant-quote__review-fields">
                    <div><label for="material">Material</label><select id="material"><option>Engineering-grade PETG</option></select></div>
                    <div><label for="preference">Preference</label><select id="preference"><option>Standard</option></select></div>
                    <div><label for="color">Color</label><select id="color"><option>Black</option></select></div>
                    <div><label for="quantity">Quantity</label><input id="quantity" type="number" value="10"></div>
                  </div>
                  <dl class="instant-quote__review-prices"><div><dt>Unit price</dt><dd>฿100.00</dd></div><div><dt>Subtotal</dt><dd>฿1,000.00</dd></div></dl>
                  <details class="instant-quote__review-details"><summary>Part details</summary><p>Engineering review required</p></details>
                </article>
                <section class="instant-quote__pricing-summary">Total ฿1,000.00</section>
                <div class="instant-quote__review-forward-actions" data-review-forward-actions>
                  <section class="instant-quote__preliminary-quotation"><button type="button">PDF</button></section>
                  <button type="button" data-review-continue>Customer details</button>
                </div></section></div>`;
            }
            """);

        var result = await page.EvaluateAsync<bool[]>("""
            () => {
              const fields = document.querySelector('.instant-quote__review-fields');
              const controls = [...fields.querySelectorAll('select, input')];
              const pdf = document.querySelector('.instant-quote__preliminary-quotation button').getBoundingClientRect();
              const next = document.querySelector('[data-review-continue]').getBoundingClientRect();
              return [
                controls.every(control => control.getBoundingClientRect().width >= 100),
                Math.abs((pdf.top + pdf.height / 2) - (next.top + next.height / 2)) <= 2,
                document.documentElement.scrollWidth <= innerWidth + 1
              ];
            }
            """);

        Assert.True(result[0], "Review controls must not clip their selected values.");
        Assert.True(result[1], "PDF and Continue must share a row.");
        Assert.True(result[2], "Review must not force horizontal overflow.");
    }
}
