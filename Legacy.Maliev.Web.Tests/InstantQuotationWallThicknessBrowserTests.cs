using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationWallThicknessBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(375, 667)]
    public async Task NormalRayWorkerRunsOnQuoteOriginAtDesktopAndMobileWidths(int width, int height)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await using var page = await context.NewPageAsync();
        var quoteUrl = new Uri(new Uri(fixture.CncQuotationUrl), "/instantquotation/3d-printing?culture=en").ToString();
        var response = await page.GotoAsync(quoteUrl);
        Assert.Equal(200, response?.Status);

        var result = await page.EvaluateAsync<JsonElement>("""
            async () => {
            const worker = new Worker('/src/app/js/instant-quotation/wall-thickness-runner.worker.js?v=2');
            const box = [
              0, 0, 0, 0, 10, 0, 20, 10, 0, 0, 0, 0, 20, 10, 0, 20, 0, 0,
              0, 0, 0.5, 20, 0, 0.5, 20, 10, 0.5, 0, 0, 0.5, 20, 10, 0.5, 0, 10, 0.5,
              0, 0, 0, 20, 0, 0, 20, 0, 0.5, 0, 0, 0, 20, 0, 0.5, 0, 0, 0.5,
              20, 0, 0, 20, 10, 0, 20, 10, 0.5, 20, 0, 0, 20, 10, 0.5, 20, 0, 0.5,
              20, 10, 0, 0, 10, 0, 0, 10, 0.5, 20, 10, 0, 0, 10, 0.5, 20, 10, 0.5,
              0, 10, 0, 0, 0, 0, 0, 0, 0.5, 0, 10, 0, 0, 0, 0.5, 0, 10, 0.5,
            ];
            try {
              return await Promise.race([
                new Promise((resolve, reject) => {
                  worker.onmessage = event => event.data.error
                    ? reject(new Error(event.data.error))
                    : resolve({
                        state: event.data.evidence.summary.state,
                        minimum: event.data.evidence.summary.minMm,
                        fieldCount: event.data.evidence.fields.length,
                      });
                  worker.onerror = reject;
                  worker.postMessage({ meshes: [{
                    position: Float32Array.from(box),
                    index: null,
                    matrix: Float32Array.from([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]),
                  }] });
                }),
                new Promise((_, reject) => setTimeout(() => reject(new Error('Worker timed out')), 10000)),
              ]);
            } finally {
              worker.terminate();
            }
            }
            """);

        Assert.Equal("complete", result.GetProperty("state").GetString());
        Assert.InRange(result.GetProperty("minimum").GetDouble(), 0.4, 0.6);
        Assert.Equal(1, result.GetProperty("fieldCount").GetInt32());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }
}
