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

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task EmptyDecorationKeepsRealUploadControlReachableAndHonorsReducedMotion(string culture, bool reducedMotion)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 820, Height = 800 },
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-empty-scene]')?.dataset.cubeReady === 'true' && document.querySelector('[data-empty-scene]')?.dataset.hexReady === 'true'",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        var decoration = page.Locator("[data-empty-scene]");
        Assert.Equal("true", await decoration.GetAttributeAsync("aria-hidden"));
        Assert.Equal("none", await decoration.EvaluateAsync<string>("element => getComputedStyle(element).pointerEvents"));
        Assert.Equal(2, await decoration.Locator("canvas[aria-hidden='true']").CountAsync());
        Assert.Equal(reducedMotion ? "still" : "running", await decoration.GetAttributeAsync("data-motion"));
        var label = page.Locator("label[for='instant-quote-files'].iq-empty-dropzone");
        Assert.Contains(culture == "th" ? "วางไฟล์ 3D ของคุณที่นี่" : "Drop your 3D file here", await label.InnerTextAsync(), StringComparison.Ordinal);
        await page.Locator("#instant-quote-files").FocusAsync();
        Assert.True(await page.Locator("#instant-quote-files").EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.True(await label.EvaluateAsync<bool>("element => { const r = element.getBoundingClientRect(); return element.contains(document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2)); }"));
        await using var emptyScene = await decoration.EvaluateHandleAsync("element => element");
        var chooser = await page.RunAndWaitForFileChooserAsync(
            () => label.ClickAsync(),
            new PageRunAndWaitForFileChooserOptions { Timeout = 30_000 });
        Assert.True(chooser.IsMultiple);
        await chooser.SetFilesAsync(new FilePayload
        {
            Name = "empty-scene-box.stl",
            MimeType = "model/stl",
            Buffer = WallThicknessParityBrowserFixture.Box(5),
        });
        await page.WaitForFunctionAsync(
            """
            scene => {
              const workflow = document.querySelector('.instant-quote__workflow');
              return !scene.isConnected && scene.dataset.motion === 'disposed'
                && workflow && workflow.dataset.workflowState !== 'empty'
                && workflow.querySelector('[data-workflow-upload-item], [data-workflow-part]') !== null;
            }
            """,
            emptyScene, new PageWaitForFunctionOptions { Timeout = 30_000 });
        Assert.True(await emptyScene.EvaluateAsync<bool>("element => !element.isConnected"));
        Assert.Equal("disposed", await emptyScene.EvaluateAsync<string>("element => element.dataset.motion"));
        Assert.Equal(0, await label.CountAsync());
        Assert.True(await page.Locator("[data-workflow-upload-item], [data-workflow-part]").CountAsync() > 0);
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
