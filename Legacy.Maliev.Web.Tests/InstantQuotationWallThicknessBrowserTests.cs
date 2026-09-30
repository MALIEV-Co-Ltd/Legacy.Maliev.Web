using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationWallThicknessBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task OppositeFaceWorkerPatchRemainsLocalAndOpenSheetHasNoInventedMeasurement()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var quoteUrl = new Uri(new Uri(fixture.CncQuotationUrl), "/instantquotation/3d-printing?culture=en").ToString();
        var response = await page.GotoAsync(quoteUrl);
        Assert.Equal(200, response?.Status);

        var result = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const run = triangles => new Promise((resolve, reject) => {
                const worker = new Worker('/src/app/js/instant-quotation/wall-thickness-runner.worker.js?v=2');
                const timer = setTimeout(() => { worker.terminate(); reject(new Error('Worker timed out')); }, 10000);
                worker.onmessage = event => {
                  clearTimeout(timer);
                  worker.terminate();
                  if (event.data.error) reject(new Error(event.data.error));
                  else resolve(event.data.evidence);
                };
                worker.onerror = error => { clearTimeout(timer); worker.terminate(); reject(error); };
                worker.postMessage({ meshes: [{
                  position: Float32Array.from(triangles),
                  index: null,
                  matrix: Float32Array.from([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])
                }] });
              });
              const target = [[0, 0, 0], [2, 0, 0], [0, 0, 2]];
              const source = [[-1, 0.5, -1], [0, 0.5, 2], [2, 0.5, 0]];
              const patchEvidence = await run([...target, ...source].flat());
              const sheetEvidence = await run([
                0, 0, 0, 20, 0, 0, 20, 20, 0,
                0, 0, 0, 20, 20, 0, 0, 20, 0
              ]);
              const patches = Array.from(patchEvidence.oppositeSurfacePatches);
              return {
                patchCount: patches.length / 13,
                patchesLocal: patches.length > 0 && patches.length % 13 === 0
                  && patches.every((_, i) => i % 13 >= 9 || (() => {
                    if ((i % 13) % 3 !== 0) return true;
                    const x = patches[i], z = patches[i + 2];
                    return x >= -1e-5 && z >= -1e-5 && x + z <= 2 + 1e-5;
                  })()),
                patchThin: patches.some((value, i) => i % 13 >= 9 && i % 13 < 12 && value < 0.8),
                sheetState: sheetEvidence.summary.state,
                sheetMeasured: sheetEvidence.summary.measuredSampleCount,
                sheetUnmeasured: Array.from(sheetEvidence.fields[0]).every(Number.isNaN)
              };
            }
            """);

        Assert.True(result.GetProperty("patchCount").GetDouble() > 0);
        Assert.True(result.GetProperty("patchesLocal").GetBoolean());
        Assert.True(result.GetProperty("patchThin").GetBoolean());
        Assert.Equal("unavailable", result.GetProperty("sheetState").GetString());
        Assert.Equal(0, result.GetProperty("sheetMeasured").GetInt32());
        Assert.True(result.GetProperty("sheetUnmeasured").GetBoolean());
    }

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
