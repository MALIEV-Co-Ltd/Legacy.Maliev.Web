using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationReviewLayoutBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(320, 700, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(820, 900, "light")]
    [InlineData(1280, 800, "dark")]
    public async Task CalculatingStatusStaysBesideHeadingWithoutClipping(int width, int height, string colorScheme)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = colorScheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), "/instantquotation/3d-printing?culture=th").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        await page.EvaluateAsync("""
            () => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'review';
              workflow.innerHTML = `<div data-workflow-review><section data-workflow-review-content>
                <section class="instant-quote__pricing-summary">
                  <div class="instant-quote__status-heading">
                    <h4>สรุปราคาและระยะเวลาผลิต</h4>
                    <p class="instant-quote__pricing-status" role="status" data-pricing-loading-status>
                      <span class="instant-quote__pricing-spinner" aria-hidden="true"></span>
                      กำลังอัปเดตราคาและระยะเวลาผลิต…</p>
                  </div>
                  <dl aria-busy="true"><div><dt>Total</dt><dd>฿1,000.00</dd></div></dl>
                </section>
                <div class="instant-quote__review-forward-actions" data-review-forward-actions>
                  <section class="instant-quote__preliminary-quotation"><button type="button">PDF</button></section>
                  <button type="button" data-review-continue>กรอกข้อมูลลูกค้า</button>
                </div></section></div>`;
            }
            """);

        var result = await page.EvaluateAsync<bool[]>("""
            () => {
              const heading = document.querySelector('.instant-quote__status-heading');
              const status = heading.querySelector('[data-pricing-loading-status]');
              const title = heading.querySelector('h4');
              const bounds = heading.getBoundingClientRect();
              const label = status.getBoundingClientRect();
              return [
                status.getAttribute('role') === 'status',
                status.querySelector('[aria-hidden="true"]') !== null,
                title.getBoundingClientRect().width > 0 && label.width > 0,
                label.left >= bounds.left - 1 && label.right <= bounds.right + 1,
                document.documentElement.scrollWidth <= innerWidth + 1,
                [...document.querySelectorAll('[data-review-forward-actions] button')].every(button => button.getBoundingClientRect().height >= 44)
              ];
            }
            """);
        Assert.All(result, Assert.True);
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.EvaluateAsync<bool>("document.activeElement !== document.body"));

        var dockFits = await page.EvaluateAsync<bool>("""
            () => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'configured';
              workflow.innerHTML = `<section data-workflow-configuration>
                <div class="instant-quote__configuration-footer">
                  <details class="instant-quote__summary-dock"><summary>
                    <span class="instant-quote__summary-dock-heading">
                      <span class="instant-quote__summary-dock-title">สรุปชิ้นงานและราคา</span>
                      <span class="instant-quote__pricing-status" role="status" data-pricing-loading-status>
                        <span class="instant-quote__pricing-spinner" aria-hidden="true"></span>
                        กำลังอัปเดตราคาและระยะเวลาผลิต…</span>
                    </span><span>1 part</span><strong>฿1,000.00</strong><span aria-hidden="true">⌄</span>
                  </summary><dl aria-busy="true"><div><dt>Total</dt><dd>฿1,000.00</dd></div></dl></details>
                  <div class="instant-quote__configuration-actions"><button type="button">Review</button></div>
                </div></section>`;
              const heading = document.querySelector('.instant-quote__summary-dock-heading');
              const status = heading.querySelector('[data-pricing-loading-status]').getBoundingClientRect();
              const bounds = heading.getBoundingClientRect();
              return status.width > 0 && status.left >= bounds.left - 1
                && status.right <= bounds.right + 1
                && document.documentElement.scrollWidth <= innerWidth + 1;
            }
            """);
        Assert.True(dockFits, "Configuration status must remain within the summary dock.");
    }

    [Theory]
    [InlineData(320, 700)]
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
                document.documentElement.scrollWidth <= innerWidth + 1,
                next.width <= 240,
                Math.abs(document.querySelector('[data-review-forward-actions]').getBoundingClientRect().right - next.right) <= 2
              ];
            }
            """);

        Assert.True(result[0], "Review controls must not clip their selected values.");
        if (width >= 390) Assert.True(result[1], "PDF and Continue must share a row when space permits.");
        Assert.True(result[2], "Review must not force horizontal overflow.");
        Assert.True(result[3], "Continue must remain a compact action, not a full-width strip.");
        Assert.True(result[4], "The forward action must stay right aligned.");

        if (width == 320)
        {
            var longThaiLabelFits = await page.EvaluateAsync<bool>("""
                () => {
                  const button = document.querySelector('[data-review-continue]');
                  button.textContent = 'กรอกข้อมูลสำหรับใบเสนอราคาและดำเนินการต่อ';
                  return document.documentElement.scrollWidth <= innerWidth + 1
                    && button.getBoundingClientRect().width <= innerWidth;
                }
                """);
            Assert.True(longThaiLabelFits, "Long Thai action labels must wrap without horizontal overflow.");
        }
    }
}
