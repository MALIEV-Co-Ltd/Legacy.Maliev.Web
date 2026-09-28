using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class ModelViewerWorkerBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task RealBrowserWorker_PreservesShearedGlbBoundsAndReportsMalformedStl()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var quoteUrl = new Uri(new Uri(fixture.CncQuotationUrl), "/instantquotation/3d-printing?culture=en").ToString();
        var response = await page.GotoAsync(quoteUrl);
        Assert.Equal(200, response?.Status);

        JsonElement result = await page.EvaluateAsync<JsonElement>("""
            async () => {
              const worker = new Worker('/src/app/js/model-viewer/model-viewer.worker.js?v=browser-parity');
              const send = data => new Promise((resolve, reject) => {
                const timer = setTimeout(() => reject(new Error('Model worker timed out')), 30000);
                worker.onmessage = event => { clearTimeout(timer); resolve(event.data); };
                worker.onerror = event => { clearTimeout(timer); reject(new Error(event.message || 'Model worker failed')); };
                worker.postMessage(data);
              });
              try {
                const analysis = await send({
                  action: 'analyze', jobId: 'sheared-glb',
                  meshes: [{
                    position: Float32Array.from([0, 0, 0, 0, 1, 0, 1, 1, 0]),
                    matrix: Float32Array.from([1, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]),
                  }],
                });
                const malformed = await send({
                  action: 'parse', jobId: 'malformed-stl', extension: 'stl', buffer: new ArrayBuffer(2),
                });
                return {
                  analysisSucceeded: analysis.success,
                  maxX: analysis.modelInfo?.max.x,
                  maxY: analysis.modelInfo?.max.y,
                  malformedFailed: malformed.success === false && typeof malformed.error === 'string',
                };
              } finally {
                worker.terminate();
              }
            }
            """);

        Assert.True(result.GetProperty("analysisSucceeded").GetBoolean());
        Assert.Equal(2, result.GetProperty("maxX").GetDouble());
        Assert.Equal(1, result.GetProperty("maxY").GetDouble());
        Assert.True(result.GetProperty("malformedFailed").GetBoolean());
    }
}
