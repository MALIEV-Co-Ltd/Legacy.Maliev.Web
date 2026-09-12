using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class CncNativeImportBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task NativePair_LoadsUnderApplicationCspAndPreservesAnalysisEnvelope()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync();
        await using IPage page = await context.NewPageAsync();
        IResponse? response = await page.GotoAsync(fixture.CncQuotationUrl);

        Assert.NotNull(response);
        string? policy = await response.HeaderValueAsync("content-security-policy");
        Assert.NotNull(policy);
        string scriptPolicy = policy.Split(';')
            .Single(value => value.TrimStart().StartsWith("script-src ", StringComparison.Ordinal));
        string[] scriptTokens = scriptPolicy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain("'unsafe-eval'", scriptTokens);
        Assert.Contains("'wasm-unsafe-eval'", scriptTokens);

        string bytes = Convert.ToBase64String(await File.ReadAllBytesAsync(fixture.CncFixturePath));
        JsonElement result = await page.EvaluateAsync<JsonElement>(
            """
            async base64 => {
                const worker = new Worker(window.malievModelWorkerUrl);
                let jobId = 0;
                const send = data => new Promise((resolve, reject) => {
                    const timeout = setTimeout(() => reject(new Error('native worker timeout')), 90000);
                    worker.onerror = event => { clearTimeout(timeout); reject(new Error(event.message)); };
                    worker.onmessage = event => {
                        clearTimeout(timeout);
                        event.data.success ? resolve(event.data) : reject(new Error(event.data.error));
                    };
                    worker.postMessage({ jobId: ++jobId, ...data });
                });
                const buffer = () => Uint8Array.from(atob(base64), value => value.charCodeAt(0)).buffer;
                try {
                    const immediate = await send({ action: 'parse', extension: 'step', analysisProfile: 'cnc', buffer: buffer() });
                    const preview = await send({ action: 'parse', extension: 'step', analysisProfile: 'cnc', deferCncAnalysis: true, buffer: buffer() });
                    const native = preview.analysisMeshes[0].nativeImport;
                    const analyzed = await send({ action: 'analyze', analysisProfile: 'cnc', meshes: preview.analysisMeshes });
                    const ordinary = await send({ action: 'parse', extension: 'step', analysisProfile: 'additive', buffer: buffer() });
                    const topology = analyzed.cncGeometry.cadTopology;
                    let previewSeen = false;
                    const stagedBuffer = buffer();
                    const staged = ModelParseWorkerManager.Submit({ action: 'parse', extension: 'step', analysisProfile: 'cnc',
                        deferCncAnalysis: true, retainNativeAnalysis: true, buffer: stagedBuffer }, [stagedBuffer], async result => {
                            if (result.stage !== 'native-preview' || !result.meshes[0].position.length) throw new Error('Native preview missing');
                            await new Promise(resolve => requestAnimationFrame(resolve));
                            previewSeen = true;
                        });
                    const completed = await staged.promise;
                    return { contract: topology.cadDocument.contract, faces: topology.faces.length,
                        immediateAccepted: immediate.cncGeometry.cadTopology.nativeInterpretation.interpretationAccepted,
                        stagedAccepted: completed.cncGeometry.cadTopology.nativeInterpretation.interpretationAccepted,
                        stagedEligible: completed.cncGeometry.cadTopology.automaticPlanningEligible, previewSeen,
                        reconstructedAccepted: topology.nativeInterpretation.interpretationAccepted,
                        sameRevision: topology.cadDocument.nativeImport.importRevision === native.importRevision,
                        nativeFactory: native.buildIdentity.jsSha256,
                        eligible: topology.automaticPlanningEligible,
                        ordinaryNative: !!ordinary.analysisMeshes?.[0]?.nativeImport,
                        ordinaryCnc: ordinary.cncGeometry };
                } finally { worker.terminate(); }
            }
            """,
            bytes);

        Assert.Equal("CadDocument.v2", result.GetProperty("contract").GetString());
        Assert.Equal(6, result.GetProperty("faces").GetInt32());
        Assert.True(result.GetProperty("sameRevision").GetBoolean());
        Assert.Equal(
            "0f4759e678eafaf72a85e6a46ba3cf7cd324a88f7882d64d8dffdbaed25f2f90",
            result.GetProperty("nativeFactory").GetString());
        Assert.True(result.GetProperty("immediateAccepted").GetBoolean());
        Assert.True(result.GetProperty("stagedAccepted").GetBoolean());
        Assert.True(result.GetProperty("previewSeen").GetBoolean());
        Assert.False(result.GetProperty("stagedEligible").GetBoolean());
        Assert.False(result.GetProperty("reconstructedAccepted").GetBoolean());
        Assert.False(result.GetProperty("eligible").GetBoolean());
        Assert.False(result.GetProperty("ordinaryNative").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("ordinaryCnc").ValueKind);
    }

    [Fact]
    public async Task NativePageUpload_PaintsPreviewBeforeVerifiedAnalysisWithoutFinalPrice()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync();
        await using IPage page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.RouteAsync("**/*", async route =>
        {
            if (route.Request.Url.Contains("Handler=UploadFile", StringComparison.OrdinalIgnoreCase))
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = "{\"success\":true,\"path\":\"runtime-test/box.step\",\"receipt\":\"isolated-runtime-test-receipt\"}",
                });
                return;
            }

            await route.ContinueAsync();
        });
        await page.GotoAsync(fixture.CncQuotationUrl);
        try
        {
            await page.WaitForFunctionAsync(
                "() => window.viewer && typeof window.viewer.ParseFile === 'function'",
                null,
                new PageWaitForFunctionOptions { Timeout = 30000 });
        }
        catch (TimeoutException error)
        {
            string diagnostics = await page.EvaluateAsync<string>(
                """
                () => JSON.stringify({
                    three: typeof window.THREE,
                    orbit: typeof window.THREE?.OrbitControls,
                    scripts: Array.from(document.scripts).map(script => script.src).filter(Boolean)
                })
                """);
            throw new InvalidOperationException(
                "The CNC page did not initialize its model viewer. Page errors: " + string.Join(" | ", pageErrors)
                    + ". Browser state: " + diagnostics,
                error);
        }
        await page.EvaluateAsync(
            """
            () => {
                window.__nativePageOrder = [];
                const parse = viewer.ParseFile;
                viewer.ParseFile = function(file, ready, options) {
                    const preview = options.onPreviewReady;
                    return parse.call(this, file, function(...args) {
                        window.__nativePageOrder.push(args[0] ? 'final' : 'error');
                        return ready(...args);
                    }, { ...options, onPreviewReady: function(...args) {
                        window.__nativePageOrder.push('preview');
                        return Promise.resolve(preview(...args)).then(() => window.__nativePageOrder.push('painted'));
                    } });
                };
            }
            """);
        await page.SetInputFilesAsync("#file-input-hidden", fixture.CncFixturePath);
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const item = utils.GetItem(utils.GetActiveId());
                    return item?.parseComplete && item?.uploadComplete && item?.modelInfo?.cncGeometry?.cadTopology
                        && (item.cncStatus === 'review_required' || item.cncStatus === 'finalized' || item.cncQuoteError);
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 90000 });
        }
        catch (TimeoutException error)
        {
            string state = await page.EvaluateAsync<string>(
                """
                () => {
                    const item = window.utils?.GetItem(window.utils?.GetActiveId());
                    return JSON.stringify({ order: window.__nativePageOrder,
                        item: item && { parseComplete:item.parseComplete, uploadComplete:item.uploadComplete,
                            previewComplete:item.previewComplete, cncStatus:item.cncStatus,
                            cncQuoteError:item.cncQuoteError, workerError:item.cncWorkerError,
                            progress:item.cncProgress, hasTopology:!!item.modelInfo?.cncGeometry?.cadTopology },
                        errors: window.__malievErrors });
                }
                """);
            throw new InvalidOperationException(
                "The native page upload did not reach a terminal analysis state. Page errors: "
                    + string.Join(" | ", pageErrors) + ". State: " + state,
                error);
        }
        JsonElement result = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const item = utils.GetItem(utils.GetActiveId()), topology = item.modelInfo.cncGeometry.cadTopology;
                return { order:window.__nativePageOrder.join(','), accepted:topology.nativeInterpretation.interpretationAccepted,
                    eligible:topology.automaticPlanningEligible, quote:item.cncQuote, status:item.cncStatus,
                    source:topology.cadDocument.nativeImport.sourceBytesHash };
            }
            """);

        Assert.Equal("preview,painted,final", result.GetProperty("order").GetString());
        Assert.True(result.GetProperty("accepted").GetBoolean());
        Assert.False(result.GetProperty("eligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("quote").ValueKind);
        Assert.Equal("review_required", result.GetProperty("status").GetString());
        // Legacy pins STEP fixtures as binary, so this is the SHA-256 of the committed Git blob;
        // it does not vary with a checkout's text line-ending conversion.
        Assert.Equal(
            "07de60e7ccf78f903386e4ad7fd04870fe31f0c96c1544b66f926ca60f91e1b6",
            result.GetProperty("source").GetString());
    }
}

public sealed class CncNativeBrowserFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? factory;
    private HttpClient? hostClient;
    private IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = null!;

    public string CncQuotationUrl { get; private set; } = string.Empty;

    public string CncFixturePath => Path.Combine(AppContext.BaseDirectory, "TestAssets", "Cnc", "box-20x30x40.step");

    public async Task InitializeAsync()
    {
        int port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory = new TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        factory.UseKestrel(port);
        hostClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        CncQuotationUrl = new Uri(origin, "/instantquotation/cnc-machining?culture=en").ToString();
        using HttpResponseMessage readiness = await hostClient.GetAsync(CncQuotationUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        BrowserHostIdentityVerifier.EnsureCurrentBuild(await readiness.Content.ReadAsStringAsync());

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();
        hostClient?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }
    }

    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition(Name)]
public sealed class CncNativeBrowserCollection : ICollectionFixture<CncNativeBrowserFixture>
{
    public const string Name = "CNC native browser";
}
