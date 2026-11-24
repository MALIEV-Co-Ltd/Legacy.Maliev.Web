using System.Reflection;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class PreviewWorkerBoundaryBrowserTests
{
    private const string WorkflowModule = "/dist/instant-quotation-workflow.mjs";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenderedCaller_StopsAtItsOwnWorkerPreview(bool additive)
    {
        var origin = new Uri($"http://127.0.0.1:{BrowserTestServerPort.Reserve()}");
        string routePath = additive ? "/instantquotation/3d-printing" : "/instantquotation/cnc-machining";
        await using var factory = new PreviewFactory(routePath, additive);
        factory.UseKestrel(origin.Port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin, AllowAutoRedirect = false });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            ExecutablePath = Environment.GetEnvironmentVariable("MALIEV_PREVIEW_TEST_CHROMIUM"),
            Args = ["--disable-background-networking", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--disable-quic", "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1"],
        });
        await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
        var uploadIntercepts = 0;
        var unexpectedHandlerRequests = 0;
        var unexpected = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            var uri = new Uri(request.Url);
            if (uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
            {
                await route.AbortAsync();
                return;
            }

            if (!additive && request.Method == "POST" && string.Equals(uri.AbsolutePath, routePath, StringComparison.OrdinalIgnoreCase)
                && uri.Query.Contains("handler=UploadFile", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref uploadIntercepts);
                await route.AbortAsync();
                return;
            }

            if (uri.Query.Contains("handler=", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref unexpectedHandlerRequests);
                await route.AbortAsync();
                return;
            }

            if (additive && request.Method == "GET" && uri.AbsolutePath == WorkflowModule && uri.Query.Length == 0)
            {
                await route.FulfillAsync(new() { ContentType = "text/javascript", Body = HeldClaimModule });
                return;
            }

            if (IsAllowedRequest(request.Method, uri.AbsolutePath, routePath, additive))
            {
                await route.ContinueAsync();
                return;
            }

            unexpected.Enqueue(request.Method + " " + uri.AbsolutePath);
            await route.AbortAsync();
        });
        await context.AddInitScriptAsync(WorkerGuard);
        await using var page = await context.NewPageAsync();
        await OptionalPrintingDriverFixture.UseUnavailableDriverAsync(page);
        page.SetDefaultTimeout(15000);
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        page.Console += (_, message) => { if (message.Type == "error") errors.Enqueue(message.Text); };
        page.Response += (_, response) =>
        {
            if (response.Status >= 400) errors.Enqueue(response.Status + " " + new Uri(response.Url).AbsolutePath);
        };
        var navigation = await page.GotoAsync(new Uri(origin, routePath + "?culture=en").ToString());
        Assert.Equal(200, navigation?.Status);
        if (additive)
        {
            await WaitForAsync("""
                () => previewBoundary.claimGuard
                    && previewBoundary.boundInput === document.querySelector('#instant-quote-files')
                    && typeof previewBoundary.boundInput?._blazorInputFileNextFileId === 'number'
                """);
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "preview-box.stl",
                MimeType = "model/stl",
                Buffer = WallThicknessParityBrowserFixture.Box(1),
            });
            await WaitForAsync("() => previewBoundary.claimsHeld === 1 && previewBoundary.advisory?.state === 'complete' && previewBoundary.localPreview && document.querySelector('[data-workflow-viewer] canvas')");
        }
        else
        {
            await WaitForAsync("() => window.viewer && typeof viewer.ParseFile === 'function'");
            await page.EvaluateAsync("""
                () => {
                    const parse = viewer.ParseFile;
                    if (typeof GetCncEstimate !== 'function' || GetCncEstimate !== window.GetCncEstimate) throw new Error('Estimate guard cannot be installed');
                    window.GetCncEstimate = () => {
                        previewBoundary.estimates++;
                        throw new Error('Estimate blocked by preview fixture');
                    };
                    viewer.ParseFile = function(file, terminal, options) {
                        if (options.analysisProfile !== 'cnc') throw new Error('Unexpected profile');
                        return parse.call(this, file, () => { previewBoundary.terminalCallbacks++; }, options);
                    };
                }
                """);
            await page.SetInputFilesAsync("#file-input-hidden",
                Path.Combine(AppContext.BaseDirectory, "TestAssets", "Cnc", "box-20x30x40.step"));
            await WaitForAsync("() => previewBoundary.cncPreview && previewBoundary.continuationsBlocked === 1");
        }

        var observed = await page.EvaluateAsync<JsonElement>("() => previewBoundary");
        Assert.Equal(0, observed.GetProperty("forbiddenSockets").GetInt32());
        Assert.Equal(0, observed.GetProperty("estimates").GetInt32());
        Assert.Equal(0, observed.GetProperty("terminalCallbacks").GetInt32());
        Assert.Equal(0, observed.GetProperty("forbiddenWorkerDispatches").GetInt32());
        Assert.Equal(0, unexpectedHandlerRequests);
        Assert.Equal(0, factory.OutboundAttempts);
        Assert.Equal(0, factory.RejectedHostRequests);
        Assert.Equal(0, factory.SessionWrites);
        Assert.Equal(0, factory.ForbiddenCalls);
        Assert.Empty(unexpected);
        if (additive)
        {
            Assert.Equal(0, uploadIntercepts);
            Assert.Equal(1, observed.GetProperty("thicknessRequests").GetInt32());
            Assert.Equal(0, observed.GetProperty("parseRequests").GetInt32());
            Assert.True(observed.GetProperty("advisory").GetProperty("measuredSampleCount").GetInt32() > 0);
        }
        else
        {
            Assert.True(uploadIntercepts > 0);
            Assert.Equal(1, observed.GetProperty("parseRequests").GetInt32());
            Assert.Equal(0, observed.GetProperty("thicknessRequests").GetInt32());
            Assert.False(observed.GetProperty("cncPreview").GetProperty("hasThickness").GetBoolean());
        }

        async Task WaitForAsync(string predicate)
        {
            try { await page.WaitForFunctionAsync(predicate); }
            catch (TimeoutException error)
            {
                string state = await page.EvaluateAsync<string>("""
                    () => {
                        const input = document.querySelector('#instant-quote-files');
                        return JSON.stringify({ ...window.previewBoundary, input: !!input,
                            inputReady: typeof input?._blazorInputFileNextFileId === 'number',
                            inputDisabled: input?.disabled, location: location.pathname,
                            errorUi: getComputedStyle(document.querySelector('#blazor-error-ui') ?? document.body).display });
                    }
                    """);
                throw new InvalidOperationException($"Preview did not reach the requested boundary. State: {state}. Errors: {string.Join("; ", errors)}. Blocked: {string.Join("; ", unexpected)}.", error);
            }
        }
    }

    private static bool IsAllowedRequest(string method, string path, string routePath, bool additive) =>
        (method == "GET" && (string.Equals(path, routePath, StringComparison.OrdinalIgnoreCase) || IsStatic(path)))
        || (additive && ((method == "GET" && path is "/_blazor/initializers" or "/_blazor")
            || (method == "POST" && path == "/_blazor/negotiate")));

    private static bool IsStatic(string path) => path.StartsWith("/_framework/", StringComparison.Ordinal)
        || path.StartsWith("/_content/", StringComparison.Ordinal)
        || path.StartsWith("/dist/", StringComparison.Ordinal)
        || path.StartsWith("/src/", StringComparison.Ordinal)
        || path.StartsWith("/lib/", StringComparison.Ordinal)
        || path.StartsWith("/images/", StringComparison.Ordinal)
        || path.StartsWith("/css/", StringComparison.Ordinal)
        || path.StartsWith("/fonts/", StringComparison.Ordinal)
        || path.StartsWith("/js/", StringComparison.Ordinal)
        || path is "/favicon.ico" or "/favicon.svg" or "/favicon.webp" or "/favicon-32x32.webp"
            or "/favicon-16x16.webp" or "/site.webmanifest";

    private const string HeldClaimModule = """
        import { createInstantQuotationWorkflowInterop as create } from '/dist/instant-quotation-workflow.mjs?preview-boundary-real';
        export * from '/dist/instant-quotation-workflow.mjs?preview-boundary-real';
        export async function createInstantQuotationWorkflowInterop(...args) {
            const actual = await create(...args);
            if (typeof actual.bindInput !== 'function' || typeof actual.getGeometryClaim !== 'function') {
                previewBoundary.bindingError = 'Missing methods from real module: ' + Object.keys(actual).join(',');
                throw new Error(previewBoundary.bindingError);
            }
            previewBoundary.claimGuard = true;
            return {
                ...actual,
                bindInput(element) {
                    previewBoundary.bindingEntered = true;
                    try {
                        actual.bindInput(element);
                        previewBoundary.boundInput = element;
                    } catch (error) {
                        previewBoundary.bindingError = String(error);
                        throw error;
                    }
                },
                async selectLocalPreview(key) {
                    previewBoundary.localPreview = key;
                    return actual.selectLocalPreview(key);
                },
                async getGeometryClaim(key) {
                    await actual.getGeometryClaim(key);
                    previewBoundary.claimsHeld++;
                    // Never send the claim to .NET or release the upload reservation.
                    return new Promise(() => {});
                }
            };
        }
        """;

    private const string WorkerGuard = """
        (() => {
            const state = window.previewBoundary = {
                estimates: 0, terminalCallbacks: 0, forbiddenWorkerDispatches: 0, continuationsBlocked: 0,
                claimsHeld: 0, thicknessRequests: 0, parseRequests: 0, forbiddenSockets: 0
            };
            const NativeSocket = window.WebSocket;
            window.WebSocket = class extends NativeSocket {
                constructor(url, ...args) {
                    const endpoint = new URL(url, location.href);
                    if (!location.pathname.endsWith('/3d-printing') || endpoint.protocol !== 'ws:'
                        || endpoint.host !== location.host || endpoint.pathname !== '/_blazor') {
                        state.forbiddenSockets++;
                        throw new Error('Unexpected socket in preview fixture');
                    }
                    super(url, ...args);
                }
            };
            const NativeWorker = window.Worker;
            window.Worker = class extends NativeWorker {
                constructor(url, options) {
                    const path = new URL(url, location.href).pathname;
                    const thickness = path.endsWith('/wall-thickness-runner.worker.js');
                    const model = path.endsWith('/model-viewer.worker.js');
                    if (!thickness && !model) {
                        state.forbiddenWorkerDispatches++;
                        throw new Error('Unexpected worker in preview fixture');
                    }
                    super(url, options);
                    let job;
                    this.addEventListener('message', ({data}) => {
                        if (thickness && data.evidence) state.advisory = data.evidence.summary;
                        if (model && data.jobId === job && data.success && data.stage === 'native-preview' && data.meshes?.length) {
                            state.cncPreview = {hasThickness: 'wallThickness' in data || 'thicknessAnalysis' in data
                                || 'wallThickness' in data.modelInfo || 'thicknessAnalysis' in data.modelInfo};
                        }
                    });
                    const post = this.postMessage.bind(this);
                    this.postMessage = (message, ...transfer) => {
                        if (thickness && location.pathname.endsWith('/3d-printing')) {
                            state.thicknessRequests++;
                            return post(message, ...transfer);
                        }
                        if (model && message.action === 'parse' && message.analysisProfile === 'cnc'
                            && message.deferCncAnalysis === true && location.pathname.endsWith('/cnc-machining')) {
                            state.parseRequests++;
                            job = message.jobId;
                            return post(message, ...transfer);
                        }
                        if (model && message.action === 'continue-native-analysis' && state.cncPreview && message.jobId === job) {
                            state.continuationsBlocked++;
                            return;
                        }
                        state.forbiddenWorkerDispatches++;
                    };
                }
            };
        })();
        """;

    private sealed class PreviewFactory(string routePath, bool additive) : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        public int OutboundAttempts;
        public int RejectedHostRequests;
        public int SessionWrites;
        public int ForbiddenCalls;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationSessionStore>();
                services.AddSingleton<IInstantQuotationSessionStore>(new PreviewSession(this));
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new BlockOutbound(this));
                services.AddSingleton<IStartupFilter>(new GuardHost(this, routePath, additive));
                Block<IInstantQuotationUploadClient>(services);
                Block<IInstantQuotationPricingService>(services);
                Block<IInstantQuotationAuthoritativePricingService>(services);
                Block<IInstantQuotationSubmissionService>(services);
            });
        }

        private void Block<T>(IServiceCollection services) where T : class
        {
            services.RemoveAll<T>();
            var service = DispatchProxy.Create<T, ForbiddenCallProxy>();
            ((ForbiddenCallProxy)(object)service).OnCall = () => Interlocked.Increment(ref ForbiddenCalls);
            services.AddSingleton(service);
        }
    }

    public class ForbiddenCallProxy : DispatchProxy
    {
        internal Action? OnCall { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            OnCall?.Invoke();
            throw new InvalidOperationException("Terminal workflow service invoked in a preview test.");
        }
    }

    private sealed class GuardHost(PreviewFactory control, string routePath, bool additive) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, proceed) =>
            {
                var request = context.Request;
                string path = request.Path.Value ?? string.Empty;
                if (!request.Query.ContainsKey("handler") && IsAllowedRequest(request.Method, path, routePath, additive))
                {
                    await proceed();
                    return;
                }
                Interlocked.Increment(ref control.RejectedHostRequests);
                context.Response.StatusCode = 403;
            });
            next(app);
        };
    }

    private sealed class PreviewSession(PreviewFactory control) : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState state, CancellationToken cancellationToken) =>
            Task.FromResult(new InstantQuotationSessionState(new string('a', 64), "preview-only", state, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationSessionState?>(null);
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw UnexpectedWrite();
        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw UnexpectedWrite();

        private InvalidOperationException UnexpectedWrite()
        {
            Interlocked.Increment(ref control.SessionWrites);
            return new("Preview session mutations are forbidden.");
        }
    }

    private sealed class BlockOutbound(PreviewFactory control) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler = new RejectTransport(control, builder.Name);
        };
    }

    private sealed class RejectTransport(PreviewFactory control, string? clientName) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The rendered pages bootstrap a country picker. Serve the existing
            // offline fallback without supplying country data or opening a socket.
            if (clientName == "countries" && request.Method == HttpMethod.Get && request.RequestUri is { } uri
                && uri.AbsolutePath == "/Countries" && uri.Query.Length == 0)
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));

            Interlocked.Increment(ref control.OutboundAttempts);
            throw new InvalidOperationException("Preview fixture cannot call upstream services.");
        }
    }
}
