using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationActionLayoutBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(1351, 1032, "en")]
    [InlineData(1351, 1032, "th")]
    public async Task CompactDesktopActionsStayOnOneLineAtEqualHeight(int width, int height, string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await OpenActionsAsync(context, culture);
        var geometry = await ReadActionGeometryAsync(page);

        Assert.Equal(1, geometry.GetProperty("primaryRows").GetInt32());
        Assert.Equal(1, geometry.GetProperty("secondaryRows").GetInt32());
        Assert.InRange(Math.Abs(geometry.GetProperty("primaryTop").GetDouble()
            - geometry.GetProperty("secondaryTop").GetDouble()), 0, 1);
        Assert.InRange(Math.Abs(geometry.GetProperty("primaryHeight").GetDouble()
            - geometry.GetProperty("secondaryHeight").GetDouble()), 0, 1);
        Assert.False(geometry.GetProperty("overflowsViewport").GetBoolean());
    }

    [Theory]
    [InlineData(320, 700, "th")]
    [InlineData(375, 667, "en")]
    [InlineData(820, 800, "th")]
    public async Task NarrowActionsRemainReachableWithoutHorizontalOverflow(int width, int height, string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await OpenActionsAsync(context, culture);
        var geometry = await ReadActionGeometryAsync(page);

        Assert.True(geometry.GetProperty("primaryHeight").GetDouble() >= 44);
        Assert.True(geometry.GetProperty("secondaryHeight").GetDouble() >= 44);
        Assert.False(geometry.GetProperty("overflowsViewport").GetBoolean());
        await page.Locator("#action-review").ScrollIntoViewIfNeededAsync();
        Assert.True(await page.Locator("#action-review").IsVisibleAsync());
        Assert.True(await page.Locator("#action-consult").IsVisibleAsync());
        await page.Locator("#action-review").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.Locator("#action-consult").EvaluateAsync<bool>(
            "element => document.activeElement === element"));
    }

    private async Task<IPage> OpenActionsAsync(IBrowserContext context, string culture)
    {
        var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }
        // Synthetic layout markup must not race the initial interactive render.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId !== undefined");

        await page.EvaluateAsync("""
            thai => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'configured';
              workflow.innerHTML = `<section data-workflow-viewer></section>
                <section data-workflow-configuration>
                  <div class="instant-quote__configuration-footer">
                    <details class="instant-quote__summary-dock"><summary>Part and price summary</summary></details>
                    <div class="instant-quote__summary-legal">Privacy Policy · Non-Disclosure Agreement</div>
                    <p class="instant-quote__price-confidence instant-quote__price-confidence--review">Preliminary price — manufacturing suitability is confirmed by our engineer.</p>
                    <div class="instant-quote__configuration-actions">
                      <button id="action-review" type="button">${thai ? 'ตรวจสอบรายการ' : 'Review'}</button>
                      <a id="action-consult" class="instant-quote__consultation-link" href="/contact#contact-us">${thai ? 'ปรึกษาวิศวกร' : 'Talk to an engineer'}</a>
                    </div>
                  </div>
                </section>`;
            }
            """, culture == "th");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        return page;
    }

    private static Task<JsonElement> ReadActionGeometryAsync(IPage page) => page.EvaluateAsync<JsonElement>("""
        () => {
          const primary = document.querySelector('#action-review');
          const secondary = document.querySelector('#action-consult');
          const primaryRect = primary.getBoundingClientRect();
          const secondaryRect = secondary.getBoundingClientRect();
          const textRows = element => {
            const range = document.createRange();
            range.selectNodeContents(element);
            return new Set([...range.getClientRects()].map(rect => Math.round(rect.top))).size;
          };
          return {
            primaryTop: primaryRect.top,
            secondaryTop: secondaryRect.top,
            primaryHeight: primaryRect.height,
            secondaryHeight: secondaryRect.height,
            primaryRows: textRows(primary),
            secondaryRows: textRows(secondary),
            overflowsViewport: document.documentElement.scrollWidth > innerWidth + 1
              || primaryRect.left < -1 || secondaryRect.right > innerWidth + 1
          };
        }
        """);
}
