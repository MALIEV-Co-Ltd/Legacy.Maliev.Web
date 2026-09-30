using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

// Source fad52019c898ebe63c39eb0f30e65b0fb59126ca has four scenarios.
// Coarse projection comes from 483a2fdb0beeafa014bb6b0a5ca1e85cd7bdbb74,
// refined by 351825232f787ef988c7c058910520a14aa70123.
public sealed class InstantQuotationWallThicknessParityBrowserTests(WallThicknessParityBrowserFixture fixture)
    : IClassFixture<WallThicknessParityBrowserFixture>
{
    [Theory]
    [InlineData(0.7f, true)]
    [InlineData(1f, false)]
    public async Task ActualWorkerEvidenceSurvivesMaterialReinterpretationWithoutReanalysis(float height, bool thin)
    {
        await using var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.Url + "?culture=en");
        var result = await RunViewerAsync(page, WallThicknessParityBrowserFixture.Box(height));
        Assert.True(result.GetProperty("reliable").GetBoolean());
        Assert.Equal(thin, result.GetProperty("thin").GetBoolean());
        Assert.True(result.GetProperty("sameEvidence").GetBoolean());
        Assert.Equal(1, result.GetProperty("workerCount").GetInt32());
        Assert.Equal("resin", result.GetProperty("resinProcess").GetString());
        Assert.Equal(0.6, result.GetProperty("resinLimit").GetDouble());
        Assert.False(result.GetProperty("resinThin").GetBoolean());
        Assert.True(result.GetProperty("appearanceRetained").GetBoolean());
    }

    [Fact]
    public async Task GenuineViewerProjectsCoarseOppositePatchLocallyAndHidesItForResin()
    {
        await using var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.Url + "?culture=en");
        var result = await RunViewerAsync(page, WallThicknessParityBrowserFixture.CoarseFaces());
        Assert.True(result.GetProperty("patchVisible").GetBoolean());
        Assert.True(result.GetProperty("patchLocal").GetBoolean());
        Assert.True(result.GetProperty("patchRendered").GetBoolean());
        Assert.True(result.GetProperty("patchHiddenForResin").GetBoolean());
        Assert.True(result.GetProperty("appearanceRetained").GetBoolean());
    }

    [Theory]
    [InlineData("en", "Thickness analysis is incomplete", 1280)]
    [InlineData("th", "ตรวจวัดความหนาได้ไม่ครบทุกบริเวณ", 1280)]
    [InlineData("en", "Thickness analysis is incomplete", 375)]
    [InlineData("th", "ตรวจวัดความหนาได้ไม่ครบทุกบริเวณ", 375)]
    public async Task ZeroVolumeSheetRetainsIncompletePreviewWithoutIssuingPhysicalPrice(string culture, string incomplete, int width)
    {
        await using var page = await fixture.Browser.NewPageAsync();
        var pageErrors = new List<string>();
        await page.SetViewportSizeAsync(width, 800);
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(fixture.Url + "?culture=" + culture, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "source-open-sheet.stl",
            MimeType = "model/stl",
            Buffer = WallThicknessParityBrowserFixture.Sheet(),
        });
        await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__workflow')?.dataset.workflowState === 'error' || !!document.querySelector('[data-workflow-incomplete-preview]')");
        Assert.Equal(0, await page.Locator("[data-workflow-price-tier]").CountAsync());
        Assert.Equal(0, await page.Locator("[data-workflow-dfm-clear]").CountAsync());
        Assert.Empty(pageErrors);
        Assert.Contains("/instantquotation/3d-printing", page.Url, StringComparison.Ordinal);
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
        // Region creation precedes the genuine worker callback; wait for that rendered evidence.
        await page.Locator("[data-workflow-incomplete-preview]").GetByText(incomplete, new LocatorGetByTextOptions { Exact = true }).WaitForAsync();
        if (Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(directory, $"wall397-incomplete-preview-{culture}-{width}.png"),
                FullPage = true,
            });
        }
        Assert.Contains(incomplete, await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
        Assert.True(await page.Locator("[data-workflow-incomplete-preview]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-workflow-incomplete-preview] canvas").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("""
            () => new Promise(resolve => requestAnimationFrame(() => {
              const canvas = document.querySelector('[data-workflow-incomplete-preview] canvas');
              const gl = canvas.getContext('webgl2');
              const pixels = new Uint8Array(canvas.width * canvas.height * 4);
              gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
              resolve(pixels.some((value, index) => index % 4 === 0
                && value < 230 && pixels[index + 1] < 230 && pixels[index + 2] < 230
                && pixels[index + 3] > 0));
            }))
            """), "The actual WebGL canvas must contain foreground mesh pixels, not only background.");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        await page.Locator("[data-workflow-incomplete-preview] button").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("[data-workflow-incomplete-preview]").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.Equal(0, await page.Locator("[data-workflow-incomplete-preview]").CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewSelectionReleasesPriorExceptionalPreviewEvenWhenReplacementIsNotExceptional(bool malformed)
    {
        await using var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.Url + "?culture=en", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "prior-sheet.stl",
            MimeType = "model/stl",
            Buffer = WallThicknessParityBrowserFixture.Sheet(),
        });
        await page.Locator("[data-workflow-incomplete-preview]").WaitForAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#instant-quote-files').disabled");
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "replacement.stl",
            MimeType = "model/stl",
            Buffer = malformed ? [1, 2, 3] : WallThicknessParityBrowserFixture.Box(1),
        });
        await page.Locator("[data-workflow-incomplete-preview]").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 5_000 });
        Assert.Equal(0, await page.Locator("[data-workflow-incomplete-preview]").CountAsync());
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
    }

    [Theory]
    [InlineData("CancelUpload")]
    [InlineData("QuarantinePreviewAsync")]
    [InlineData("ReleasePreviewAsync")]
    [InlineData("DisposeAsync")]
    public async Task RenderedLifecycleRemovesExceptionalRegionAndIgnoresLateReport(string operation)
    {
        var result = await WallThicknessParityBrowserFixture.RenderLifecycleAsync(operation);
        Assert.Contains("data-workflow-incomplete-preview", result.Before, StringComparison.Ordinal);
        Assert.DoesNotContain("data-workflow-incomplete-preview", result.After, StringComparison.Ordinal);
        Assert.Null(result.Key);
        Assert.Contains(operation == "DisposeAsync" ? "dispose" : "release", result.JsCalls);
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("remove")]
    [InlineData("dispose")]
    [InlineData("ineligible")]
    [InlineData("stale-failure")]
    [InlineData("current-failure")]
    [InlineData("eligibility-replacement")]
    [InlineData("eligibility-dispose")]
    public async Task DelayedAttachmentReplyCannotAttachOrClearAReplacementPreview(string transition)
    {
        var result = await WallThicknessParityBrowserFixture.RenderAttachmentRaceAsync(transition);
        Assert.False(result.StaleAttached);
        if (transition is "replacement" or "stale-failure" or "eligibility-replacement")
        {
            Assert.Equal("replacement-preview", result.KeyAfterOldReply);
            Assert.True(result.ReplacementAttached);
            Assert.Equal(["local-only-preview", "replacement-preview"], result.RetainedKeys);
        }
        else
        {
            Assert.Null(result.KeyAfterOldReply);
            Assert.DoesNotContain("data-workflow-incomplete-preview", result.HtmlAfterOldReply, StringComparison.Ordinal);
        }
    }

    private static Task<JsonElement> RunViewerAsync(IPage page, byte[] bytes) => page.EvaluateAsync<JsonElement>("""
        async base64 => {
          const { createWorkflowPreviewInterop } = await import('/src/app/js/instant-quotation/workflow-interop.mjs');
          const viewerModule = await import('/dist/instant-quotation-viewer.mjs');
          const { analyzeUploadDerivedGeometry } = await import('/src/app/js/instant-quotation/geometry-analysis.mjs');
          const { assessThickness } = await import('/src/app/js/instant-quotation/wall-thickness.mjs');
          const NativeWorker = window.Worker;
          let workerCount = 0, object, viewer;
          window.Worker = class extends NativeWorker {
            constructor(url, options) { super(url, options); if (String(url).includes('wall-thickness-runner')) workerCount++; }
          };
          const canvas = document.createElement('canvas');
          canvas.style.cssText = 'width:400px;height:300px'; document.body.append(canvas);
          let ready;
          const measured = new Promise(resolve => ready = resolve);
          const interop = createWorkflowPreviewInterop({
            loadModel: async (...args) => object = await viewerModule.loadStandaloneModel(...args),
            analyzeGeometry: analyzeUploadDerivedGeometry,
            createViewer: element => viewer = viewerModule.createThreeModelViewer(element),
            reportThickness: (...args) => { if (args[1]) ready(); }
          });
          try {
            const file = new File([Uint8Array.from(atob(base64), c => c.charCodeAt(0))], 'advisory-component.stl', {type:'model/stl'});
            interop.attach(canvas);
            const key = interop.beginSelection({files:[file]})[0];
            await interop.getGeometryClaim(key);
            // Browser preview component correlation only: never server admission or pricing authority.
            interop.admit(key, 'component-only', 'PLA'); interop.select('component-only');
            await Promise.race([measured, new Promise((_,reject) => setTimeout(() => reject(new Error('Real thickness worker did not complete')), 15000))]);
            const evidence = object.userData.wallThicknessEvidence;
            const originals = [];
            object.traverse(child => { if (child.isMesh && !child.userData.wallThicknessPatch) originals.push({child,color:child.material.color.getHex(),vertexColors:child.material.vertexColors,side:child.material.side}); });
            const fdm = assessThickness(evidence, 'PLA');
            interop.toggleThickness('component-only');
            viewer.snapshot('component-only');
            const patch = object.userData.wallThicknessPatchMesh;
            const positions = patch?.geometry.getAttribute('position');
            const patchVisible = !!patch?.visible;
            const patchLocal = !!positions?.count && Array.from({length:positions.count}, (_,i) => i).every(i =>
              Math.abs(positions.getY(i)) < 0.001 && positions.getZ(i) >= -0.001 && positions.getZ(i) <= 0.501);
            const patchRendered = !!patch?.userData.shader && patch.material.transparent
              && patch.geometry.getAttribute('wallThicknessMm')?.count === positions?.count;
            interop.setThicknessMaterial('component-only','M68'); viewer.snapshot('component-only');
            const resin = assessThickness(object.userData.wallThicknessEvidence, 'M68');
            return { reliable:fdm.reliable, thin:fdm.hasThinRegion, workerCount,
              sameEvidence:object.userData.wallThicknessEvidence === evidence,
              resinProcess:resin.process, resinLimit:resin.thinMm, resinThin:resin.hasThinRegion,
              patchVisible, patchLocal, patchRendered, patchHiddenForResin:!patch || !patch.visible,
              appearanceRetained:originals.every(({child,color,vertexColors,side}) => child.material.color.getHex() === color && child.material.vertexColors === vertexColors && child.material.side === side) };
          } finally { interop.dispose(); canvas.remove(); window.Worker = NativeWorker; }
        }
        """, Convert.ToBase64String(bytes));
}
