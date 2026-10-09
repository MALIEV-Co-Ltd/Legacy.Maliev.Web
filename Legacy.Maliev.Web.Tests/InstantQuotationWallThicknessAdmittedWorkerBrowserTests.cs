using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

// fad52019c898ebe63c39eb0f30e65b0fb59126ca: strengthen already ported behavior at
// the admitted Blazor page boundary. No replacement worker, pricing or receipt.
public sealed class InstantQuotationWallThicknessAdmittedWorkerBrowserTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0.7f, "en", 1280, true, "0.80 mm", "0.60 mm")]
    [InlineData(0.7f, "th", 375, true, "0.80 มม.", "0.60 มม.")]
    [InlineData(1f, "en", 1280, false, "0.80 mm", "0.60 mm")]
    [InlineData(1f, "th", 375, false, "0.80 มม.", "0.60 มม.")]
    public async Task AdmittedSlabRetainsRealThicknessEvidenceWhenMaterialChanges(
        float height, string culture, int width, bool thin, string fdmLimit, string resinLimit)
    {
        var transport = new BoundUploadTransport();
        await using var factory = new AdmittedWallFactory(transport);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory.UseKestrel(port);
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        using var admittedRoute = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, admittedRoute.StatusCode);
        const string runner = "src/app/js/instant-quotation/wall-thickness-runner.worker.js";
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", runner)),
            await http.GetByteArrayAsync("/" + runner));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 800 },
            HasTouch = width < 600,
            IsMobile = width < 600,
        });
        // Observe the real native worker without intercepting messages/results or changing callbacks.
        await context.AddInitScriptAsync("""
            (() => {
              const NativeWorker = window.Worker;
              window.__wallAcceptance = { started: 0, evidence: [], errors: [] };
              window.Worker = class extends NativeWorker {
                constructor(url, options) {
                  super(url, options);
                  if (String(url).includes('wall-thickness-runner.worker.js')) {
                    window.__wallAcceptance.started++;
                    this.addEventListener('message', event => {
                      if (event.data.evidence) window.__wallAcceptance.evidence.push(event.data.evidence);
                      if (event.data.error) window.__wallAcceptance.errors.push(event.data.error);
                    });
                    this.addEventListener('error', () => window.__wallAcceptance.errors.push('native worker error'));
                  }
                }
              };
            })();
            """);
        await using var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        var consoleErrors = new List<string>();
        var consoleWarnings = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error") consoleErrors.Add(message.Text);
            if (message.Type == "warning") consoleWarnings.Add(message.Text);
        };
        IResponse? response;
        using (var navigation = new InstantQuotationNavigationFailureDiagnostics(page, origin))
        {
            response = await InstantQuotationNavigationFailureDiagnostics.PreserveFailureAsync(
                () => page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle }),
                () => navigation.WriteFailureAsync(output, new
                {
                    culture,
                    width,
                    height,
                    thin,
                    browserConnected = browser.IsConnected,
                    pageClosed = page.IsClosed,
                    pageErrorCount = pageErrors.Count,
                    consoleErrorCount = consoleErrors.Count,
                    consoleWarningCount = consoleWarnings.Count,
                    transport.UploadCount,
                    transport.ReadCount,
                }));
        }
        Assert.Equal(200, response?.Status);
        Assert.Contains("/instantquotation/3d-printing", page.Url, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ประเมินราคาพิมพ์ 3 มิติทันที" : "Instant 3D Printing Estimate",
            await page.TitleAsync(), StringComparison.Ordinal);
        Assert.True(await page.Locator("#instant-quote-files").IsVisibleAsync());
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = $"source-wall-{height.ToString(System.Globalization.CultureInfo.InvariantCulture)}.stl",
            MimeType = "model/stl",
            Buffer = WallThicknessParityBrowserFixture.Box(height),
        });
        await page.Locator("[data-workflow-part]").WaitForAsync();
        await page.WaitForFunctionAsync("() => window.__wallAcceptance.evidence.length === 1 && !!document.querySelector('.instant-quote__thickness-toggle:not(:disabled)')");
        var actual = await page.EvaluateAsync<JsonElement>("""
            () => ({started:window.__wallAcceptance.started,
              summary:window.__wallAcceptance.evidence[0].summary,
              errors:window.__wallAcceptance.errors})
            """);
        Assert.Equal(1, actual.GetProperty("started").GetInt32());
        Assert.Equal("complete", actual.GetProperty("summary").GetProperty("state").GetString());
        Assert.True(actual.GetProperty("summary").GetProperty("measuredSampleCount").GetInt32() > 0);
        Assert.InRange(actual.GetProperty("summary").GetProperty("minMm").GetDouble(), height - 0.05, height + 0.05);
        Assert.Equal(0, actual.GetProperty("errors").GetArrayLength());

        var material = page.Locator("[data-workflow-material-picker] select[name='material']");
        await material.SelectOptionAsync("ABS");
        await page.Locator("[data-workflow-price-tier]").Last.WaitForAsync();
        try
        {
            await page.WaitForFunctionAsync("() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')");
        }
        catch (TimeoutException)
        {
            // Observe only after the original deadline; never replace the primary timeout.
            try
            {
                using var diagnosticDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var observation = page.EvaluateAsync<string>("""
                    () => {
                        const configuration = document.querySelector('[data-workflow-configuration]');
                        const review = configuration?.querySelector('.instant-quote__configuration-actions button');
                        const state = document.querySelector('.instant-quote__workflow')?.dataset.workflowState;
                        const states = ['empty', 'uploading', 'uploaded', 'error', 'multipart', 'configured', 'review', 'customerdetails', 'submitted'];
                        return JSON.stringify({
                            workflow: states.includes(state) ? state : 'unknown',
                            configurationPresent: !!configuration,
                            reviewPresent: !!review,
                            reviewDisabled: review ? review.disabled : null,
                            pricingLoading: !!document.querySelector('[data-pricing-loading-status]'),
                            materialMatched: configuration?.querySelector('select[name="material"]')?.value === 'ABS',
                            partCount: Math.min(document.querySelectorAll('[data-workflow-part]').length, 10),
                            priceTierCount: Math.min(document.querySelectorAll('[data-workflow-price-tier]').length, 10),
                            unavailableCount: Math.min(document.querySelectorAll('[data-workflow-price-unavailable]').length, 10)
                        });
                    }
                    """);
                _ = observation.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                var rendered = await observation.WaitAsync(diagnosticDeadline.Token);
                output.WriteLine($"[admitted-abs-review-render] {rendered}; pageErrorCount={Math.Min(pageErrors.Count, 10)}; consoleErrorCount={Math.Min(consoleErrors.Count, 10)}");
                var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
                var observed = await store.GetAsync(transport.SessionId!, transport.OwnerIdentity, diagnosticDeadline.Token)
                    .WaitAsync(diagnosticDeadline.Token);
                var observedPart = observed is not null && observed.Parts.Count == 1 ? observed.Parts[0] : null;
                output.WriteLine($"[admitted-abs-review-authority] sessionPresent={observed is not null}; authorizationPresent={(observed is null ? "unknown" : (observed.QuoteAuthorization is not null).ToString())}; singlePartPresent={(observed is null ? "unknown" : (observedPart is not null).ToString())}; materialMatched={(observedPart is null ? "unknown" : (observedPart.Configuration.MaterialKey == "ABS").ToString())}");
            }
            catch
            {
                try { output.WriteLine("[admitted-abs-review] observation=unavailable; authority=unknown"); }
                catch { /* A failing output sink cannot replace the primary timeout. */ }
            }
            throw;
        }
        var partId = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
        var sessions = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        var session = await sessions.GetAsync(transport.SessionId!, transport.OwnerIdentity, default);
        Assert.NotNull(session);
        Assert.NotNull(session.QuoteAuthorization);
        var configuration = Assert.Single(session.Parts, item => item.PartId == partId).Configuration;
        var receipt = Assert.Single(session.PhysicalReceipts!, item => item.PartId == partId
            && item.ConfiguredMaterialKey == "ABS" && item.MaterialKey == "ABS"
            && item.Quantity == configuration.Quantity && item.BuildPreference == configuration.BuildPreference);
        Assert.Equal(transport.FileId, receipt.FileId);
        Assert.Equal(transport.Sha256, receipt.UploadSha256);
        Assert.Equal(session.SessionId, receipt.SessionId);
        Assert.Equal(session.OwnerIdentity, receipt.OwnerIdentity);
        Assert.True(receipt.MotionSeconds > 0);
        Assert.True(receipt.DepositedMm3 > 0);
        Assert.Equal(1, transport.UploadCount);
        Assert.True(transport.ReadCount > 0);
        Assert.IsType<InstantQuotationPricingService>(factory.Services.GetRequiredService<IInstantQuotationPricingService>());
        await using var pricingScope = factory.Services.CreateAsyncScope();
        var quote = await pricingScope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>()
            .QuoteAsync(session, session.OwnerIdentity, includeComparisons: true, CancellationToken.None);
        Assert.NotNull(quote);
        Assert.True(quote.FinalOrderPrice > 0);
        Assert.True(factory.Services.GetRequiredService<IInstantQuotationQuoteTicketService>()
            .Validate(session, quote, session.QuoteAuthorization, DateTimeOffset.UtcNow));

        if (thin) await page.Locator("[data-dfm-code='thin-wall']").WaitForAsync();
        Assert.Equal(thin ? 1 : 0, await page.Locator("[data-dfm-code='thin-wall']").CountAsync());
        Assert.Equal(0, await page.Locator("[data-dfm-code='thickness-incomplete']").CountAsync());
        if (thin) Assert.Contains(fdmLimit, await page.Locator("[data-dfm-code='thin-wall']").InnerTextAsync(), StringComparison.Ordinal);
        var total = page.Locator("[data-workflow-summary-dock] > summary > strong[aria-live]");
        var before = await total.InnerTextAsync();
        Assert.NotEqual("—", before);
        var toggle = page.Locator(".instant-quote__thickness-toggle");
        await toggle.ScrollIntoViewIfNeededAsync();
        if (width < 600) await toggle.TapAsync();
        else { await toggle.FocusAsync(); await page.Keyboard.PressAsync("Enter"); }
        await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'true'");
        Assert.Contains(fdmLimit, await page.Locator(".instant-quote__thickness-legend").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await total.InnerTextAsync());
        Assert.True(await page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").IsEnabledAsync());
        var afterToggle = await sessions.GetAsync(transport.SessionId!, transport.OwnerIdentity, default);
        Assert.Equal(session.QuoteAuthorization.OrderTicket, afterToggle!.QuoteAuthorization!.OrderTicket);
        Assert.Equal(receipt, Assert.Single(afterToggle.PhysicalReceipts!, item => item.PartId == partId
            && item.ConfiguredMaterialKey == "ABS" && item.MaterialKey == "ABS"
            && item.Quantity == configuration.Quantity && item.BuildPreference == configuration.BuildPreference));
        Assert.True(await page.Locator("[data-workflow-viewer] canvas").IsVisibleAsync());
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        await ScreenshotAsync(page, $"admitted-fdm-{height}-{culture}-{width}");

        await material.SelectOptionAsync("M68");
        await page.WaitForFunctionAsync("limit => document.querySelector('.instant-quote__thickness-legend')?.textContent?.includes(limit)", resinLimit);
        Assert.Equal("M68", await material.InputValueAsync());
        Assert.Equal(0, await page.Locator("[data-dfm-code='thin-wall']").CountAsync());
        Assert.DoesNotContain("needs-attention", await toggle.GetAttributeAsync("class"), StringComparison.Ordinal);
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__wallAcceptance.started"));
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__wallAcceptance.evidence.length"));
        Assert.Equal(actual.GetProperty("summary").GetProperty("measuredSampleCount").GetInt32(),
            await page.EvaluateAsync<int>("window.__wallAcceptance.evidence[0].summary.measuredSampleCount"));
        Assert.Equal(1, transport.UploadCount);
        await ScreenshotAsync(page, $"admitted-resin-{height}-{culture}-{width}");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'false'");
        Assert.False(await page.Locator(".instant-quote__thickness-legend").IsVisibleAsync());
        Assert.Empty(pageErrors);
        Assert.Empty(consoleErrors);
        Assert.All(consoleWarnings, warning => Assert.Matches(
            @"\A\[\.WebGL-0x[0-9a-f]{1,16}\]GL Driver Message \(OpenGL, Performance, GL_CLOSE_PATH_NV, High\): GPU stall due to ReadPixels(?: \(this message will no longer repeat\))?\z",
            warning));
    }

    private static Task ScreenshotAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return Task.CompletedTask;
        Directory.CreateDirectory(directory);
        return page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(directory, name + ".png"), FullPage = true });
    }

    private sealed class AdmittedWallFactory(BoundUploadTransport transport)
        : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationUploadClient>();
                services.AddSingleton<IInstantQuotationUploadClient>(transport);
                services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                services.AddScoped<IInstantQuotationPhysicalAnalysisInputReader>(provider =>
                    new BoundInputReader(transport, provider.GetRequiredService<IInstantQuotationSessionStore>()));
            });
        }
    }

    private sealed class BoundUploadTransport : IInstantQuotationUploadClient
    {
        public string? SessionId { get; private set; }
        public string? OwnerIdentity { get; private set; }
        public Guid FileId { get; private set; }
        public string? FileName { get; private set; }
        public string? Sha256 { get; private set; }
        public byte[]? Bytes { get; private set; }
        public int UploadCount { get; private set; }
        public int ReadCount { get; set; }

        public async Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity,
            Stream content, string fileName, string contentType, long contentLength,
            InstantQuotationGeometryClaim geometryClaim, string operationId, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var bytes = copy.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (bytes.LongLength != contentLength || hash != geometryClaim.Sha256)
                return InstantQuotationUploadResult.Failed(operationId, InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized, InstantQuotationProblemCategory.Validation);
            SessionId = sessionId; OwnerIdentity = ownerIdentity; FileId = Guid.NewGuid();
            FileName = fileName; Sha256 = hash; Bytes = bytes; UploadCount++;
            return InstantQuotationUploadResult.Succeeded(operationId,
                new InstantQuotationUploadReference(FileId.ToString("D")), hash,
                new InstantQuotationPhysicalAnalysisUpload(FileId, fileName, contentType, bytes.Length, hash, "clean"));
        }

        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(InstantQuotationRemoveResult.Unavailable(operationId));

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity,
            int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(InstantQuotationFinalizationResult.Unavailable(operationId));
    }

    private sealed class BoundInputReader(BoundUploadTransport transport, IInstantQuotationSessionStore sessions)
        : IInstantQuotationPhysicalAnalysisInputReader
    {
        public async Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(string sessionId,
            string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            var session = await sessions.GetAsync(sessionId, ownerIdentity, cancellationToken);
            var part = session?.Parts.SingleOrDefault(item => item.PartId == partId);
            if (part?.PhysicalAnalysisUpload?.FileId != transport.FileId
                || sessionId != transport.SessionId || ownerIdentity != transport.OwnerIdentity || transport.Bytes is null)
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(InstantQuotationPhysicalAnalysisInputFailure.UploadMismatch);
            transport.ReadCount++;
            return new InstantQuotationPhysicalAnalysisInputResult(transport.Bytes,
                InstantQuotationPhysicalAnalysisInputFailure.None, transport.FileId, transport.FileName, transport.Sha256);
        }
    }
}
