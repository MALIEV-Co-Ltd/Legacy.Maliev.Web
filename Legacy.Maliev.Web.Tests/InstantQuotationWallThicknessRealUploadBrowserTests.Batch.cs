using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationWallThicknessRealUploadBrowserTests
{
    /// <summary>The causal trace retains only bounded primitive metadata, never browser identities or text.</summary>
    [Fact]
    public void BatchMaterialTrace_RejectsPrivateFieldsInvalidValuesAndUnboundedRows()
    {
        const string secret = "private-batch-key-label-url-message-sentinel";
        var valid = System.Text.Json.JsonSerializer.Serialize(new
        {
            @event = 2,
            owner = true,
            requested = true,
            @checked = false,
            factoryReturned = true,
            trusted = true,
            information = false,
            connected = true,
            relativeMs = 7,
            key = secret,
            label = secret,
            url = secret,
            message = secret,
        });
        var rows = ParseBatchMaterialTrace("[" + string.Join(',', Enumerable.Repeat(valid, 40)) + "]");
        Assert.Equal(32, rows.Count);
        var retained = System.Text.Json.JsonSerializer.Serialize(rows);
        Assert.DoesNotContain(secret, retained, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(retained);
        Assert.All(document.RootElement.EnumerateArray(), row => Assert.Equal(
            new[] { "Event", "Owner", "Requested", "Checked", "FactoryReturned", "Trusted", "Information", "Connected", "RelativeMs" },
            row.EnumerateObject().Select(property => property.Name)));
        foreach (var invalid in new[]
        {
            valid.Replace("\"event\":2", "\"event\":8", StringComparison.Ordinal),
            valid.Replace("\"relativeMs\":7", "\"relativeMs\":-1", StringComparison.Ordinal),
            valid.Replace("\"relativeMs\":7", "\"relativeMs\":300001", StringComparison.Ordinal),
            valid.Replace("\"owner\":true", "\"owner\":\"private\"", StringComparison.Ordinal),
        })
        {
            var paired = ParseBatchMaterialTrace("[" + invalid + "," + valid + "]");
            var control = Assert.Single(paired);
            Assert.Equal(2, control.Event);
            Assert.Equal(7L, control.RelativeMs);
            Assert.True(control.Owner);
        }
    }

    private readonly record struct BatchMaterialTraceRow(int Event, bool Owner, bool Requested, bool Checked,
        bool FactoryReturned, bool Trusted, bool Information, bool Connected, long RelativeMs);

    private static IReadOnlyList<BatchMaterialTraceRow> ParseBatchMaterialTrace(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > 16384) return [];
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            var rows = new List<BatchMaterialTraceRow>();
            foreach (var value in document.RootElement.EnumerateArray().Take(32))
            {
                if (value.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !value.TryGetProperty("event", out var kind) || kind.ValueKind != System.Text.Json.JsonValueKind.Number
                    || !kind.TryGetInt32(out var code) || code is < 0 or > 7
                    || !value.TryGetProperty("relativeMs", out var elapsed) || elapsed.ValueKind != System.Text.Json.JsonValueKind.Number
                    || !elapsed.TryGetInt64(out var milliseconds) || milliseconds is < 0 or > 300000) continue;
                var names = new[] { "owner", "requested", "checked", "factoryReturned", "trusted", "information", "connected" };
                var flags = new bool[names.Length];
                var valid = true;
                for (var index = 0; index < names.Length; index++)
                {
                    if (!value.TryGetProperty(names[index], out var flag)
                        || flag.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                    {
                        valid = false;
                        break;
                    }
                    flags[index] = flag.GetBoolean();
                }
                if (valid) rows.Add(new BatchMaterialTraceRow(code, flags[0], flags[1], flags[2], flags[3],
                    flags[4], flags[5], flags[6], milliseconds));
            }
            return rows;
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private async Task CaptureBatchMaterialTraceAsync(IPage page)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var read = page.EvaluateAsync<string>("() => JSON.stringify(window.source428Trace?.snapshot() ?? [])");
            ObserveBatchTrace(read);
            var rows = ParseBatchMaterialTrace(await read.WaitAsync(timeout.Token));
            var retained = System.Text.Json.JsonSerializer.Serialize(rows);
            output.WriteLine("[batch-material-trace] " + retained);
            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "material-display-browser-diagnostics");
            Directory.CreateDirectory(directory);
            var write = File.WriteAllTextAsync(Path.Combine(directory, $"batch-material-click-{Guid.NewGuid():N}.json"), retained, timeout.Token);
            ObserveBatchTrace(write);
            await write.WaitAsync(timeout.Token);
        }
        catch (Exception) { /* Diagnostic failure must preserve the original assertion. */ }
    }

    private static async Task StopBatchMaterialTraceAsync(IPage page)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var stop = page.EvaluateAsync("() => window.source428Trace?.dispose()");
            ObserveBatchTrace(stop);
            await stop.WaitAsync(timeout.Token);
        }
        catch (Exception) { /* The existing owned-page close also releases the document observers. */ }
    }

    private static void ObserveBatchTrace(Task task) =>
        _ = task.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    [Fact]
    public async Task NinePartUpload_InteractionDuringPricing_DrainsAllPendingWork()
    {
        var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
        Assert.Equal("2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc",
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true, OwnUploadLifetime = true };
        using var estimates = new BatchEstimateGate();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService(),
            batchEstimates: estimates);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        var url = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        });
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.AddInitScriptAsync("""
            (() => {
              // 0 owner, 1 pointer, 2 capture, 3 bubble, 4 factory entry, 5 factory return, 6 checked mutation, 7 snapshot.
              // Factory return is synchronous and does not claim that scheduled layout has completed.
              const started = performance.now(), rows = [], returned = new WeakMap();
              let expected = null, observed = null, closed = false;
              const owner = root => !!expected && root?.closest('[data-workflow-material-picker]')?.dataset.browserPreviewKey === expected;
              const find = () => [...document.querySelectorAll('[data-material-selector]')].find(owner) ?? null;
              const append = (event, root, original = null) => {
                try {
                  if (closed || expected === null || rows.length >= 32) return;
                  const target = original?.target instanceof Element ? original.target : null;
                  const card = root?.querySelector('[role="radio"][data-material-key="PETG"]');
                  rows.push({ event, owner: owner(root), requested: !!target?.closest('[role="radio"][data-material-key="PETG"]'),
                    checked: card?.getAttribute('aria-checked') === 'true', factoryReturned: returned.get(root) === true,
                    trusted: original?.isTrusted === true, information: !!target?.closest('.material-card__heading button'),
                    connected: root?.isConnected === true, relativeMs: Math.max(0, Math.min(300000, Math.floor(performance.now() - started))) });
                } catch { /* Observation must not change application work. */ }
              };
              const observer = new MutationObserver(records => {
                for (const record of records) {
                  if (record.target.matches('[role="radio"][data-material-key="PETG"]')) append(6, observed);
                }
              });
              const observe = root => {
                if (!owner(root) || observed === root) return;
                observer.disconnect(); observed = root;
                observer.observe(root, { subtree: true, attributes: true, attributeFilter: ['aria-checked'] });
              };
              const capture = (event, code) => {
                try {
                  const root = event.target instanceof Element ? event.target.closest('[data-material-selector]') : null;
                  if (!root) return;
                  observe(root); append(code, root, event);
                } catch { /* Fixed metadata only; never intercept the event. */ }
              };
              const pointer = event => capture(event, 1), click = event => capture(event, 2), bubble = event => capture(event, 3);
              document.addEventListener('pointerdown', pointer, true);
              document.addEventListener('click', click, true);
              document.addEventListener('click', bubble);
              window.source428Trace = {
                track(key) { try { expected = key; const root = find(); observe(root); append(0, root); } catch {} },
                factory(code, root) { try { if (closed) return; returned.set(root, code === 5); observe(root); append(code, root); } catch {} },
                snapshot() { append(7, find()); return rows.slice(); },
                dispose() {
                  closed = true; observer.disconnect(); observed = null; expected = null; rows.length = 0;
                  document.removeEventListener('pointerdown', pointer, true);
                  document.removeEventListener('click', click, true);
                  document.removeEventListener('click', bubble);
                  delete window.source428Trace;
                }
              };
            })();
            """);
        await page.RouteAsync("**/dist/instant-quotation-workflow.mjs", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "text/javascript",
            Body = """
                export * from '/dist/instant-quotation-workflow.mjs?source428-original';
                import { createInstantQuotationWorkflowInterop as actual, createMaterialCardSelector as actualSelector } from '/dist/instant-quotation-workflow.mjs?source428-original';
                export function createMaterialCardSelector(...args) {
                  try { window.source428Trace?.factory(4, args[0]); } catch {}
                  const controller = actualSelector(...args);
                  try { window.source428Trace?.factory(5, args[0]); } catch {}
                  return controller;
                }
                export async function createInstantQuotationWorkflowInterop(...args) {
                  const interop = await actual(...args);
                  const state = window.source428 = { pending: [], tasks: [], closed: false };
                  return Object.freeze({ ...interop, getGeometryClaim(key) {
                    const task = interop.getGeometryClaim(key).then(claim => {
                      if (state.closed) return claim;
                      return new Promise(resolve => state.pending.push({ key, release: () => resolve(claim) }));
                    });
                    state.tasks.push(task);
                    task.catch(() => {});
                    return task;
                  } });
                }
                """,
        }));
        Exception? primary = null;
        Task? callbackDrain = null;
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await WaitForBatchInputAsync(page);
            await page.Locator("[data-workflow-gltf-unit]").SelectOptionAsync("millimetres");
            await page.SetInputFilesAsync("#instant-quote-files", Enumerable.Range(0, 9).Select(index => new FilePayload
            {
                Name = $"partial-copy-{index}.glb",
                MimeType = "model/gltf-binary",
                Buffer = bytes,
            }));
            await page.WaitForFunctionAsync("() => window.source428?.pending.length === 9", null,
                new PageWaitForFunctionOptions { Timeout = 60000 });
            await Assertions.Expect(page.Locator("article[data-workflow-pending-parse]")).ToHaveCountAsync(9);
            var keys = await page.Locator("article[data-workflow-pending-parse]").EvaluateAllAsync<string[]>(
                """
                rows => {
                  const keys = rows.map(row => row.dataset.browserPreviewKey);
                  try { window.source428Trace?.track(keys[8]); } catch {}
                  return keys;
                }
                """);
            Assert.Equal(9, keys.Distinct(StringComparer.Ordinal).Count());
            await ViewBatchKeyAsync(page, keys[8]);
            await MaterialCardBrowserActions.SelectAsync(page, "PETG");
            await page.EvaluateAsync("""
                keys => {
                  for (const key of keys.slice(0, 4)) {
                    const index = window.source428.pending.findIndex(item => item.key === key);
                    if (index < 0) throw new Error('The selected callback is not pending.');
                    window.source428.pending.splice(index, 1)[0].release();
                  }
                }
                """, keys);
            try
            {
                // This is an actual trusted producer call, not merely a held fixture promise.
                await estimates.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.False(estimates.Release.Task.IsCompleted);
                Assert.True(estimates.Active > 0);
                for (var index = 0; index < 5; index++)
                {
                    await ViewBatchKeyAsync(page, keys[index]);
                    // Match the source's rapid clicks while the producer is held; acceptance is asserted after release.
                    var picker = page.Locator("[data-workflow-material-picker] [data-material-selector]");
                    await picker.Locator("[role='radio'][data-material-key='PETG']").ClickAsync();
                    await picker.Locator("[role='radio'][data-material-key='PLA']").ClickAsync();
                    await Assertions.Expect(page.Locator("[data-pricing-loading-status]").First).ToBeVisibleAsync();
                }
                await page.Locator("[data-workflow-material-picker] input[name=quantity]").FillAsync("7");
                await ViewBatchKeyAsync(page, keys[8]);
                await page.EvaluateAsync("() => window.source428.pending.splice(0).forEach(item => item.release())");
            }
            finally { estimates.Release.TrySetResult(); }
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions > button");
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            await Assertions.Expect(page.Locator("[data-workflow-pending-parse]")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("[data-workflow-local-settings]")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("[data-pricing-loading-status]")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("[data-workflow-order-summary]")).ToHaveAttributeAsync("aria-busy", "false");
            await Assertions.Expect(page.Locator($"article[data-browser-preview-key='{keys[8]}'] button[aria-label^='View']"))
                .ToHaveAttributeAsync("aria-pressed", "true");
            Assert.Equal(0, estimates.Active);
            using var scope = factory.Services.CreateScope();
            var session = (await scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>()
                .GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            Assert.Equal(9, session.Parts.Count);
            Assert.Equal(7, session.Parts.Single(part => part.DisplayFileName == "partial-copy-4.glb").Configuration.Quantity);
            Assert.All(session.Parts, part => Assert.Equal(
                part.DisplayFileName == "partial-copy-8.glb" ? "PETG" : "PLA", part.Configuration.MaterialKey));
            await AssertActualBatchAuthorityAsync(scope.ServiceProvider, upload, session, estimates.LatestQuote!, 9);
            Assert.Empty(errors);
            await CaptureBatchMaterialTraceAsync(page);
        }
        catch (Exception exception)
        {
            primary = exception;
            await CaptureBatchMaterialTraceAsync(page);
            output.WriteLine("Batch exception kinds: " + string.Join(';', factory.ExceptionKinds));
            throw;
        }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () =>
                {
                    callbackDrain = page.EvaluateAsync("""
                        async () => {
                          const state = window.source428;
                          if (!state) return;
                          state.closed = true;
                          state.pending.splice(0).forEach(item => item.release());
                          await Promise.allSettled(state.tasks);
                          state.tasks.length = 0;
                        }
                        """);
                    _ = callbackDrain.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    await callbackDrain.WaitAsync(TimeSpan.FromSeconds(5));
                },
                () => { estimates.StopAdmission(); estimates.Release.TrySetResult(); return ValueTask.CompletedTask; },
                () => { upload.StopUploadAdmission(); return ValueTask.CompletedTask; },
                async () =>
                {
                    try { await StopBatchMaterialTraceAsync(page); }
                    finally { await CloseOwnedPageAsync(page, playwright); }
                },
                async () =>
                {
                    if (callbackDrain is null) return;
                    try { await callbackDrain.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (PlaywrightException) when (page.IsClosed && callbackDrain.IsCompleted) { }
                },
                async () => await upload.DrainUploadsAsync(),
                () => { upload.DisposeUploadLifetime(); return ValueTask.CompletedTask; },
                async () => await estimates.DrainAsync(primary),
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                async () => await estimates.CancelAndDrainRemainingAsync(),
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; },
                () => { estimates.Dispose(); return ValueTask.CompletedTask; });
        }
    }

    [Theory]
    [InlineData(1440, "en")]
    [InlineData(390, "th")]
    public async Task ScrolledParts_KeepAddFilesAccessibleAndRevealActualTenthPart(int width, string culture)
    {
        var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true, OwnUploadLifetime = true };
        using var estimates = new BatchEstimateGate();
        estimates.Release.TrySetResult();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService(),
            batchEstimates: estimates);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 1000 },
        });
        Exception? primary = null;
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await WaitForBatchInputAsync(page);
            FilePayload Copy(int index) => new() { Name = $"add-copy-{index}.glb", MimeType = "model/gltf-binary", Buffer = bytes };
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions > button");
            await page.Locator("[data-workflow-gltf-unit]").SelectOptionAsync("millimetres");
            await page.SetInputFilesAsync("#instant-quote-files", Copy(0));
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            await page.SetInputFilesAsync("#instant-quote-files", Enumerable.Range(1, 8).Select(Copy));
            await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(9, new() { Timeout = 90000 });
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            var rail = page.Locator("[data-workflow-part-list]");
            if (width < 992)
            {
                await page.Locator("[data-workflow-part]").Last.Locator("button[aria-pressed]").ClickAsync();
                await page.WaitForFunctionAsync("() => window.scrollY > 0");
            }
            else await rail.EvaluateAsync("element => { element.scrollLeft = element.scrollWidth; element.scrollTop = element.scrollHeight; }");
            var add = page.Locator("[data-workflow-add-file]");
            await Assertions.Expect(add).ToBeInViewportAsync();
            var chooser = await page.RunAndWaitForFileChooserAsync(() => add.ClickAsync());
            await chooser.SetFilesAsync(Copy(9));
            await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(10, new() { Timeout = 90000 });
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            var newest = page.Locator("[data-workflow-part]").Filter(new() { Has = page.Locator("h4", new() { HasText = "add-copy-9.glb" }) });
            await Assertions.Expect(newest.Locator("button[aria-pressed]"))
                .ToHaveAttributeAsync("aria-pressed", "true");
            await page.WaitForFunctionAsync("""
                () => {
                  const rail = document.querySelector('[data-workflow-part-list]');
                  const tile = [...rail.querySelectorAll('[data-workflow-part]')].find(element => element.querySelector('h4')?.textContent === 'add-copy-9.glb');
                  if (!tile) return false;
                  const item = tile.getBoundingClientRect(), bounds = rail.getBoundingClientRect();
                  return item.left >= bounds.left - 1 && item.right <= bounds.right + 1
                    && item.top >= bounds.top - 1 && item.bottom <= bounds.bottom + 1;
                }
                """);
            await Assertions.Expect(add).ToBeInViewportAsync();
            using var scope = factory.Services.CreateScope();
            var session = (await scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>()
                .GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            await AssertActualBatchAuthorityAsync(scope.ServiceProvider, upload, session, estimates.LatestQuote!, 10);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { estimates.StopAdmission(); estimates.Release.TrySetResult(); return ValueTask.CompletedTask; },
                () => { upload.StopUploadAdmission(); return ValueTask.CompletedTask; },
                async () => await CloseOwnedPageAsync(page, playwright),
                async () => await upload.DrainUploadsAsync(),
                () => { upload.DisposeUploadLifetime(); return ValueTask.CompletedTask; },
                async () => await estimates.DrainAsync(primary),
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                async () => await estimates.CancelAndDrainRemainingAsync(),
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; },
                () => { estimates.Dispose(); return ValueTask.CompletedTask; });
        }
    }

    private static Task WaitForBatchInputAsync(IPage page) => page.WaitForFunctionAsync("""
        () => {
          const input = document.querySelector('#instant-quote-files');
          return input && typeof input._blazorInputFileNextFileId === 'number';
        }
        """);

    private static async Task ViewBatchKeyAsync(IPage page, string key)
    {
        await page.Locator($"article[data-browser-preview-key='{key}'] button[aria-label^='View']").ClickAsync();
        await page.WaitForFunctionAsync("""
            key => {
              const row = document.querySelector('article[data-browser-preview-key="' + key + '"]');
              const picker = document.querySelector('[data-workflow-material-picker]');
              return picker?.dataset.browserPreviewKey === key
                || !!row?.dataset.partId && picker?.querySelector('input[name=quantity]')?.id === 'quantity-' + row.dataset.partId;
            }
            """, key);
    }

    private static async Task AssertActualBatchAuthorityAsync(IServiceProvider services, HashCheckingUploadClient upload,
        InstantQuotationSessionState session, InstantQuotationOrderQuote quote, int count)
    {
        var rawSha = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb)));
        Assert.Equal(count, upload.VerifiedUploads);
        Assert.Equal(count, session.Parts.Count);
        Assert.Equal(count, session.Parts.Select(part => part.PartId).Distinct().Count());
        Assert.Equal(count, session.Parts.Select(part => part.PhysicalAnalysisUpload!.FileId).Distinct().Count());
        var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(session.QuoteAuthorization);
        Assert.Equal(count, authorization.LineTickets.Count);
        var tickets = services.GetRequiredService<IInstantQuotationQuoteTicketService>();
        Assert.True(tickets.Validate(session, quote, authorization, DateTimeOffset.UtcNow));
        Assert.False(tickets.Validate(session with { OwnerIdentity = "different-controlled-owner" }, quote, authorization, DateTimeOffset.UtcNow));
        Assert.False(tickets.Validate(session with { SessionId = "different-controlled-session" }, quote, authorization, DateTimeOffset.UtcNow));
        var meshes = new InstantQuotationAdmittedMeshService(services.GetRequiredService<IInstantQuotationPhysicalAnalysisInputReader>());
        foreach (var part in session.Parts)
        {
            var file = Assert.IsType<InstantQuotationPhysicalAnalysisUpload>(part.PhysicalAnalysisUpload);
            Assert.Equal(rawSha, part.Geometry.Sha256);
            Assert.Equal(rawSha, file.Sha256);
            var mesh = await meshes.ReadAsync(session.SessionId, upload.OwnerIdentity, part.PartId, file.FileId, rawSha, default);
            Assert.True(mesh.IsReady);
            Assert.Equal(12, mesh.Mesh!.Triangles.Count);
            var receipt = Assert.Single(session.PhysicalReceipts!, item => item.PartId == part.PartId
                && item.MaterialKey == part.Configuration.MaterialKey);
            Assert.Equal(file.FileId, receipt.FileId);
            Assert.Equal(rawSha, receipt.UploadSha256);
            Assert.Equal(session.SessionId, receipt.SessionId);
            Assert.Equal(upload.OwnerIdentity, receipt.OwnerIdentity);
            Assert.Equal(part.Configuration.Quantity, receipt.Quantity);
            Assert.Equal(1, receipt.BoundingCm3PerUnit, 3);
            Assert.True(receipt.DepositedMm3 > 0);
            Assert.True(receipt.MotionSeconds > 0);
        }
        Assert.False((await meshes.ReadAsync(session.SessionId, upload.OwnerIdentity, session.Parts[0].PartId,
            session.Parts[1].PhysicalAnalysisUpload!.FileId, rawSha, default)).IsReady);
    }

    private sealed class BatchEstimateGate : IDisposable
    {
        private readonly object synchronization = new();
        private readonly List<(Task<InstantQuotationOrderQuote?> Task, CancellationToken Consumer)> actual = [];
        private readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(5));
        private readonly CancellationTokenSource holdLease = new(TimeSpan.FromSeconds(90));
        private bool stopped;
        private int active;
        internal int Active => Volatile.Read(ref active);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private InstantQuotationOrderQuote? latestQuote;
        internal InstantQuotationOrderQuote? LatestQuote => Volatile.Read(ref latestQuote);

        internal void Decorate(IServiceCollection services)
        {
            var original = services.Single(item => item.ServiceType == typeof(IInstantQuotationAuthoritativePricingService));
            services.RemoveAll<IInstantQuotationAuthoritativePricingService>();
            services.Add(new ServiceDescriptor(typeof(IInstantQuotationAuthoritativePricingService), provider =>
                new Pricing((IInstantQuotationAuthoritativePricingService)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)), this), original.Lifetime));
        }

        private Task<InstantQuotationOrderQuote?> Start(IInstantQuotationAuthoritativePricingService inner,
            InstantQuotationSessionState session, string? owner, bool comparisons,
            Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask>? observer, CancellationToken consumer)
        {
            TaskCompletionSource<InstantQuotationOrderQuote?> completion;
            lock (synchronization)
            {
                if (stopped) return Task.FromCanceled<InstantQuotationOrderQuote?>(new CancellationToken(canceled: true));
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual.Add((completion.Task, consumer));
            }
            _ = completion.Task.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = CompleteAsync();
            return completion.Task;

            async Task CompleteAsync()
            {
                InstantQuotationOrderQuote? quote = null;
                Exception? failure = null;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(consumer, lifetime.Token))
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        Entered.TrySetResult();
                        using (var hold = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, holdLease.Token))
                            await Release.Task.WaitAsync(hold.Token);
                        quote = observer is null ? await inner.QuoteAsync(session, owner, comparisons, linked.Token)
                            : await inner.QuoteAsync(session, owner, comparisons, observer, linked.Token);
                        if (quote is not null) Volatile.Write(ref latestQuote, quote);
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { Interlocked.Decrement(ref active); }
                }
                if (failure is OperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
                else if (failure is not null) completion.TrySetException(failure);
                else completion.TrySetResult(quote);
            }
        }

        internal void StopAdmission() { lock (synchronization) stopped = true; }
        internal async Task DrainAsync(Exception? primary)
        {
            (Task<InstantQuotationOrderQuote?> Task, CancellationToken Consumer)[] snapshot;
            lock (synchronization) snapshot = actual.ToArray();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary, snapshot.Select(item =>
                (Func<ValueTask>)(async () => await MaterialPricingTestCleanup.DrainQuoteAsync(item.Task,
                    static () => { }, () => Release.TrySetResult(), item.Consumer, null,
                    receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                    afterWaitFailure: lifetime.Cancel, taskKind: "actual-partial-batch-price"))).ToArray());
        }
        internal async Task CancelAndDrainRemainingAsync()
        {
            Task<InstantQuotationOrderQuote?>[] snapshot;
            lock (synchronization) snapshot = actual.Select(item => item.Task).ToArray();
            if (snapshot.Any(task => !task.IsCompleted)) lifetime.Cancel();
            try { await Task.WhenAll(snapshot).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (snapshot.All(task => task.IsCompleted)) { }
        }

        public void Dispose()
        {
            lock (synchronization)
            {
                if (actual.Any(item => !item.Task.IsCompleted)) return;
                holdLease.Dispose();
                lifetime.Dispose();
            }
        }
        private sealed class Pricing(IInstantQuotationAuthoritativePricingService inner, BatchEstimateGate owner)
            : IInstantQuotationAuthoritativePricingService
        {
            public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
                string? identity, bool comparisons, CancellationToken token) => owner.Start(inner, session, identity, comparisons, null, token);
            public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
                string? identity, bool comparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
                CancellationToken token) => owner.Start(inner, session, identity, comparisons, observer, token);
        }
    }
}
