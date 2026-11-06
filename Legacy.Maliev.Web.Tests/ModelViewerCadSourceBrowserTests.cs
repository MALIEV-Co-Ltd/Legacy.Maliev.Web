using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class ModelViewerCadSourceBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task LargeTrustedCadOverlay_ClassifiesInWorkerWithoutBlockingFrames()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync();
        await using IPage page = await context.NewPageAsync();
        IResponse? response = await page.GotoAsync(fixture.CncQuotationUrl);
        Assert.Equal(200, response?.Status);
        await page.WaitForFunctionAsync("() => window.viewer && typeof window.viewer.GetCncOverlayState === 'function'");
        JsonElement result = await page.EvaluateAsync<JsonElement>(@"async () => {
            const triangleCount = 120001;
            const positions = new Float32Array(triangleCount * 9);
            for (let triangle = 0; triangle < triangleCount; triangle += 1) {
                const x = triangle % 400;
                const y = Math.floor(triangle / 400);
                positions.set([x, y, 0, x + 0.8, y, 0, x, y + 0.8, 0], triangle * 9);
            }
            const geometry = new THREE.BufferGeometry();
            geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
            // The current viewer requires face membership before admitting a trusted CAD overlay.
            geometry.userData.cadFaceRanges = [{ first: 0, last: triangleCount - 1 }];
            const object = new THREE.Group();
            object.add(new THREE.Mesh(geometry, new THREE.MeshBasicMaterial()));
            viewer.ShowObject(object, {
                min: { x: 0, y: 0, z: 0 }, size: { x: 400, y: 301, z: 0.01 }, bodyCount: 1,
                sourceFormat: 'step', isCadSource: true
            });
            const setup = { id: 'setup-1', number: 1, direction: { x: 0, y: 0, z: 1 },
                reachableClusterIds: ['cluster-0'] };
            viewer.SetCncOverlayPlan(
                { stockShape: 'block', stockSizeMm: { x: 402, y: 303, z: 1 } },
                [setup], [],
                { partId: 'large-trusted-cad', topologyDegraded: false, surfaceClusters: [{
                    id: 'cluster-0', type: 'planar', centroid: { x: 200, y: 150, z: 0 },
                    normal: { x: 0, y: 0, z: 1 },
                    triangleIndexes: Array.from({ length: triangleCount }, (_, index) => index)
                }], clusterIds: [], camCertain: false });

            let timerFired = false;
            const nextTask = new Promise(resolve => setTimeout(() => { timerFired = true; resolve(); }, 0));
            const started = performance.now();
            viewer.SelectCncSetup('setup-1');
            const selectElapsedMs = performance.now() - started;
            await nextTask;
            await new Promise((resolve, reject) => {
                const deadline = performance.now() + 15000;
                function poll() {
                    const state = viewer.GetCncOverlayState();
                    if (state.overlayBuildPending !== true) { resolve(); return; }
                    if (performance.now() >= deadline) { reject(new Error('Timed out waiting for overlay worker.')); return; }
                    requestAnimationFrame(poll);
                }
                poll();
            });
            return { timerFired, selectElapsedMs, state: viewer.GetCncOverlayState() };
        }");

        Assert.True(result.GetProperty("timerFired").GetBoolean());
        Assert.InRange(result.GetProperty("selectElapsedMs").GetDouble(), 0D, 100D);
        JsonElement state = result.GetProperty("state");
        Assert.Equal(JsonValueKind.Null, state.GetProperty("overlayUnavailableReason").ValueKind);
        Assert.True(state.GetProperty("overlayWorkerUsed").GetBoolean());
        Assert.False(state.GetProperty("overlayBuildPending").GetBoolean());
        Assert.True(state.GetProperty("overlayBuildFrameCount").GetInt32() > 0);
        Assert.Single(state.GetProperty("selectedSetupIds").EnumerateArray());
        Assert.False(state.GetProperty("sourceModelVisible").GetBoolean());
        Assert.Equal(120001, state.GetProperty("renderedHighlightedTriangleCount").GetInt32());
        JsonElement limits = state.GetProperty("overlayLimits");
        Assert.True(limits.GetProperty("maxTriangles").GetInt32() >= 120001);
        Assert.True(limits.GetProperty("maxTrustedClusters").GetInt32() >= 257);
        Assert.True(limits.GetProperty("maxTriangleClusterComparisons").GetInt32() >= 5120000);
        Assert.Equal(100000, limits.GetProperty("maxSynchronousTriangles").GetInt32());
        Assert.Equal(256, limits.GetProperty("maxSynchronousClusters").GetInt32());
        Assert.Equal(4000000, limits.GetProperty("maxSynchronousComparisons").GetInt32());
    }
}
