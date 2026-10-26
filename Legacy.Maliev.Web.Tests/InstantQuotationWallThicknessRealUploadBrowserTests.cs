using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationWallThicknessRealUploadBrowserTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealFdmUpload_TouchScrollRetainsPetgSelectionAcrossOwnedRepricing()
    {
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true };
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        factory.UseKestrel(origin.Port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        var url = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        using var readiness = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 375, Height = 844 },
            HasTouch = true,
            IsMobile = true,
        });
        await using var page = await context.NewPageAsync();
        using var lease = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ICDPSession? cdp = null;
        Exception? primary = null;
        try
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.CountAsync() > 0) await consent.ClickAsync();
            await WaitForUploadInputReadyAsync(page);
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "touch-existing-box.stl",
                MimeType = "model/stl",
                Buffer = WallThicknessParityBrowserFixture.Box(5),
            });
            await page.WaitForFunctionAsync(
                "() => document.querySelectorAll('[data-workflow-part]').length === 1 && !!document.querySelector('[data-workflow-price-tier]')",
                null, new() { Timeout = 60_000 });
            var id = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
            var scroller = page.Locator("[data-material-card-scroll]");
            var petg = scroller.Locator("[data-material-key='PETG']");
            await petg.TapAsync();
            await Assertions.Expect(petg).ToHaveAttributeAsync("aria-checked", "true");
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await Assertions.Expect(review).ToBeEnabledAsync();
            await Assertions.Expect(petg.Locator("[data-material-card-price]"))
                .ToHaveAttributeAsync("data-price-status", "completed");
            var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
            var before = Assert.IsType<InstantQuotationSessionState>(await store.GetAsync(upload.SessionId, upload.OwnerIdentity, lease.Token));
            AssertCurrentReceipt(before, id, 1);
            var originalPart = Assert.Single(before.Parts);
            await scroller.ScrollIntoViewIfNeededAsync(); // Only position the outer page; the catalogue gesture below is trusted touch.
            const string geometry = """
                node => {
                    const card = node.querySelector('[data-material-key="PETG"]');
                    const list = node.getBoundingClientRect(), bounds = card.getBoundingClientRect();
                    return [node.scrollTop, node.scrollHeight, node.clientHeight, list.top, list.bottom,
                        bounds.top, bounds.bottom, window.scrollY];
                }
                """;
            await page.WaitForFunctionAsync("""
                () => {
                    const node = document.querySelector('[data-material-card-scroll]');
                    const card = node.querySelector('[data-material-key="PETG"]');
                    const list = node.getBoundingClientRect(), bounds = card.getBoundingClientRect();
                    return bounds.top >= list.top - 1 && bounds.bottom <= list.bottom + 1;
                }
                """, null, new() { Timeout = 3000 });
            var initial = await scroller.EvaluateAsync<double[]>(geometry);
            var down = Math.Min(initial[5] - initial[3] - 8, initial[1] - initial[2] - initial[0] - 8);
            var up = Math.Min(initial[4] - initial[6] - 8, initial[0] - 8);
            var distance = Math.Min(80, Math.Max(down, up));
            Assert.True(distance >= 24, "The selected FDM card must have real room for a meaningful touch scroll.");
            var box = Assert.IsType<LocatorBoundingBoxResult>(await scroller.BoundingBoxAsync());
            var top = Math.Max(0, box.Y);
            var bottom = Math.Min(844, box.Y + box.Height);
            Assert.True(bottom - top >= 100);
            await scroller.EvaluateAsync("""
                node => {
                    window.materialTrustedTouchStarts = 0;
                    node.addEventListener('touchstart', event => {
                        if (event.isTrusted) window.materialTrustedTouchStarts++;
                    }, { passive: true, once: true });
                }
                """);
            cdp = await context.NewCDPSessionAsync(page);
            await cdp.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>
            {
                ["x"] = box.X + box.Width / 2,
                ["y"] = (top + bottom) / 2,
                ["yDistance"] = down >= up ? -distance : distance,
                ["gestureSourceType"] = "touch",
                ["preventFling"] = true,
                ["speed"] = 400,
            });
            await page.WaitForFunctionAsync("""
                start => window.materialTrustedTouchStarts > 0
                    && Math.abs(document.querySelector('[data-material-card-scroll]').scrollTop - start) >= 8
                """, initial[0], new() { Timeout = 3000 });
            var touched = await scroller.EvaluateAsync<double[]>(geometry);
            Assert.InRange(Math.Abs(touched[7] - initial[7]), 0, 1);
            AssertScrollRetained(touched, touched);
            upload.BeforePhysicalRead = async (partId, token) =>
            {
                if (partId != id) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            };
            var quantity = page.Locator($"#quantity-{id}");
            await quantity.FillAsync("2");
            await quantity.PressAsync("Tab");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assertions.Expect(review).ToBeDisabledAsync();
            await Assertions.Expect(petg).ToHaveAttributeAsync("aria-checked", "true");
            AssertScrollRetained(touched, await scroller.EvaluateAsync<double[]>(geometry));
            var pending = Assert.IsType<InstantQuotationSessionState>(await store.GetAsync(upload.SessionId, upload.OwnerIdentity, lease.Token));
            Assert.Equal("PETG", Assert.Single(pending.Parts).Configuration.MaterialKey);
            Assert.Equal(2, Assert.Single(pending.Parts).Configuration.Quantity);
            Assert.Null(pending.QuoteAuthorization);
            release.TrySetResult();
            upload.BeforePhysicalRead = null;
            await Assertions.Expect(review).ToBeEnabledAsync();
            await Assertions.Expect(petg.Locator("[data-material-card-price]"))
                .ToHaveAttributeAsync("data-price-status", "completed");
            AssertScrollRetained(touched, await scroller.EvaluateAsync<double[]>(geometry));
            var current = Assert.IsType<InstantQuotationSessionState>(await store.GetAsync(upload.SessionId, upload.OwnerIdentity, lease.Token));
            AssertCurrentReceipt(current, id, 2);
            Assert.Equal(originalPart.UploadReference, Assert.Single(current.Parts).UploadReference);
            Assert.Equal(originalPart.PhysicalAnalysisUpload, Assert.Single(current.Parts).PhysicalAnalysisUpload);
            Assert.Equal(1, upload.VerifiedUploads);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                () => { release.TrySetResult(); upload.BeforePhysicalRead = null; return ValueTask.CompletedTask; },
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; },
                async () => { if (cdp is not null) await cdp.DetachAsync(); });
        }

        static void AssertScrollRetained(double[] original, double[] current)
        {
            Assert.True(current[5] >= current[3] - 1 && current[6] <= current[4] + 1);
            // Same one-pixel card-relative anchor bound as the existing manual-scroll regression; layout shrink is rebased.
            Assert.True(current[5] - current[3] <= original[5] - original[3] + 1);
        }

        static void AssertCurrentReceipt(InstantQuotationSessionState state, Guid id, int quantity)
        {
            var part = Assert.Single(state.Parts);
            Assert.Equal(id, part.PartId);
            Assert.Equal("PETG", part.Configuration.MaterialKey);
            Assert.Equal(quantity, part.Configuration.Quantity);
            Assert.NotNull(state.QuoteAuthorization);
            var source = Assert.IsType<InstantQuotationPhysicalAnalysisUpload>(part.PhysicalAnalysisUpload);
            var receipt = Assert.Single(state.PhysicalReceipts!, item => item.PartId == id
                && item.MaterialKey == "PETG" && item.ConfiguredMaterialKey == "PETG" && item.Quantity == quantity);
            Assert.Equal(state.SessionId, receipt.SessionId);
            Assert.Equal(state.OwnerIdentity, receipt.OwnerIdentity);
            Assert.Equal(source.FileId, receipt.FileId);
            Assert.Equal(source.Sha256, receipt.UploadSha256, ignoreCase: true);
            Assert.Equal(part.Geometry.Sha256, receipt.UploadSha256, ignoreCase: true);
            Assert.Equal(part.Configuration.BuildPreference, receipt.BuildPreference);
            Assert.True(receipt.DepositedMm3 > 0 && receipt.MotionSeconds > 0);
        }
    }

    [Fact]
    public async Task NineOriginalGlbCopies_SupersededPetgTasksDoNotBlockCurrentSignedPlaBatch()
    {
        var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
        var rawSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal("2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc", rawSha);
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true, OwnUploadLifetime = true };
        using var obsolete = new SupersededPetgPricing();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService(),
            obsolete: obsolete);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        var url = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        using var readiness = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        });
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        Exception? primary = null;
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await page.WaitForFunctionAsync("""
                () => {
                  const input = document.querySelector('#instant-quote-files');
                  return input && typeof input._blazorInputFileNextFileId === 'number';
                }
                """);
            FilePayload Copy(int index) => new()
            {
                Name = $"source-cube-copy-{index:00}.glb",
                MimeType = "model/gltf-binary",
                Buffer = bytes,
            };
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await page.Locator("[data-workflow-gltf-unit]").SelectOptionAsync("millimetres");
            await page.SetInputFilesAsync("#instant-quote-files", Copy(1));
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            await page.SetInputFilesAsync("#instant-quote-files", Enumerable.Range(2, 8).Select(Copy));
            var cards = page.Locator("[data-workflow-part]");
            await Assertions.Expect(cards).ToHaveCountAsync(9, new() { Timeout = 90000 });
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            Assert.Equal(9, upload.VerifiedUploads);
            using var scope = factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
            var before = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            Assert.Equal(9, before.Parts.Count);
            var partIds = before.Parts.Select(part => part.PartId).ToArray();
            Assert.Equal(9, partIds.Distinct().Count());
            Assert.Equal(9, before.Parts.Select(part => part.PhysicalAnalysisUpload!.FileId).Distinct().Count());
            var admitted = new InstantQuotationAdmittedMeshService(
                scope.ServiceProvider.GetRequiredService<IInstantQuotationPhysicalAnalysisInputReader>());
            foreach (var part in before.Parts)
            {
                Assert.Equal("PLA", part.Configuration.MaterialKey);
                Assert.Equal(rawSha, part.Geometry.Sha256);
                var file = Assert.IsType<InstantQuotationPhysicalAnalysisUpload>(part.PhysicalAnalysisUpload);
                Assert.Equal(rawSha, file.Sha256);
                Assert.Equal(file.FileId.ToString("D"), part.UploadReference.Value);
                var mesh = await admitted.ReadAsync(before.SessionId, upload.OwnerIdentity, part.PartId,
                    file.FileId, rawSha, default);
                Assert.True(mesh.IsReady);
                Assert.Equal(rawSha, mesh.UploadSha256);
                Assert.Equal(12, mesh.Mesh!.Triangles.Count);
            }

            var wrongFile = before.Parts[1].PhysicalAnalysisUpload!.FileId;
            Assert.False((await admitted.ReadAsync(before.SessionId, upload.OwnerIdentity,
                before.Parts[0].PartId, wrongFile, rawSha, default)).IsReady);

            obsolete.Arm(partIds.Take(5));
            foreach (var partId in partIds.Take(5))
            {
                var card = page.Locator($"[data-workflow-part][data-part-id='{partId:D}']");
                await card.Locator("button[aria-label^='View']").ClickAsync();
                await page.WaitForFunctionAsync(
                    "id => document.querySelector('[data-workflow-material-picker] input[name=quantity]')?.id === 'quantity-' + id",
                    partId.ToString("D"));
                await MaterialCardBrowserActions.SelectAsync(page, "PETG");
                await obsolete.EnteredAsync(partId).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(await page.Locator("[data-workflow-material-picker]").EvaluateAsync<bool>(
                    "element => element.matches(':disabled') || !!element.closest('[inert]')"));
                await Assertions.Expect(page.Locator(
                    "[data-workflow-material-picker] [role=radio][data-material-key=PLA]")).ToBeEnabledAsync();
                await MaterialCardBrowserActions.SelectAsync(page, "PLA");
            }
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            await Assertions.Expect(page.Locator("[data-pricing-loading-status]")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("[data-workflow-order-summary]")).ToHaveAttributeAsync("aria-busy", "false");
            Assert.Equal(0, obsolete.ActiveCurrent);
            var held = obsolete.Snapshot();
            Assert.Equal(5, held.Length);
            Assert.Equal(partIds.Take(5).OrderBy(id => id), held.Select(item => item.PartId).OrderBy(id => id));
            Assert.All(held, item =>
            {
                Assert.True(item.ConsumerToken.IsCancellationRequested);
                Assert.NotNull(item.Actual);
                Assert.False(item.Actual!.IsCompleted);
            });
            var current = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            Assert.Equal(9, current.Parts.Count);
            Assert.All(current.Parts, part => Assert.Equal("PLA", part.Configuration.MaterialKey));
            var quote = Assert.IsType<InstantQuotationOrderQuote>(obsolete.CurrentPlaQuote);
            var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(current.QuoteAuthorization);
            Assert.Equal(9, authorization.LineTickets.Count);
            Assert.Equal(9, authorization.LineTickets.Distinct(StringComparer.Ordinal).Count());
            var tickets = scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>();
            Assert.True(tickets.Validate(current, quote, authorization, DateTimeOffset.UtcNow));
            Assert.False(tickets.Validate(current with { SessionId = "different-controlled-session" },
                quote, authorization, DateTimeOffset.UtcNow));
            Assert.False(tickets.Validate(current with { OwnerIdentity = "different-controlled-owner" },
                quote, authorization, DateTimeOffset.UtcNow));
            foreach (var part in current.Parts)
            {
                var receipt = Assert.Single(current.PhysicalReceipts!, value =>
                    value.PartId == part.PartId && value.MaterialKey == "PLA");
                Assert.Equal(part.PhysicalAnalysisUpload!.FileId, receipt.FileId);
                Assert.Equal(current.SessionId, receipt.SessionId);
                Assert.Equal(upload.OwnerIdentity, receipt.OwnerIdentity);
                Assert.Equal(rawSha, receipt.UploadSha256);
                Assert.Equal(part.Configuration.Quantity, receipt.Quantity);
                Assert.Equal(1, receipt.BoundingCm3PerUnit, 3);
                Assert.True(receipt.DepositedMm3 > 0);
                Assert.True(receipt.MotionSeconds > 0);
            }
            var currentTickets = authorization.LineTickets.ToArray();
            var protectedBefore = System.Text.Json.JsonSerializer.Serialize(current);
            obsolete.ReleaseAll();
            await obsolete.DrainAsync(null);
            Assert.All(held, item => Assert.Equal(TaskStatus.RanToCompletion, item.Actual!.Status));
            var after = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            Assert.Equal(currentTickets.AsEnumerable(), after.QuoteAuthorization!.LineTickets);
            Assert.True(protectedBefore == System.Text.Json.JsonSerializer.Serialize(after));
            await Assertions.Expect(review).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("[data-pricing-loading-status]")).ToHaveCountAsync(0);
            Assert.Empty(pageErrors);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { obsolete.StopAdmission(); obsolete.ReleaseAll(); return ValueTask.CompletedTask; },
                () => { upload.StopUploadAdmission(); return ValueTask.CompletedTask; },
                async () => await CloseOwnedPageAsync(page, playwright),
                async () => await upload.DrainUploadsAsync(),
                () => { upload.DisposeUploadLifetime(); return ValueTask.CompletedTask; },
                async () => await obsolete.DrainAsync(primary),
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                async () => await obsolete.CancelAndDrainRemainingAsync(),
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; },
                () => { obsolete.Dispose(); return ValueTask.CompletedTask; });
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task RemovingPendingLocalParser_UnblocksCurrentProtectedQuoteBeforeActualReadReturns(string culture)
    {
        var upload = new HashCheckingUploadClient();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService());
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
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await using var failureEvidence = new BrowserFailureEvidence(page,
            metadata => output.WriteLine("[real-upload-evidence-cleanup] " + metadata));
        await page.AddInitScriptAsync("""
            (() => {
              const original = File.prototype.arrayBuffer;
              let release;
              let reject;
              const gate = new Promise((resolve, fail) => { release = resolve; reject = fail; });
              gate.catch(() => {});
              const state = { entered: 0, completed: 0 };
              let timeout = null;
              state.release = () => { clearTimeout(timeout); release(); };
              state.restore = () => { state.release(); File.prototype.arrayBuffer = original; };
              File.prototype.arrayBuffer = async function () {
                if (this.name !== 'pending-local-parse.stl') return original.call(this);
                state.entered++;
                timeout ??= setTimeout(() => reject(new Error('Controlled parser lease expired.')), 30000);
                try { await gate; return await original.call(this); }
                finally { state.completed++; }
              };
              window.__pendingLocalParser = state;
            })();
            """);
        Exception? primary = null;
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await page.WaitForFunctionAsync("""
                () => {
                  const input = document.querySelector('#instant-quote-files');
                  return input && typeof input._blazorInputFileNextFileId === 'number';
                }
                """);
            var bytes = WallThicknessParityBrowserFixture.Box(5);
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "healthy-protected.stl",
                MimeType = "model/stl",
                Buffer = bytes,
            });
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await Assertions.Expect(review).ToBeEnabledAsync();
            var partId = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
            await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(partId);
            var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
            var before = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            Assert.NotNull(before.QuoteAuthorization);
            var receipt = Assert.Single(before.PhysicalReceipts!,
                row => row.MaterialKey == Assert.Single(before.Parts).Configuration.MaterialKey);
            Assert.Equal(partId, receipt.PartId);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), receipt.UploadSha256);
            Assert.True(receipt.MotionSeconds > 0);
            var protectedBefore = System.Text.Json.JsonSerializer.Serialize(before);

            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "pending-local-parse.stl",
                MimeType = "model/stl",
                Buffer = bytes,
            });
            var pending = page.Locator("[data-workflow-pending-parse]");
            await Assertions.Expect(pending).ToHaveCountAsync(1);
            await page.WaitForFunctionAsync("() => window.__pendingLocalParser.entered > 0");
            await Assertions.Expect(review).ToBeDisabledAsync();
            Assert.Equal(0, await page.EvaluateAsync<int>("window.__pendingLocalParser.completed"));
            await pending.Locator("[data-workflow-remove-pending]").ClickAsync();
            await Assertions.Expect(pending).ToHaveCountAsync(0);
            await Assertions.Expect(review).ToBeEnabledAsync();
            Assert.Equal(0, await page.EvaluateAsync<int>("window.__pendingLocalParser.completed"));
            Assert.Equal(1, upload.VerifiedUploads);
            await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(1);
            var current = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            Assert.True(protectedBefore == System.Text.Json.JsonSerializer.Serialize(current));

            await page.EvaluateAsync("window.__pendingLocalParser.release()");
            await page.WaitForFunctionAsync("""
                () => window.__pendingLocalParser.completed === window.__pendingLocalParser.entered
                  && window.__pendingLocalParser.entered > 0
                """);
            await Assertions.Expect(page.Locator("[data-workflow-failed-preview]")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(1);
            Assert.Equal(partId.ToString(), await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"));
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.True(protectedBefore == System.Text.Json.JsonSerializer.Serialize(
                (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!));
            await Assertions.Expect(review).ToBeEnabledAsync();
        }
        catch (Exception exception)
        {
            primary = exception;
            await ObserveRealUploadFailureAsync(exception, failureEvidence, page, factory, upload);
            throw;
        }
        finally
        {
            try
            {
                await page.EvaluateAsync("window.__pendingLocalParser?.restore()");
                await page.WaitForFunctionAsync("""
                    () => !window.__pendingLocalParser
                      || window.__pendingLocalParser.completed === window.__pendingLocalParser.entered
                    """);
            }
            catch when (primary is not null) { /* Preserve the first browser failure while context disposal drains owned work. */ }
        }
    }

    [Theory]
    [InlineData("desktop", "model/x.stl-ascii")]
    [InlineData("iphone", "application/sla")]
    [InlineData("ipad", "application/vnd.ms-pki.stl")]
    public async Task MobileCadPickerPreservesAllowlistAndRejectsPdfBeforeRealStlUpload(
        string platform, string contentType)
    {
        const string expectedTypes = ".stl,.obj,.3mf,.glb,.gltf,.stp,.step,.igs,.iges,model/stl,application/sla,application/vnd.ms-pki.stl,model/x.stl-binary,model/x.stl-ascii,model/obj,model/3mf,model/gltf-binary,model/gltf+json,model/step,model/iges";
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            HasTouch = platform != "desktop",
            IsMobile = platform != "desktop",
            UserAgent = platform == "iphone"
                ? "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1"
                : "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Safari/605.1.15",
        });
        if (platform == "ipad")
        {
            await context.AddInitScriptAsync("""
                Object.defineProperty(navigator, 'platform', { configurable: true, get: () => 'MacIntel' });
                Object.defineProperty(navigator, 'maxTouchPoints', { configurable: true, get: () => 2 });
                """);
        }
        await using var page = await context.NewPageAsync();
        await using var failureEvidence = new BrowserFailureEvidence(page,
            metadata => output.WriteLine("[mobile-evidence-cleanup] " + metadata));
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(quoteUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }
        await page.WaitForFunctionAsync(
            """
            () => {
              const zone = document.querySelector('.instant-quote__workflow');
              const input = document.querySelector('#instant-quote-files');
              if (!zone || !input || input._blazorInputFileNextFileId === undefined
                  || input.disabled || zone.getAttribute('aria-busy') === 'true') return false;
              const transfer = new DataTransfer();
              transfer.items.add(new File([], 'picker-readiness.stl', { type: 'model/stl' }));
              const probe = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: transfer });
              return !zone.dispatchEvent(probe) && probe.defaultPrevented;
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        var input = page.Locator("#instant-quote-files");
        if (platform == "desktop")
        {
            Assert.Equal(expectedTypes, await input.GetAttributeAsync("accept"));
        }
        else
        {
            Assert.Null(await input.GetAttributeAsync("accept"));
            Assert.Equal(expectedTypes, await input.GetAttributeAsync("data-accepted-types"));
        }

        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "drawing.pdf",
            MimeType = "application/pdf",
            Buffer = System.Text.Encoding.UTF8.GetBytes("%PDF-1.7 unsupported model"),
        });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-workflow-preview-status]')?.textContent
                .includes('3D preview is unavailable.')
              && document.querySelector('.instant-quote__workflow')?.getAttribute('aria-busy') === 'false'
            """,
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        Assert.Equal(0, upload.StartedUploads);
        Assert.Equal(0, upload.VerifiedUploads);
        Assert.Equal(0, upload.FinalizeCalls);
        Assert.Equal(0, await page.Locator("[data-workflow-part]").CountAsync());
        Assert.Equal(0, await page.Locator("[data-workflow-review-part]").CountAsync());

        await WaitForUploadInputReadyAsync(page);
        var bytes = WallThicknessParityBrowserFixture.Box(5);
        var expectedDigest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "mobile-picker.stl",
            MimeType = contentType,
            Buffer = bytes,
        });
        failureEvidence.Stage("UploadAdmission");
        try
        {
            // Upload admission is not a configuration gesture: explicitly accept the initial color.
            await page.Locator("[data-workflow-configuration] select[id^='color-']").SelectOptionAsync(
                "Black", new LocatorSelectOptionOptions { Timeout = 30_000 });
            // A rejected file remains actionable and blocks review until the customer removes it.
            var rejectedPdf = page.Locator("[data-workflow-failed-preview]").Filter(
                new LocatorFilterOptions { HasText = "drawing.pdf" });
            await Microsoft.Playwright.Assertions.Expect(rejectedPdf).ToHaveCountAsync(1);
            await Microsoft.Playwright.Assertions.Expect(page.Locator(
                "[data-workflow-configuration] .instant-quote__configuration-actions button")).ToBeDisabledAsync();
            await rejectedPdf.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
            {
                Name = "Remove drawing.pdf",
                Exact = true,
            }).ClickAsync();
            await Microsoft.Playwright.Assertions.Expect(rejectedPdf).ToHaveCountAsync(0);
            await page.WaitForFunctionAsync(
                """
                () => document.querySelectorAll('[data-workflow-parts] > [data-workflow-part]').length === 1
                  && document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)') !== null
                  && document.querySelector('.instant-quote__workflow')?.getAttribute('aria-busy') === 'false'
                """,
                null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        }
        catch (Exception admissionFailure)
        {
            await failureEvidence.AttachAsync(admissionFailure, () => page.EvaluateAsync<string>("""
                () => {
                  const zone = document.querySelector(".instant-quote__workflow");
                  const workflow = zone?.dataset.workflowState;
                  return JSON.stringify({
                    workflow:["empty","uploading","uploaded","error","multipart","configured","review","customerdetails","submitted"].includes(workflow) ? workflow : "unavailable",
                    parts:Math.min(document.querySelectorAll("[data-workflow-parts] > [data-workflow-part]").length,32),
                    uploadRows:Math.min(document.querySelectorAll("[data-workflow-upload-item]").length,32),
                    progress:zone?.getAttribute("aria-busy") === "true",
                    configurationEnabled:document.querySelector("[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)") !== null,
                    isRepricing:!!document.querySelector("[data-pricing-loading-status]"),
                    errorVisible:document.querySelector("#blazor-error-ui")?.style.display === "block"
                  });
                }
                """), metadata => output.WriteLine("[mobile-upload-admission] " + metadata));
            try
            {
                output.WriteLine($"[mobile-upload-services] started={Math.Min(upload.StartedUploads, 32)}; verified={Math.Min(upload.VerifiedUploads, 32)}; validationRejected={Math.Min(upload.ValidationRejectedUploads, 32)}");
            }
            catch { /* Secondary evidence must preserve the original upload admission failure. */ }
            throw;
        }
        Assert.Equal(1, upload.StartedUploads);
        Assert.Equal(1, upload.VerifiedUploads);
        Assert.Equal(0, upload.ValidationRejectedUploads);
        var partId = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
        await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(partId);
        var session = await factory.Services.GetRequiredService<IInstantQuotationSessionStore>()
            .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(session);
        var part = Assert.Single(session.Parts);
        Assert.Equal(partId, part.PartId);
        Assert.Equal("mobile-picker.stl", part.DisplayFileName);
        Assert.Equal(expectedDigest, part.Geometry.Sha256);
        Assert.NotNull(part.PhysicalAnalysisUpload);
        Assert.Equal(contentType, part.PhysicalAnalysisUpload.ContentType);
        Assert.Equal("mobile-picker.stl", part.PhysicalAnalysisUpload.FileName);
        var original = upload.Read(upload.SessionId, upload.OwnerIdentity, part.PhysicalAnalysisUpload.FileId);
        Assert.Equal(bytes, original.Content);
        Assert.NotNull(session.QuoteAuthorization);
        Assert.Contains(session.PhysicalReceipts!, receipt => receipt.PartId == partId
            && receipt.UploadSha256 == expectedDigest && receipt.MotionSeconds > 0);
        await page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-workflow-review-part]")).ToHaveCountAsync(1);
        Assert.Contains("mobile-picker.stl",
            await page.Locator("[data-workflow-review-part]").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(0, upload.FinalizeCalls);
        Assert.Empty(pageErrors);
    }

    [Theory]
    [InlineData(320, "th")]
    [InlineData(375, "en")]
    public async Task UploadedPartShowsOnlyPhysicallyVerifiedQuantityAndReviewActionsReachable(int width, string culture)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        var comparisonObservation = new MaterialCompletionObservation();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing, comparisonObservation);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.True(readiness.StatusCode == HttpStatusCode.OK,
            await readiness.Content.ReadAsStringAsync());

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 700 },
            HasTouch = true,
            IsMobile = true,
        });
        await using var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateBoxStl(20, 20, 5);
        try
        {
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", path);
            await page.Locator("[data-workflow-price-tier]").Last.WaitForAsync();
            var viewerControlsFit = await page.Locator("[data-workflow-viewer] > button:not(.instant-quote__thickness-toggle)")
                .EvaluateAllAsync<bool>("""
                    buttons => {
                      const thickness = document.querySelector('.instant-quote__thickness-toggle').getBoundingClientRect();
                      return buttons.length === 3 && buttons.every(button => {
                        const box = button.getBoundingClientRect();
                        return box.width >= 88 && box.height >= 44 && box.right < thickness.left - 4
                          && button.scrollWidth <= button.clientWidth + 1
                          && button.scrollHeight <= button.clientHeight + 1;
                      });
                    }
                    """);
            Assert.True(viewerControlsFit, "Localized viewer controls must not collapse into vertical text columns.");
            var tiers = page.Locator("[data-workflow-price-tier]");
            Assert.Equal(1, await tiers.CountAsync());
            var tierRegion = page.Locator("[data-workflow-bulk-pricing]");
            await tierRegion.FocusAsync();
            Assert.True(await tierRegion.EvaluateAsync<bool>("element => document.activeElement === element"));
            await tiers.Last.ScrollIntoViewIfNeededAsync();
            Assert.True(await tiers.Last.EvaluateAsync<bool>(
                "element => { const r = element.getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth; }"));
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
            var uploadedPartId = Guid.Parse((await page.Locator("[data-workflow-part]")
                .GetAttributeAsync("data-part-id"))!);
            await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(uploadedPartId);
            var protectedSession = await factory.Services.GetRequiredService<IInstantQuotationSessionStore>()
                .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
            Assert.NotNull(protectedSession);
            Assert.NotNull(protectedSession.QuoteAuthorization);
            var receipt = Assert.Single(protectedSession.PhysicalReceipts!,
                candidate => candidate.MaterialKey == protectedSession.Parts.Single().Configuration.MaterialKey);
            Assert.Equal(uploadedPartId, receipt.PartId);
            Assert.Equal(protectedSession.SessionId, receipt.SessionId);
            Assert.Equal(protectedSession.Parts.Single().Geometry.Sha256, receipt.UploadSha256);
            Assert.True(receipt.MotionSeconds > 0);
            Assert.Equal(0, await page.Locator("[data-workflow-price-unavailable]").CountAsync());
            string petgComparison;
            try
            {
                petgComparison = await page.Locator("[data-workflow-material-price='PETG'] strong").InnerTextAsync();
            }
            catch (TimeoutException original)
            {
                try
                {
                    var description = comparisonObservation.Describe();
                    original.Data["PetgComparisonServiceObservation"] = description;
                    output.WriteLine("[petg-comparison-services] " + description);
                }
                catch { /* Fixed secondary metadata cannot replace the original price timeout. */ }
                throw;
            }
            Assert.DoesNotContain("฿0", petgComparison, StringComparison.Ordinal);
            var pricingCallsBeforePhysicalCheck = pricing.CallCount;

            await page.Locator("[data-workflow-physical-analysis] button").ClickAsync();
            await page.Locator("[data-physical-analysis-result]").WaitForAsync();
            Assert.Equal("ready:None", await page.Locator("[data-physical-analysis-result]")
                .GetAttributeAsync("data-physical-analysis-result") + ":" +
                await page.Locator("[data-physical-analysis-result]")
                    .GetAttributeAsync("data-physical-analysis-failure"));
            Assert.Equal(pricingCallsBeforePhysicalCheck, pricing.CallCount);

            await MaterialCardBrowserActions.SelectAsync(page, "ABS");
            await page.Locator("[data-physical-analysis-result='ready']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
            Assert.Equal(0, await page.Locator("[data-physical-analysis-result='ready']").CountAsync());
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await review.WaitForAsync();
            try
            {
                await page.WaitForFunctionAsync(
                    "() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')",
                    null,
                    new PageWaitForFunctionOptions { Timeout = 10000 });
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                      workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                      review: document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button')?.outerHTML,
                      status: document.querySelector('#instant-quote-status')?.textContent,
                      alert: document.querySelector('[role=alert]')?.textContent,
                      pricingStatus: document.querySelector('[data-pricing-loading-status]')?.textContent,
                      parts: document.querySelectorAll('[data-workflow-part]').length,
                      tiers: document.querySelectorAll('[data-workflow-price-tier]').length
                    })
                    """);
                throw new InvalidOperationException($"Uploaded part did not enable review: {state}", error);
            }
            if (culture == "th")
            {
                await review.TapAsync();
            }
            else
            {
                await review.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            }
            var part = page.Locator("[data-workflow-review-part]");
            await part.WaitForAsync();
            Assert.Equal(1, await part.CountAsync());
            Assert.Contains(Path.GetFileName(path), await part.InnerTextAsync(), StringComparison.Ordinal);
            var disclosure = part.Locator("[data-review-part-details] > summary");
            if (culture == "th")
            {
                await disclosure.TapAsync();
            }
            else
            {
                await disclosure.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            }
            Assert.True(await part.Locator("[data-review-part-details]").EvaluateAsync<bool>("element => element.open"));
            var continueButton = page.Locator("[data-review-continue]");
            await continueButton.ScrollIntoViewIfNeededAsync();
            Assert.True(await continueButton.EvaluateAsync<bool>(
                "element => { const r = element.getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth; }"));
            Assert.True(await continueButton.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Empty(pageErrors);
            var skipLink = page.Locator(".maliev-skip-link").First;
            await skipLink.FocusAsync();
            Assert.True(await skipLink.EvaluateAsync<bool>("element => getComputedStyle(element).clipPath !== 'inset(50%)'"));
            await page.EvaluateAsync("() => { document.body.tabIndex = -1; document.body.focus(); }");
            Assert.True(await skipLink.EvaluateAsync<bool>("element => getComputedStyle(element).clipPath === 'inset(50%)'"));
            if (Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR") is { Length: > 0 } evidenceDirectory)
            {
                Directory.CreateDirectory(evidenceDirectory);
                await page.Locator("[data-workflow-viewer]").ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Path = Path.Combine(evidenceDirectory, $"source-576-real-upload-viewer-{culture}-{width}.png"),
                });
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(evidenceDirectory, $"source-576-real-upload-{culture}-{width}.png"),
                    FullPage = true,
                });
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SelectingUploadedPartsShowsOnlyTheirOwnPrintTime()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var small = CreateBoxStl(20, 20, 5);
        var large = CreateBoxStl(30, 30, 10);
        try
        {
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", [small, large]);
            var parts = page.Locator("[data-workflow-part]");
            await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 2");
            Assert.Equal(2, await parts.CountAsync());
            var selectedPrintTime = page.Locator("[data-workflow-selected-print-time]");

            var firstId = await parts.Nth(0).GetAttributeAsync("data-part-id");
            await parts.Nth(0).Locator("button[aria-label^='View']").ClickAsync();
            try
            {
                await page.WaitForFunctionAsync(
                    "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                    firstId);
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                      workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                      priceUnavailable: document.querySelectorAll('[data-workflow-price-unavailable]').length,
                      printTime: document.querySelector('[data-workflow-selected-print-time]')?.outerHTML,
                      status: document.querySelector('#instant-quote-status')?.textContent,
                      errors: [...document.querySelectorAll('[role=alert]')].map(x => x.textContent)
                    })
                    """);
                using var scope = factory.Services.CreateScope();
                var saved = await scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>()
                    .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                var analyzer = scope.ServiceProvider.GetRequiredService<InstantQuotationBoundPhysicalAnalysisService>();
                var profileVersion = scope.ServiceProvider.GetRequiredService<Legacy.Maliev.Web.Application.Pricing.FdmRuntimeProfileCatalog>().ProfileVersion;
                var failures = new List<string>();
                foreach (var savedPart in saved?.Parts ?? [])
                {
                    if (savedPart.PhysicalAnalysisUpload is not { } admitted)
                    {
                        failures.Add("upload-missing");
                        continue;
                    }

                    var binding = new InstantQuotationPhysicalAnalysisBinding(
                        saved!.SessionId, upload.OwnerIdentity, savedPart.PartId, admitted.FileId,
                        admitted.Sha256, savedPart.Configuration.MaterialKey,
                        savedPart.Configuration.BuildPreference, savedPart.Configuration.Quantity, profileVersion);
                    var analyzed = await analyzer.AnalyzeAsync(binding, default);
                    failures.Add(analyzed.Failure.ToString());
                }
                throw new InvalidOperationException(
                    $"Two-part physical quote did not become ready: {state}; physical={string.Join(',', failures)}", error);
            }
            var firstDuration = await selectedPrintTime.Locator("dd").InnerTextAsync();

            var secondId = await parts.Nth(1).GetAttributeAsync("data-part-id");
            await parts.Nth(1).Locator("button[aria-label^='View']").ClickAsync();
            await page.WaitForFunctionAsync(
                "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                secondId);
            var secondDuration = await selectedPrintTime.Locator("dd").InnerTextAsync();

            Assert.NotEqual(firstDuration, secondDuration);
            Assert.Equal(1, await selectedPrintTime.CountAsync());
            Assert.Equal(2, upload.VerifiedUploads);
            Assert.Empty(pageErrors);
        }
        finally
        {
            File.Delete(small);
            File.Delete(large);
        }
    }

    [Fact]
    public async Task NineGeneratedStlUploadsKeepDistinctReviewAndPreliminaryPreviews()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        var observation = new MaterialCompletionObservation();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing, observation);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        });
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        (float Width, float Depth, float Height)[] dimensions =
        [
            (12, 18, 7), (19, 13, 11), (24, 23, 8),
            (15, 31, 13), (34, 17, 10), (21, 27, 19),
            (39, 25, 14), (18, 41, 20), (43, 16, 27),
        ];
        var paths = dimensions.Select((size, index) =>
            CreateBoxStl(size.Width, size.Depth, size.Height, $"maliev-preview-part-{index + 1:00}")).ToArray();
        try
        {
            Assert.Equal(9, paths.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                .Distinct(StringComparer.Ordinal).Count());
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", paths);
            await page.WaitForFunctionAsync(
                "() => document.querySelectorAll('[data-workflow-part]').length === 9",
                null,
                new PageWaitForFunctionOptions { Timeout = 90000 });
            Assert.Equal(9, upload.VerifiedUploads);
            var parts = page.Locator("[data-workflow-part]");
            for (var index = 0; index < 9; index++)
            {
                var partId = await parts.Nth(index).GetAttributeAsync("data-part-id");
                await parts.Nth(index).Locator("button[aria-label^='View']").ClickAsync();
                try
                {
                    await page.WaitForFunctionAsync(
                        "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                        partId);
                }
                catch (TimeoutException original)
                {
                    await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, index + 1, partId,
                        () => page.EvaluateAsync<string>("""
                            () => {
                                const duration = document.querySelector('[data-workflow-selected-print-time]');
                                const configuration = document.querySelector('[data-workflow-material-picker]');
                                const quantity = configuration?.querySelector('input[name="quantity"]');
                                return JSON.stringify({
                                    actualPartId: duration?.getAttribute('data-part-id'),
                                    configurationPartId: quantity?.id?.replace(/^quantity-/, ''),
                                    workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                                    isRepricing: !!document.querySelector('[data-pricing-loading-status]'),
                                    material: configuration?.querySelector('[data-material-selector] [aria-checked="true"]')?.dataset.materialKey,
                                    quantity: quantity?.value,
                                    durationPresent: !!duration,
                                    unavailablePresent: !!configuration?.querySelector('[data-workflow-price-unavailable]')
                                });
                            }
                            """), description => output.WriteLine(description));
                    throw;
                }
                using var completionScope = factory.Services.CreateScope();
                var completionStore = completionScope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
                var beforeMaterialChange = await completionStore.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                Assert.NotNull(beforeMaterialChange);
                observation.BeginEdit();
                await MaterialCardBrowserActions.SelectAsync(page, "ABS");
                // Completion includes optional comparison callbacks under the pricing owner's
                // existing 33-second absolute budget, not merely its 30-second selected phase.
                using (var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(33)))
                {
                    try
                    {
                        while (!SelectedPrintTimeTimeoutDiagnostics.IsMaterialChangeComplete(
                            await completionStore.GetAsync(upload.SessionId, upload.OwnerIdentity, completionDeadline.Token)
                                .WaitAsync(completionDeadline.Token), beforeMaterialChange.UpdatedAt, Guid.Parse(partId!)))
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100), completionDeadline.Token);
                        }
                    }
                    catch (OperationCanceledException) when (completionDeadline.IsCancellationRequested)
                    {
                        // Bounded booleans/count only; never replace the primary timeout with a diagnostic failure.
                        try
                        {
                            output.WriteLine($"[material-services] iteration={index + 1}; {observation.Describe()}");
                            var observed = await completionStore.GetAsync(upload.SessionId, upload.OwnerIdentity, default)
                                .WaitAsync(TimeSpan.FromSeconds(1));
                            var observedPart = observed?.Parts.FirstOrDefault(candidate => candidate.PartId == Guid.Parse(partId!));
                            output.WriteLine($"[material-completion] iteration={index + 1}; revisionChanged={observed is not null && observed.UpdatedAt != beforeMaterialChange.UpdatedAt}; authorizationPresent={observed?.QuoteAuthorization is not null}; partPresent={observedPart is not null}; materialMatched={observedPart?.Configuration.MaterialKey == "ABS"}; quantityMatched={observedPart?.Configuration.Quantity == 1}; partCount={Math.Min(observed?.Parts.Count ?? 0, 9)}");
                        }
                        catch { /* Secondary observation must not mask the material-completion timeout. */ }
                        throw new TimeoutException("Protected material change completion was not observed.");
                    }
                }
                await page.WaitForFunctionAsync(
                    "() => !document.querySelector('[data-pricing-loading-status]')");
            }
            try
            {
                await page.WaitForFunctionAsync(
                    """
                    () => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')
                    """,
                    null,
                    new PageWaitForFunctionOptions { Timeout = 90000 });
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                        workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                        parts: document.querySelectorAll('[data-workflow-part]').length,
                        pendingUploads: document.querySelectorAll('[data-workflow-upload-item]').length,
                        reviewEnabled: !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)'),
                        status: document.querySelector('#instant-quote-status')?.textContent,
                        alert: document.querySelector('[role=alert]')?.textContent
                    })
                    """);
                throw new InvalidOperationException(
                    $"Nine-part upload did not reach review. State: {state}; verified uploads: {upload.VerifiedUploads}; page errors: {string.Join(" | ", pageErrors)}.",
                    error);
            }
            Assert.Equal(9, upload.VerifiedUploads);

            await page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").ClickAsync();
            await page.WaitForFunctionAsync(
                """
                () => {
                    const images = [...document.querySelectorAll('[data-workflow-review-part] [data-review-thumbnail]')];
                    return images.length === 9 && images.every(image => image.getAttribute('src')?.startsWith('data:image/webp'));
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 90000 });
            var reviewNames = await page.Locator("[data-workflow-review-part] h4").AllInnerTextsAsync();
            Assert.Equal(paths.Select(Path.GetFileName).Order(StringComparer.Ordinal),
                reviewNames.Order(StringComparer.Ordinal));
            var reviewPreviews = await page.Locator("[data-workflow-review-part] [data-review-thumbnail]")
                .EvaluateAllAsync<string[]>("images => images.map(image => image.getAttribute('src'))");
            Assert.Equal(9, reviewPreviews.Distinct(StringComparer.Ordinal).Count());

            var preliminaryButton = page.Locator("#preliminary-quotation-button");
            Assert.True(await preliminaryButton.IsEnabledAsync());
            await using var preview = await page.RunAndWaitForPopupAsync(() => preliminaryButton.ClickAsync());
            await preview.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var logo = preview.Locator(".iq-preliminary-quotation-logo");
            await preview.WaitForFunctionAsync("() => document.querySelector('.iq-preliminary-quotation-logo')?.complete === true");
            Assert.True(await logo.EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0"));
            Assert.StartsWith("data:image/webp;base64,", await logo.GetAttributeAsync("src"), StringComparison.Ordinal);
            var preliminaryNames = await preview.Locator(".iq-preliminary-quotation-part h2").AllInnerTextsAsync();
            Assert.Equal(reviewNames, preliminaryNames);
            var preliminaryPreviews = await preview.Locator(".iq-preliminary-quotation-thumbnail")
                .EvaluateAllAsync<string[]>("images => images.map(image => image.getAttribute('src'))");
            Assert.Equal(9, preliminaryPreviews.Length);
            Assert.All(preliminaryPreviews, source => Assert.StartsWith("data:image/webp", source, StringComparison.Ordinal));
            Assert.Equal(9, preliminaryPreviews.Distinct(StringComparer.Ordinal).Count());
            Assert.True(await preview.Locator(".iq-preliminary-quotation-thumbnail").EvaluateAllAsync<bool>("""
                images => images.length === 9 && images.every(image => {
                    const bounds = image.getBoundingClientRect();
                    return bounds.width >= 150 && bounds.height >= 150 && getComputedStyle(image).objectFit === 'contain';
                })
                """));
            Assert.Empty(pageErrors);
        }
        finally
        {
            foreach (var path in paths) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("en", "Thin walls may print with defects", "Instant 3D Printing Quote | MALIEV", "0.80 mm", "0.60 mm")]
    [InlineData("th", "ผนังบางอาจเกิดข้อบกพร่อง", "ประเมินราคาพิมพ์ 3D ออนไลน์ | MALIEV", "0.80 มม.", "0.60 มม.")]
    public async Task ThinFdmUploadWarnsAndHeatmapDoesNotRepriceOrBlockReview(
        string culture, string warningTitle, string pageTitle, string fdmLimit, string resinLimit)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        var html = await readiness.Content.ReadAsStringAsync();
        Assert.Contains("data-migration-component=\"instant-quotation-three-dimensional-printing\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"instant-quote-files\"", html, StringComparison.Ordinal);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        });
        await using var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        var consoleErrors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning") consoleErrors.Add(message.Text);
        };

        var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
        var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.Equal(200, response?.Status);
        Assert.Contains(pageTitle, await page.TitleAsync(), StringComparison.Ordinal);
        Assert.Contains("/instantquotation/3d-printing", page.Url, StringComparison.OrdinalIgnoreCase);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateBoxStl(20, 20, 0.7f);
        try
        {
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", path);
            try
            {
                await page.Locator("[data-workflow-part]").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                      workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                      status: document.querySelector('#instant-quote-status')?.textContent,
                      preview: document.querySelector('[data-workflow-preview-status]')?.textContent,
                      upload: document.querySelector('[data-workflow-upload-item]')?.textContent,
                      alert: document.querySelector('[role=alert]')?.textContent,
                      blazor: typeof window.Blazor,
                      selectedFiles: document.querySelector('#instant-quote-files')?.files?.length
                    })
                    """);
                throw new InvalidOperationException(
                    $"Real upload did not create a part. State: {state}. Page errors: {string.Join(" | ", pageErrors)}. Console: {string.Join(" | ", consoleErrors)}. Verified uploads: {upload.VerifiedUploads}.",
                    error);
            }
            try
            {
                await page.Locator("[data-dfm-code='thin-wall']").WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
            }
            catch (TimeoutException)
            {
                using var diagnosticDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    output.WriteLine($"[dfm-thin-services] verifiedUploads={Math.Min(upload.VerifiedUploads, 100)}; pricingCalls={Math.Min(pricing.CallCount, 100)}; pageErrorCount={Math.Min(pageErrors.Count, 100)}; pageErrorsOverflow={pageErrors.Count > 100}; consoleErrorOrWarningCount={Math.Min(consoleErrors.Count, 100)}; consoleErrorsOverflow={consoleErrors.Count > 100}");
                    var rendered = await page.EvaluateAsync<string>("""
                        unavailableText => {
                            const visible = element => !!element && element.getClientRects().length > 0
                                && getComputedStyle(element).visibility !== 'hidden';
                            const count = selector => Math.min(document.querySelectorAll(selector).length, 100);
                            const toggle = document.querySelector('.instant-quote__thickness-toggle');
                            const previewStatus = document.querySelector('[data-workflow-preview-status]');
                            const warnings = [...document.querySelectorAll('[data-dfm-code="thin-wall"]')];
                            return JSON.stringify({
                                configurationPresent: !!document.querySelector('[data-workflow-configuration]'),
                                configurationVisible: visible(document.querySelector('[data-workflow-configuration]')),
                                partCount: count('[data-workflow-part]'),
                                dfmPartCount: count('[data-workflow-dfm-part]'),
                                thinWallCount: Math.min(warnings.length, 100),
                                thinWallVisibleCount: Math.min(warnings.filter(visible).length, 100),
                                incompleteCount: count('[data-dfm-code="thickness-incomplete"]'),
                                togglePresent: !!toggle,
                                toggleDisabled: toggle?.matches(':disabled') === true,
                                previewStatusPresent: !!previewStatus,
                                previewUnavailable: previewStatus?.textContent?.trim() === unavailableText,
                                pricingLoading: !!document.querySelector('[data-pricing-loading-status]')
                            });
                        }
                        """, culture == "th"
                            ? "ไม่สามารถแสดงตัวอย่าง 3 มิติได้ คุณยังสามารถดำเนินการขอใบเสนอราคาต่อได้"
                            : "3D preview is unavailable. You can continue with your quotation.")
                        .WaitAsync(diagnosticDeadline.Token);
                    output.WriteLine($"[dfm-thin-render] {rendered}");
                }
                catch
                {
                    // Secondary observation or output failure must not mask the primary timeout.
                }
                throw;
            }
            var warning = await page.Locator("[data-dfm-code='thin-wall']").InnerTextAsync();
            Assert.Contains(warningTitle, warning, StringComparison.Ordinal);
            Assert.Contains(fdmLimit, warning, StringComparison.Ordinal);


            await MaterialCardBrowserActions.SelectAsync(page, "ABS");
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await page.WaitForFunctionAsync("() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')");

            var toggle = page.Locator(".instant-quote__thickness-toggle");
            await page.WaitForFunctionAsync("() => !!document.querySelector('.instant-quote__thickness-toggle:not(:disabled)')");
            var total = page.Locator("[data-workflow-summary-dock] > summary > strong[aria-live]");
            var before = await total.InnerTextAsync();
            Assert.NotEqual("—", before);
            var pricingCalls = pricing.CallCount;

            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'true'");
            Assert.Equal("true", await toggle.GetAttributeAsync("aria-pressed"));
            Assert.True(await page.Locator(".instant-quote__thickness-legend").IsVisibleAsync());
            Assert.Equal(before, await total.InnerTextAsync());
            Assert.Equal(pricingCalls, pricing.CallCount);
            Assert.True(await review.IsEnabledAsync());

            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'false'");
            Assert.Equal("false", await toggle.GetAttributeAsync("aria-pressed"));
            Assert.False(await page.Locator(".instant-quote__thickness-legend").IsVisibleAsync());
            Assert.Equal(before, await total.InnerTextAsync());
            Assert.Equal(pricingCalls, pricing.CallCount);
            Assert.True(await review.IsEnabledAsync());
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Empty(pageErrors);

            await review.ClickAsync();
            await page.Locator("[data-workflow-review-content]").WaitForAsync();
            var reviewTotal = page.Locator("[data-workflow-review-total] > div")
                .Filter(new LocatorFilterOptions
                {
                    Has = page.GetByText(culture == "th" ? "รวมทั้งหมด" : "Total", new PageGetByTextOptions { Exact = true }),
                }).Locator("dd");
            Assert.Equal(1, await reviewTotal.CountAsync());
            Assert.Equal(before, await reviewTotal.InnerTextAsync());
            await page.Locator("[data-review-part-details] > summary").ClickAsync();
            var reviewedWarnings = await page.Locator("[data-review-dfm-warnings] li").AllInnerTextsAsync();
            Assert.Contains(reviewedWarnings, item => item.Contains(warningTitle, StringComparison.Ordinal)
                && item.Contains(fdmLimit, StringComparison.Ordinal));
            await AssertPrintableDfmMatchesReviewAsync(page, reviewedWarnings);
            Assert.Equal(before, await reviewTotal.InnerTextAsync());
            Assert.Equal(pricingCalls, pricing.CallCount);
            await page.Locator("[data-review-back]").ClickAsync();

            await MaterialCardBrowserActions.SelectAsync(page, "M68");
            try
            {
                await page.WaitForFunctionAsync("() => !document.querySelector('[data-dfm-code=thin-wall]')");
            }
            catch (TimeoutException)
            {
                // One shared secondary deadline; diagnostics must not replace the original timeout.
                using var diagnosticDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    using var resinScope = factory.Services.CreateScope();
                    var resinStore = resinScope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
                    var observed = await resinStore.GetAsync(upload.SessionId, upload.OwnerIdentity, diagnosticDeadline.Token)
                        .WaitAsync(diagnosticDeadline.Token);
                    var observedPart = observed is not null && observed.Parts.Count == 1 ? observed.Parts[0] : null;
                    output.WriteLine($"[dfm-resin-authority] authorizationPresent={observed?.QuoteAuthorization is not null}; singlePartPresent={observedPart is not null}; materialMatched={observedPart?.Configuration.MaterialKey == "M68"}; uploadCount={Math.Min(upload.VerifiedUploads, 2)}; pageErrorCount={Math.Min(pageErrors.Count, 10)}");
                    var rendered = await page.EvaluateAsync<string>("""
                        limits => JSON.stringify({
                            materialMatched: document.querySelector('[data-workflow-configuration] [data-material-selector] [aria-checked="true"]')?.dataset.materialKey === 'M68',
                            reviewEnabled: !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)'),
                            pricingLoading: !!document.querySelector('[data-pricing-loading-status]'),
                            thinWallPresent: !!document.querySelector('[data-dfm-code=thin-wall]'),
                            thicknessAvailable: !!document.querySelector('.instant-quote__thickness-toggle:not(:disabled)'),
                            warningUsesResinLimit: !!document.querySelector('[data-dfm-code=thin-wall]')?.textContent?.includes(limits.resinLimit),
                            warningUsesFdmLimit: !!document.querySelector('[data-dfm-code=thin-wall]')?.textContent?.includes(limits.fdmLimit)
                        })
                        """, new { resinLimit, fdmLimit }).WaitAsync(diagnosticDeadline.Token);
                    output.WriteLine($"[dfm-resin-render] {rendered}");
                }
                catch
                {
                    // Secondary observation or output failure must not mask the primary timeout.
                }
                throw;
            }
            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("limit => document.querySelector('.instant-quote__thickness-legend')?.textContent?.includes(limit)", resinLimit);
            Assert.DoesNotContain("needs-attention", await toggle.GetAttributeAsync("class"), StringComparison.Ordinal);
            Assert.Equal(1, upload.VerifiedUploads);

            await page.WaitForFunctionAsync("() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')");
            var resinPrice = await total.InnerTextAsync();
            await review.ClickAsync();
            await page.Locator("[data-workflow-review-content]").WaitForAsync();
            var resinWarnings = await page.Locator("[data-review-dfm-warnings] li").AllInnerTextsAsync();
            Assert.DoesNotContain(resinWarnings, item => item.Contains(warningTitle, StringComparison.Ordinal));
            Assert.Equal(resinPrice, await reviewTotal.InnerTextAsync());
            var resinPricingCalls = pricing.CallCount;
            await AssertPrintableDfmMatchesReviewAsync(page, resinWarnings);
            Assert.Equal(resinPrice, await reviewTotal.InnerTextAsync());
            Assert.Equal(resinPricingCalls, pricing.CallCount);
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Empty(pageErrors);
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Theory]
    [InlineData(375, "th")]
    [InlineData(1280, "en")]
    public async Task BulkSelection_UsesAccessibleNativeControlsAndKeepsServerOwnedPartSettings(int width, string culture)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 800 },
        });
        await using var failureEvidence = new BrowserFailureEvidence(page,
            metadata => output.WriteLine("[real-upload-evidence-cleanup] " + metadata));
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        var bytes = WallThicknessParityBrowserFixture.Box(5);
        await page.SetInputFilesAsync("#instant-quote-files", Enumerable.Range(0, 3).Select(index => new FilePayload
        {
            Name = $"bulk-{index}.stl",
            MimeType = "model/stl",
            Buffer = bytes,
        }));
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 3 && !document.querySelector('#instant-quote-files').disabled");
        var parts = page.Locator("[data-workflow-part]");
        var ids = await parts.EvaluateAllAsync<string[]>("nodes => nodes.map(node => node.dataset.partId)");
        await parts.Nth(1).Locator("button").First.ClickAsync();
        await page.Locator($"#quantity-{ids[1]}").FillAsync("7");
        await page.Locator($"#quantity-{ids[1]}").PressAsync("Tab");
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-pricing-loading-status]')");
        await page.Locator($"input[name='build-preference-{ids[1]}'][value='strength']").CheckAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-pricing-loading-status]')");
        await parts.Nth(2).Locator("button").Last.ClickAsync();
        await Assertions.Expect(parts.Nth(2).Locator("[data-workflow-part-select]")).ToBeDisabledAsync();
        await parts.Nth(0).Locator("button").First.ClickAsync();
        await MaterialCardBrowserActions.SelectAsync(page, "PETG");
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-pricing-loading-status]')");

        var second = parts.Nth(1).Locator("[data-workflow-part-select]");
        await second.Locator("..").ClickAsync(new LocatorClickOptions { Position = new Position { X = 3, Y = 3 } });
        await Assertions.Expect(second).ToBeCheckedAsync();
        var all = page.Locator("[data-workflow-select-all]");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-workflow-select-all]').indeterminate === true");
        Assert.Equal("mixed", await all.GetAttributeAsync("aria-checked"));
        Assert.True(await page.Locator("[data-workflow-part-select], [data-workflow-select-all]").EvaluateAllAsync<bool>("""
            inputs => inputs.length === 4 && inputs.every(input => {
              const box = input.getBoundingClientRect(), label = input.labels[0].getBoundingClientRect();
              return box.width === 16 && box.height === 16 && label.width >= 44 && label.height >= 44;
            })
            """));
        await second.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(second).Not.ToBeCheckedAsync();
        await all.CheckAsync();
        await Assertions.Expect(page.Locator("[data-workflow-part-select]:checked")).ToHaveCountAsync(2);
        // Add file stays reachable while selecting parts; mobile configuration follows in the page scroll path.
        try
        {
            await Assertions.Expect(page.Locator("[data-workflow-parts] .iq-compact-dropzone")).ToBeInViewportAsync();
        }
        catch (Exception exception)
        {
            await ObserveRealUploadFailureAsync(exception, failureEvidence, page, factory, upload);
            throw;
        }
        await page.WaitForFunctionAsync("() => document.querySelector('[data-workflow-select-all]').indeterminate === false");
        await Assertions.Expect(page.Locator("[data-workflow-apply-material]")).ToHaveAccessibleNameAsync(
            culture == "th" ? "ใช้ PETG กับชิ้นงานที่เลือก" : "Apply PETG to selected parts");
        await page.Locator("[data-workflow-apply-material]").ClickAsync();
        var sessions = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        using var resultDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        InstantQuotationSessionState? saved;
        do
        {
            saved = await sessions.GetAsync(upload.SessionId, upload.OwnerIdentity, resultDeadline.Token);
            if (saved?.Parts.Single(part => part.PartId == Guid.Parse(ids[1])).Configuration.MaterialKey == "PETG") break;
            await Task.Delay(20, resultDeadline.Token);
        } while (true);
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-workflow-material-color-quantity][inert]')");
        await Assertions.Expect(parts.Nth(1).Locator("[data-workflow-part-select]")).ToBeEnabledAsync();
        Assert.NotNull(saved);
        Assert.Equal(3, saved.Parts.Count);
        Assert.Equal("PETG", saved.Parts.Single(part => part.PartId == Guid.Parse(ids[0])).Configuration.MaterialKey);
        var changed = saved.Parts.Single(part => part.PartId == Guid.Parse(ids[1]));
        Assert.Equal("PETG", changed.Configuration.MaterialKey);
        Assert.Equal(7, changed.Configuration.Quantity);
        Assert.Equal(Legacy.Maliev.Web.Application.Pricing.BuildPreference.Strength, changed.Configuration.BuildPreference);
        Assert.Equal("PLA", saved.Parts.Single(part => part.PartId == Guid.Parse(ids[2])).Configuration.MaterialKey);
        Assert.Equal(3, upload.VerifiedUploads);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
    }


    [Theory]
    [InlineData(375, "th")]
    [InlineData(1280, "en")]
    public async Task NewBatch_RevealsItsLatestAdmittedPartAndRetainsAnUnreadableSelectionUntilDismissed(
        int width, string culture)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 800 },
        });
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        var bytes = WallThicknessParityBrowserFixture.Box(5);
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "original.stl",
            MimeType = "model/stl",
            Buffer = bytes,
        });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 1 && !document.querySelector('#instant-quote-files').disabled");
        var originalId = await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id");
        var next = Enumerable.Range(0, 6).Select(index => new FilePayload
        {
            Name = $"new-{index}.stl",
            MimeType = "model/stl",
            Buffer = bytes,
        }).Append(new FilePayload
        {
            Name = "unreadable.stl",
            MimeType = "model/stl",
            Buffer = new byte[] { 1, 2, 3 },
        }).ToArray();
        await page.SetInputFilesAsync("#instant-quote-files", next);
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 7 && document.querySelectorAll('[data-workflow-failed-preview]').length === 1 && !document.querySelector('#instant-quote-files').disabled",
            null, new PageWaitForFunctionOptions { Timeout = 90000 });
        var last = page.Locator("[data-workflow-part]").Last;
        await Assertions.Expect(last.Locator("button[aria-pressed]")).ToHaveAttributeAsync("aria-pressed", "true");
        var latestId = await last.GetAttributeAsync("data-part-id");
        Assert.NotEqual(originalId, latestId);
        await page.WaitForFunctionAsync("""
            () => {
                const rail = document.querySelector('[data-workflow-part-list]');
                const tile = rail.querySelector('[data-workflow-part]:last-child');
                if (!tile) return false;
                const r = rail.getBoundingClientRect(), t = tile.getBoundingClientRect();
                const add = rail.querySelector('.iq-compact-dropzone');
                if (getComputedStyle(rail).overflowY === 'visible') {
                    const a = add.getBoundingClientRect();
                    const occludesTop = getComputedStyle(add).position === 'sticky'
                        && a.bottom > 0 && a.top < innerHeight && a.left < t.right && a.right > t.left;
                    const top = occludesTop ? Math.max(0, a.bottom) : 0;
                    return t.left >= Math.max(0, r.left) - 1 && t.right <= Math.min(innerWidth, r.right) + 1
                        && t.top >= top - 1 && t.bottom <= innerHeight + 1;
                }
                const left = getComputedStyle(add).position === 'sticky'
                    ? Math.max(r.left, add.getBoundingClientRect().right) : r.left;
                return t.left >= left - 1 && t.right <= r.right + 1
                    && (rail.scrollHeight <= rail.clientHeight || (t.top >= r.top - 1 && t.bottom <= r.bottom + 1));
            }
            """);
        var failed = page.Locator("[data-workflow-failed-preview]");
        await Assertions.Expect(failed).ToContainTextAsync("unreadable.stl");
        var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        await Assertions.Expect(review).ToBeDisabledAsync();
        Assert.Equal(7, upload.VerifiedUploads);
        Assert.Equal(7, upload.StartedUploads);
        var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        var saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(saved);
        var original = saved.Parts.Single(part => part.PartId == Guid.Parse(originalId!));
        Assert.Equal("original.stl", original.DisplayFileName);
        Assert.Equal(1, original.Configuration.Quantity);
        Assert.Equal("PLA", original.Configuration.MaterialKey);
        Assert.NotNull(saved.QuoteAuthorization);
        // New uploads have not accepted a configuration yet. Configure the active
        // part and its eligible siblings through the actual existing bulk route.
        await MaterialCardBrowserActions.SelectAsync(page, "PETG");
        await page.Locator("[data-workflow-select-all]").CheckAsync();
        await page.Locator("[data-workflow-apply-material]").ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#instant-quote-files').disabled && document.querySelector('[data-workflow-configuration] fieldset[inert]') === null");
        await failed.Locator("[data-workflow-remove-failed]").ClickAsync();
        await Assertions.Expect(failed).ToHaveCountAsync(0);
        await Assertions.Expect(review).ToBeEnabledAsync();
        await review.ClickAsync();
        await Assertions.Expect(page.Locator("[data-workflow-review]")).ToBeVisibleAsync();
        Assert.Equal(7, upload.VerifiedUploads);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
    }


    [Theory]
    [InlineData("en", 1440, true, "Invalid file. Repair the model and reupload it.")]
    [InlineData("th", 1440, false, "ไฟล์ไม่สมบูรณ์ กรุณาแก้ไขโมเดลแล้วอัปโหลดใหม่")]
    [InlineData("en", 390, false, "Invalid file. Repair the model and reupload it.")]
    [InlineData("th", 390, true, "ไฟล์ไม่สมบูรณ์ กรุณาแก้ไขโมเดลแล้วอัปโหลดใหม่")]
    public async Task MixedUploadFailure_UsesLocalSelectionAlertAndUnobscuredThumbnail(
        string culture, int width, bool rejectsRenderable, string invalidMessage)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        using var readiness = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = width, Height = 1000 } });
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        upload.BeforeUpload = async () => { entered.TrySetResult(); await release.Task.WaitAsync(lease.Token); };
        Exception? primary = null;
        var badPath = CreateOpenSheetStl();
        try
        {
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "healthy.stl",
                MimeType = "model/stl",
                Buffer = WallThicknessParityBrowserFixture.Box(5),
            });
            await entered.Task.WaitAsync(lease.Token);
            var processing = page.Locator("[data-workflow-upload-overlay]");
            await Assertions.Expect(processing).ToBeVisibleAsync();
            Assert.True(await processing.EvaluateAsync<bool>("""
                overlay => {
                    const card = overlay.closest('[data-workflow-upload-item]'), a = overlay.getBoundingClientRect(), b = card.getBoundingClientRect();
                    return Math.abs(a.left - b.left - card.clientLeft) <= 1
                        && Math.abs(a.top - b.top - card.clientTop) <= 1
                        && Math.abs(a.right - b.right + card.clientLeft) <= 1
                        && Math.abs(a.bottom - b.bottom + card.clientTop) <= 1;
                }
                """));
            release.TrySetResult();
            await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 1 && !document.querySelector('#instant-quote-files').disabled",
                null, new() { Timeout = 90000 });
            upload.BeforeUpload = null;
            await MaterialCardBrowserActions.SelectAsync(page, "PETG");
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await Assertions.Expect(review).ToBeEnabledAsync();
            var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
            var saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
            var healthy = Assert.Single(Assert.IsType<InstantQuotationSessionState>(saved).Parts);
            var authorization = saved!.QuoteAuthorization;
            Assert.NotNull(authorization);
            var healthyPricingCalls = pricing.CallCount;
            upload.RejectFileName = rejectsRenderable ? "open-mesh.stl" : null;
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "open-mesh.stl",
                MimeType = "model/stl",
                Buffer = rejectsRenderable
                    ? WallThicknessParityBrowserFixture.Box(5) : await File.ReadAllBytesAsync(badPath),
            });
            var failed = page.Locator("[data-workflow-upload-item][data-upload-error]");
            await Assertions.Expect(failed).ToHaveCountAsync(1);
            await page.WaitForFunctionAsync("() => !document.querySelector('#instant-quote-files').disabled");
            var localId = await failed.GetAttributeAsync("data-upload-local-id");
            Assert.True(Guid.TryParse(localId, out _));
            Assert.Null(await failed.GetAttributeAsync("data-part-id"));
            await page.WaitForFunctionAsync("() => { const image = document.querySelector('[data-upload-error] [data-workflow-local-thumbnail]'); return image?.complete && image.naturalWidth > 0; }");
            var image = failed.Locator("[data-workflow-local-thumbnail]");
            await Assertions.Expect(image).ToBeVisibleAsync();
            var panel = page.Locator("[data-workflow-file-error]");
            await page.Locator("[data-workflow-part] button[aria-pressed]").ClickAsync();
            await Assertions.Expect(panel).ToBeHiddenAsync();
            await failed.Locator("[data-workflow-view-upload]").ClickAsync();
            await Assertions.Expect(panel).ToBeVisibleAsync();
            await Assertions.Expect(panel.Locator("strong")).ToHaveTextAsync("open-mesh.stl");
            await Assertions.Expect(panel).ToContainTextAsync(invalidMessage);
            Assert.Equal(1, await page.Locator("[role='alert']").CountAsync());
            Assert.Equal(0, await failed.Locator("[data-workflow-upload-overlay]").CountAsync());
            var badge = failed.Locator("[data-workflow-file-error-badge]");
            await Assertions.Expect(badge).ToHaveAttributeAsync("role", "img");
            await Assertions.Expect(badge).ToHaveAttributeAsync("aria-label", "open-mesh.stl: " + invalidMessage);
            var badgeBounds = await badge.BoundingBoxAsync();
            Assert.NotNull(badgeBounds);
            Assert.InRange(badgeBounds.Width, 19, 21);
            Assert.InRange(badgeBounds.Height, 19, 21);
            Assert.Equal("rgb(220, 53, 69)", await failed.EvaluateAsync<string>("element => getComputedStyle(element).borderTopColor"));
            Assert.True(await panel.EvaluateAsync<bool>("""
                panel => {
                    const a = panel.getBoundingClientRect(), b = panel.closest('[data-workflow-viewer]').getBoundingClientRect();
                    return a.left >= b.left && a.top >= b.top && a.right <= b.right + 1 && a.bottom <= b.bottom + 1;
                }
                """));
            await Assertions.Expect(review).ToBeDisabledAsync();
            await page.Locator("[data-workflow-part] button[aria-pressed]").ClickAsync();
            await Assertions.Expect(panel).ToBeHiddenAsync();
            await failed.Locator("[data-workflow-view-upload]").ClickAsync();
            await Assertions.Expect(panel).ToBeVisibleAsync();
            await failed.Locator("[data-workflow-remove-upload]").ClickAsync();
            await Assertions.Expect(failed).ToHaveCountAsync(0);
            await Assertions.Expect(panel).ToBeHiddenAsync();
            await Assertions.Expect(panel).ToHaveTextAsync(string.Empty);
            await Assertions.Expect(review).ToBeEnabledAsync();
            saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
            var retained = Assert.Single(saved!.Parts);
            Assert.Equal(healthy.PartId, retained.PartId);
            Assert.Equal(healthy.DisplayFileName, retained.DisplayFileName);
            Assert.Equal(healthy.Configuration, retained.Configuration);
            Assert.Equal(healthy.Geometry.Sha256, retained.Geometry.Sha256);
            Assert.Equal(healthy.UploadReference, retained.UploadReference);
            Assert.True(System.Text.Json.JsonSerializer.Serialize(authorization) ==
                System.Text.Json.JsonSerializer.Serialize(saved.QuoteAuthorization),
                "Removing a local failure must not replace the healthy protected quote authorization.");
            Assert.Equal(rejectsRenderable ? 2 : 1, upload.StartedUploads);
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Equal(healthyPricingCalls, pricing.CallCount);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { File.Delete(badPath); return ValueTask.CompletedTask; },
                async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                    upload.LastUploadTask ?? Task.CompletedTask, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                    receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                    expectedTerminalFailure: true, taskKind: "browser-processing-upload"));
        }
    }

    [Theory]
    [InlineData("en", "unreadable", "Invalid file. Repair the model and reupload it.")]
    [InlineData("th", "unreadable", "ไฟล์ไม่สมบูรณ์ กรุณาแก้ไขโมเดลแล้วอัปโหลดใหม่")]
    [InlineData("en", "complexity", "This file exceeds the analysis limits. Simplify the mesh or contact our engineers for a quotation.")]
    [InlineData("th", "complexity", "ไฟล์นี้เกินขีดจำกัดของการวิเคราะห์ กรุณาลดความซับซ้อนของเมชหรือติดต่อวิศวกรเพื่อขอใบเสนอราคา")]
    [InlineData("en", "unavailable", "3D file processing is unavailable. Try again or contact our engineers for a quotation.")]
    [InlineData("th", "unavailable", "ไม่สามารถประมวลผลไฟล์ 3 มิติได้ โปรดลองอีกครั้งหรือติดต่อวิศวกรเพื่อขอใบเสนอราคา")]
    public async Task ActualPreviewFailure_DistinguishesAdvisoryCategoryWithoutChangingHealthyReceipt(
        string culture, string category, string expected)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync(new()
        { ViewportSize = new() { Width = culture == "th" ? 390 : 1440, Height = 1000 } });
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "healthy-advisory.stl",
            MimeType = "model/stl",
            Buffer = WallThicknessParityBrowserFixture.Box(5),
        });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 1 && !document.querySelector('#instant-quote-files').disabled");
        await MaterialCardBrowserActions.SelectAsync(page, "PETG");
        var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        await Assertions.Expect(review).ToBeEnabledAsync();
        var partId = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
        await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(partId);
        var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        var before = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
        Assert.NotNull(before.QuoteAuthorization);
        Assert.Contains(before.PhysicalReceipts!, receipt => receipt.PartId == partId && receipt.MotionSeconds > 0);
        var protectedBefore = System.Text.Json.JsonSerializer.Serialize(before);
        var calls = pricing.CallCount;
        Exception? primary = null;
        try
        {
            await page.EvaluateAsync("""
                category => {
                    const original = File.prototype.arrayBuffer;
                    window.__restoreAdvisoryRead = () => { File.prototype.arrayBuffer = original; delete window.__restoreAdvisoryRead; };
                    File.prototype.arrayBuffer = function () {
                        if (this.name !== 'advisory-failure.stl') return original.call(this);
                        return Promise.reject(category === 'unreadable'
                            ? new TypeError('Unable to read this 3D file.')
                            : new Error(category === 'complexity'
                                ? 'Preview processing timed out.' : 'Unable to load 3D file processing support.'));
                    };
                }
                """, category);
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "advisory-failure.stl",
                MimeType = "model/stl",
                Buffer = WallThicknessParityBrowserFixture.Box(5),
            });
            var failed = page.Locator("[data-workflow-failed-preview]");
            await Assertions.Expect(failed).ToHaveCountAsync(1);
            await page.WaitForFunctionAsync("() => !document.querySelector('#instant-quote-files').disabled");
            Assert.Null(await failed.GetAttributeAsync("data-part-id"));
            await failed.Locator("button[aria-pressed]").ClickAsync();
            var alert = page.Locator("[data-workflow-file-error]");
            await Assertions.Expect(alert).ToBeVisibleAsync();
            await Assertions.Expect(alert).ToContainTextAsync(expected);
            Assert.Equal(1, await page.Locator("[role='alert']").CountAsync());
            if (category != "unreadable")
                await Assertions.Expect(alert).Not.ToContainTextAsync(culture == "th" ? "ไฟล์ไม่สมบูรณ์" : "Invalid file");
            await Assertions.Expect(review).ToBeDisabledAsync();
            Assert.Equal(1, upload.StartedUploads);
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Equal(calls, pricing.CallCount);
            Assert.True(protectedBefore == System.Text.Json.JsonSerializer.Serialize(
                (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!));
            await failed.Locator("[data-workflow-remove-failed]").ClickAsync();
            await Assertions.Expect(failed).ToHaveCountAsync(0);
            await Assertions.Expect(alert).ToBeHiddenAsync();
            await Assertions.Expect(review).ToBeEnabledAsync();
            Assert.True(protectedBefore == System.Text.Json.JsonSerializer.Serialize(
                (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!));
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            try { await page.EvaluateAsync("window.__restoreAdvisoryRead?.()").WaitAsync(TimeSpan.FromSeconds(5)); }
            catch when (primary is not null) { /* Preserve the browser failure; owned page disposal is the final cleanup. */ }
        }
    }

    private static async Task AssertPrintableDfmMatchesReviewAsync(IPage page, IReadOnlyList<string> reviewedWarnings)
    {
        var preliminary = page.Locator("#preliminary-quotation-button");
        Assert.True(await preliminary.IsEnabledAsync());
        await using var preview = await page.RunAndWaitForPopupAsync(() => preliminary.ClickAsync());
        await preview.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        Assert.Equal(1, await preview.Locator(".iq-preliminary-quotation-part").CountAsync());
        var printableWarnings = await preview.Locator(".iq-preliminary-quotation-part .iq-preliminary-quotation-warnings li").AllInnerTextsAsync();
        Assert.Equal(reviewedWarnings, printableWarnings);
        Assert.Equal(reviewedWarnings.Count == 0 ? 1 : 0,
            await preview.Locator(".iq-preliminary-quotation-part .iq-preliminary-quotation-dfm-ok").CountAsync());
    }

    [Fact]
    public async Task OneMillimeterFdmUploadDoesNotInventAThinWallWarning()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
        var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.Equal(200, response?.Status);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateBoxStl(20, 20, 1f, "maliev-one-millimeter-fdm");
        try
        {
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", path);
            await page.Locator("[data-workflow-part]").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await page.WaitForFunctionAsync("() => !!document.querySelector('.instant-quote__thickness-toggle:not(:disabled)')");
            await MaterialCardBrowserActions.SelectAsync(page, "ABS");
            await page.Locator("[data-workflow-dfm-part]").WaitForAsync();

            Assert.Equal(0, await page.Locator("[data-dfm-code='thin-wall']").CountAsync());
            var toggle = page.Locator(".instant-quote__thickness-toggle");
            Assert.DoesNotContain("needs-attention", await toggle.GetAttributeAsync("class"), StringComparison.Ordinal);
            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'true'");
            Assert.True(await page.Locator(".instant-quote__thickness-legend").IsVisibleAsync());
            Assert.Contains("0.80 mm", await page.Locator(".instant-quote__thickness-legend").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Equal(1, upload.VerifiedUploads);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenSheetFailsClosedInsteadOfInventingSafeThickness()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        await using var failureEvidence = new BrowserFailureEvidence(page,
            metadata => output.WriteLine("[real-upload-evidence-cleanup] " + metadata));
        var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
        var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.Equal(200, response?.Status);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateOpenSheetStl();
        try
        {
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", path);
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__workflow')?.dataset.workflowState === 'error'");
            await Assertions.Expect(page.Locator("[data-workflow-file-error]")).ToBeVisibleAsync();
            Assert.Contains("The file could not be uploaded. Try again.",
                await page.Locator("[role='alert']").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Equal(0, await page.Locator("[data-workflow-part]").CountAsync());
            Assert.Equal(0, await page.Locator("[data-workflow-dfm-clear]").CountAsync());
            await page.Locator("[data-upload-error] [data-workflow-remove-upload]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-workflow-file-error]")).ToHaveCountAsync(0);
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "replacement.stl",
                MimeType = "model/stl",
                Buffer = WallThicknessParityBrowserFixture.Box(5),
            });
            await page.WaitForFunctionAsync("""
                () => {
                    const canvas = document.querySelector('[data-workflow-viewer] > canvas');
                    const bounds = canvas?.getBoundingClientRect();
                    const ratio = Math.max(1, Math.min(Number(devicePixelRatio) || 1, 2));
                    return document.querySelectorAll('[data-workflow-part]').length === 1 && bounds?.width > 300
                        && canvas.clientWidth > 0 && canvas.clientHeight > 0
                        && canvas.width === Math.floor(Math.round(canvas.clientWidth) * ratio)
                        && canvas.height === Math.floor(Math.round(canvas.clientHeight) * ratio);
                }
                """, null, new PageWaitForFunctionOptions { Timeout = 90000 });
            Assert.Equal(1, upload.VerifiedUploads);
        }
        catch (Exception exception)
        {
            await ObserveRealUploadFailureAsync(exception, failureEvidence, page, factory, upload);
            throw;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(10, 2)]
    public async Task ActualUploadedParts_PreliminaryPopupExportsA4Pdf(int partCount, int minimumPages)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            Locale = "en-US",
        });
        await using var page = await context.NewPageAsync();
        var pageErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);
        var paths = new List<string>();
        await using var failureEvidence = new BrowserFailureEvidence(page,
            metadata => output.WriteLine("[pdf-evidence-cleanup] " + metadata));
        failureEvidence.Stage("Navigation");
        try
        {
            var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
            var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            for (var index = 0; index < partCount; index++)
            {
                paths.Add(CreatePreliminaryPdfBoxStl(index + 1));
            }
            failureEvidence.Stage("UploadAdmission");
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", paths);
            try
            {
                await page.WaitForFunctionAsync(
                    "count => document.querySelectorAll('[data-workflow-part]').length === count",
                    partCount, new PageWaitForFunctionOptions { Timeout = 90000 });
            }
            catch (Exception original)
            {
                await failureEvidence.AttachAsync(original, () => page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                      workflow:document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                      parts:document.querySelectorAll('[data-workflow-part]').length,
                      uploadRows:document.querySelectorAll('[data-workflow-upload-item]').length,
                      progress:document.querySelectorAll('[data-workflow-upload-item] progress').length > 0,
                      errorVisible:document.querySelector('#blazor-error-ui')?.style.display === 'block'
                    })
                    """), metadata => output.WriteLine("[pdf-upload-admission] " + metadata));
                try
                {
                    output.WriteLine($"[pdf-upload-services] expected={partCount}; started={Math.Min(upload.StartedUploads, 32)}; verified={Math.Min(upload.VerifiedUploads, 32)}; validationRejected={Math.Min(upload.ValidationRejectedUploads, 32)}");
                }
                catch { /* Secondary evidence must preserve the upload admission failure. */ }
                throw;
            }
            Assert.Equal(partCount, upload.VerifiedUploads);
            var parts = page.Locator("[data-workflow-part]");
            var partIds = new HashSet<Guid>();
            for (var index = 0; index < partCount; index++)
            {
                var partIdText = await parts.Nth(index).GetAttributeAsync("data-part-id");
                Assert.True(Guid.TryParse(partIdText, out var partId));
                Assert.True(partIds.Add(partId));
                await parts.Nth(index).Locator("button[aria-label^='View']").ClickAsync();
                await page.WaitForFunctionAsync(
                    "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                    partIdText);
                using var scope = factory.Services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
                var beforeMaterialChange = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                Assert.NotNull(beforeMaterialChange);
                Assert.Equal(partCount, beforeMaterialChange.Parts.Count);
                var storedPart = Assert.Single(beforeMaterialChange.Parts, candidate => candidate.PartId == partId);
                var physical = Assert.IsType<InstantQuotationPhysicalAnalysisUpload>(storedPart.PhysicalAnalysisUpload);
                var expectedPath = Assert.Single(paths, path => Path.GetFileName(path) == storedPart.DisplayFileName);
                var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedPath))).ToLowerInvariant();
                Assert.Equal(expectedHash, physical.Sha256);
                var mesh = await new InstantQuotationAdmittedMeshService(new BrowserMultiUploadInputReader(upload, store))
                    .ReadAsync(upload.SessionId, upload.OwnerIdentity, partId, physical.FileId, physical.Sha256, default);
                Assert.True(mesh.IsReady);
                Assert.Equal(expectedHash, mesh.UploadSha256);
                Assert.Equal(12, mesh.Mesh!.Triangles.Count);
                await MaterialCardBrowserActions.SelectAsync(page, "ABS");
                InstantQuotationSessionState? lastMaterialChangeState = null;
                using (var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    try
                    {
                        while (!SelectedPrintTimeTimeoutDiagnostics.IsMaterialChangeComplete(
                            lastMaterialChangeState = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, completionDeadline.Token)
                                .WaitAsync(completionDeadline.Token), beforeMaterialChange.UpdatedAt, partId))
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100), completionDeadline.Token);
                        }
                    }
                    catch (OperationCanceledException) when (completionDeadline.IsCancellationRequested)
                    {
                        var original = new TimeoutException("Protected material change completion was not observed.");
                        SelectedPrintTimeTimeoutDiagnostics.AttachMaterialCompletion(original, lastMaterialChangeState,
                            beforeMaterialChange.UpdatedAt, partId, output.WriteLine);
                        throw original;
                    }
                }
                await page.WaitForFunctionAsync("() => !document.querySelector('[data-pricing-loading-status]')");
            }
            using (var scope = factory.Services.CreateScope())
            {
                var state = await scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>()
                    .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                Assert.NotNull(state);
                Assert.Equal(partCount, state.Parts.Select(part => part.PhysicalAnalysisUpload!.FileId).Distinct().Count());
                Assert.NotNull(state.QuoteAuthorization);
            }
            await page.WaitForFunctionAsync(
                "() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')",
                null, new PageWaitForFunctionOptions { Timeout = 90000 });
            failureEvidence.Stage("Review");
            failureEvidence.AwaitBoundary("review", false);
            try
            {
                await page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").ClickAsync();
                await page.WaitForFunctionAsync(
                    """
                    count => {
                        const images = [...document.querySelectorAll('[data-workflow-review-part] [data-review-thumbnail]')];
                        return images.length === count && images.every(image => image.getAttribute('src')?.startsWith('data:image/webp'));
                    }
                    """, partCount, new PageWaitForFunctionOptions { Timeout = 90000 });
                failureEvidence.AwaitBoundary("review", true);
            }
            catch (Exception original)
            {
                await failureEvidence.AttachAsync(original, () => page.EvaluateAsync<string>("""
                    expected => {
                      const images = [...document.querySelectorAll('[data-workflow-review-part] [data-review-thumbnail]')];
                      const workflow = document.querySelector('.instant-quote__workflow')?.dataset.workflowState;
                      const reviewParts = document.querySelectorAll('[data-workflow-review-part]').length;
                      const webp = image => image.getAttribute('src')?.startsWith('data:image/webp') === true;
                      const placeholder = image => image.getAttribute('src') === '/src/images/3d-canvas-placeholder.svg';
                      const webpReady = images.filter(webp).length;
                      return JSON.stringify({
                        workflow:['empty','uploading','uploaded','error','multipart','configured','review','customerdetails','submitted'].includes(workflow) ? workflow : 'unavailable',
                        expectedThumbnails:Math.min(expected,32),
                        reviewParts:Math.min(reviewParts,32),
                        actualThumbnails:Math.min(images.length,32),
                        webpReadyThumbnails:Math.min(webpReady,32),
                        reviewImages:Math.min(images.length,32),
                        reviewWebpImages:Math.min(webpReady,32),
                        reviewPlaceholderImages:Math.min(images.filter(placeholder).length,32),
                        reviewOtherImages:Math.min(images.filter(image => !webp(image) && !placeholder(image)).length,32),
                        reviewCountsCapped:expected > 32 || reviewParts > 32 || images.length > 32,
                        errorVisible:document.querySelector('#blazor-error-ui')?.style.display === 'block'
                      });
                    }
                    """, partCount), metadata => output.WriteLine("[pdf-review-readiness] " + metadata));
                throw;
            }
            var reviewNames = await page.Locator("[data-workflow-review-part] h4").AllInnerTextsAsync();
            Assert.Equal(paths.Select(Path.GetFileName).Order(StringComparer.Ordinal), reviewNames.Order(StringComparer.Ordinal));
            var button = page.Locator("#preliminary-quotation-button");
            Assert.True(await button.IsEnabledAsync());
            await using var preview = await page.RunAndWaitForPopupAsync(() => button.ClickAsync());
            preview.PageError += (_, error) => pageErrors.Enqueue(error);
            await preview.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.Equal(reviewNames, await preview.Locator(".iq-preliminary-quotation-part h2").AllInnerTextsAsync());
            Assert.Equal(1, await preview.Locator(".iq-preliminary-quotation-summary").CountAsync());
            await WaitForPreliminaryPdfImagesAndFontsAsync(preview, partCount);
            Assert.StartsWith("data:image/webp;base64,", await preview.Locator(".iq-preliminary-quotation-logo").GetAttributeAsync("src"), StringComparison.Ordinal);
            Assert.True(await preview.Locator(".iq-preliminary-quotation-thumbnail").EvaluateAllAsync<bool>(
                """
                images => images.every(image => {
                    const bounds = image.getBoundingClientRect();
                    return image.getAttribute('src')?.startsWith('data:image/webp')
                        && bounds.width >= 150 && bounds.height >= 150 && getComputedStyle(image).objectFit === 'contain';
                })
                """));
            var pdf = await preview.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true });
            // Retain the exact returned bytes before assertions, including a failing page floor.
            var rawPageCount = System.Text.RegularExpressions.Regex.Matches(
                System.Text.Encoding.ASCII.GetString(pdf), @"/Type\s*/Page(?:\s|/|>)").Count;
            WritePreliminaryPdfEvidence(partCount, minimumPages, pdf, rawPageCount);
            Assert.NotEmpty(pdf);
            Assert.True(rawPageCount >= minimumPages,
                $"PDF page-object floor failed: parts={partCount}; pages={rawPageCount}; minimum={minimumPages}.");
            Assert.Empty(pageErrors);
        }
        finally
        {
            foreach (var path in paths) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task EmptyDecorationKeepsRealUploadControlReachableAndHonorsReducedMotion(string culture, bool reducedMotion)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 820, Height = 800 },
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        await using var page = await context.NewPageAsync();
        await page.GotoAsync(quoteUrl,
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
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-workflow-parts] > [data-workflow-part]').length === 1",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        Assert.Equal(1, await page.Locator("[data-workflow-parts] > [data-workflow-part]").CountAsync());
        Assert.Equal(1, upload.StartedUploads);
        Assert.Equal(1, upload.VerifiedUploads);
        Assert.Equal(0, upload.ValidationRejectedUploads);
        var partId = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
        await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(partId);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task NativeFileDropsUseTheSameInputPipelineAcrossEmptyAndConfiguredWorkflow(string culture)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 820, Height = 800 },
        });
        await using var page = await context.NewPageAsync();
        await page.GotoAsync(quoteUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }
        await page.WaitForFunctionAsync(
            """
            () => {
              const zone = document.querySelector('.instant-quote__workflow');
              const input = document.querySelector('#instant-quote-files');
              if (!zone || !input || input._blazorInputFileNextFileId === undefined
                  || input.disabled || zone.getAttribute('aria-busy') === 'true') return false;
              const transfer = new DataTransfer();
              transfer.items.add(new File([], 'workflow-drop-readiness.stl', { type: 'model/stl' }));
              const probe = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: transfer });
              return !zone.dispatchEvent(probe) && probe.defaultPrevented;
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        var encoded = Convert.ToBase64String(WallThicknessParityBrowserFixture.Box(5));
        for (var index = 0; index < 2; index++)
        {
            var selector = index == 0 ? "[data-workflow-empty-dropzone]" : "[data-workflow-configuration]";
            var observed = await page.EvaluateAsync<JsonElement>(
                """
                args => {
                  const zone = document.querySelector('.instant-quote__workflow');
                  const input = document.querySelector('#instant-quote-files');
                  const target = zone.querySelector(args.selector);
                  if (!target || input.disabled) throw new Error('The expected upload target is not ready.');
                  let changes = 0;
                  const changed = () => { changes += 1; };
                  input.addEventListener('change', changed);
                  try {
                    const text = new DataTransfer();
                    text.setData('text/plain', 'not a model');
                    const ignored = target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: text }));
                    const bytes = Uint8Array.from(atob(args.encoded), character => character.charCodeAt(0));
                    const transfer = new DataTransfer();
                    transfer.items.add(new File([bytes], args.name, { type: 'model/stl' }));
                    input.disabled = true;
                    const refused = target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
                    const refusedChanges = changes;
                    input.disabled = false;
                    const over = target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: transfer }));
                    const accepted = target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: transfer }));
                    return { ignored, refused, refusedChanges, over, accepted, changes, files: input.files.length, name: input.files[0]?.name };
                  } finally {
                    input.disabled = false;
                    input.removeEventListener('change', changed);
                  }
                }
                """,
                new { selector, encoded, name = $"workflow-drop-{index}.stl" });
            Assert.True(observed.GetProperty("ignored").GetBoolean());
            Assert.False(observed.GetProperty("refused").GetBoolean());
            Assert.Equal(0, observed.GetProperty("refusedChanges").GetInt32());
            Assert.False(observed.GetProperty("over").GetBoolean());
            Assert.False(observed.GetProperty("accepted").GetBoolean());
            Assert.Equal(1, observed.GetProperty("changes").GetInt32());
            Assert.Equal(1, observed.GetProperty("files").GetInt32());
            Assert.Equal($"workflow-drop-{index}.stl", observed.GetProperty("name").GetString());
            await page.WaitForFunctionAsync(
                """
                count => document.querySelectorAll('[data-workflow-parts] > [data-workflow-part]').length === count
                  && document.querySelector('[data-workflow-configuration]') !== null
                  && !document.querySelector('#instant-quote-files').disabled
                """,
                index + 1, new PageWaitForFunctionOptions { Timeout = 30_000 });
            Assert.Equal(index + 1, await page.Locator("[data-workflow-parts] > [data-workflow-part]").CountAsync());
            Assert.Contains($"workflow-drop-{index}.stl",
                await page.Locator("[data-workflow-parts]").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Equal(index + 1, upload.StartedUploads);
            Assert.Equal(index + 1, upload.VerifiedUploads);
            Assert.Equal(0, upload.ValidationRejectedUploads);
            var partId = Guid.Parse((await page.Locator("[data-workflow-part]").Nth(index).GetAttributeAsync("data-part-id"))!);
            await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(partId);
        }
    }

    private static async Task WaitForPreliminaryPdfImagesAndFontsAsync(IPage preview, int partCount)
    {
        await preview.EvaluateAsync(
            """
            async count => {
                const images = [...document.querySelectorAll('.iq-preliminary-quotation-logo, .iq-preliminary-quotation-thumbnail')];
                if (images.length !== count + 1) throw new Error('Unexpected preliminary PDF image count');
                let timer;
                try {
                    await Promise.race([
                        (async () => {
                            await document.fonts.ready;
                            for (const image of images) {
                                image.scrollIntoView({ block: 'center', behavior: 'instant' });
                                await new Promise(resolve => requestAnimationFrame(resolve));
                                await image.decode();
                                if (!image.complete || image.naturalWidth <= 0) throw new Error('Undecoded preliminary PDF image');
                            }
                            if (document.fonts.status !== 'loaded') throw new Error('Preliminary PDF fonts unavailable');
                            window.scrollTo(0, 0);
                        })(),
                        new Promise((_, reject) => {
                            timer = setTimeout(() => reject(new Error('Preliminary PDF readiness exceeded 30s')), 30000);
                        })
                    ]);
                } finally {
                    clearTimeout(timer);
                }
            }
            """, partCount);
    }

    private static void WritePreliminaryPdfEvidence(int partCount, int minimumPages, byte[] bytes, int rawPageCount)
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("WEB_PRELIMINARY_PDF_EVIDENCE_DIRECTORY");
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "TestResults", "preliminary-pdf-proof")
            : Path.GetFullPath(configuredDirectory);
        Directory.CreateDirectory(directory);
        var stem = $"parts-{partCount:00}";
        var pdfPath = Path.Combine(directory, stem + ".pdf");
        WritePreliminaryPdfEvidenceAtomically(pdfPath, bytes);
        var manifest = new
        {
            schema = "preliminary-popup-pdf-v1",
            partCount,
            minimumPages,
            pdfFile = stem + ".pdf",
            byteLength = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            rawPageCount,
            format = "A4",
            printBackground = true,
            fixtureBoundary = "owned-program-kestrel-in-memory-upload-native-analysis-production-popup",
        };
        WritePreliminaryPdfEvidenceAtomically(Path.Combine(directory, stem + ".json"), System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })));
    }

    private static string CreatePreliminaryPdfBoxStl(int index)
    {
        var stem = $"maliev-pdf-part-{index:00}-{Guid.NewGuid():N}";
        try
        {
            return CreateBoxStl(20, 20, 5, stem);
        }
        catch
        {
            // Existing helper chooses its final random suffix internally. Own the unique
            // prefix before entering it so even a failed write can be cleaned safely.
            foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), stem + "-*.stl")) File.Delete(path);
            throw;
        }
    }

    private static void WritePreliminaryPdfEvidenceAtomically(string target, byte[] bytes)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }


    [Fact]
    public async Task RealUploadReadiness_WaitsForTheCurrentInputFileBindingWhileBootstrapIsHeld()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(origin.Port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        using var response = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync();
        var bootstrapEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBootstrap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = new List<Task>();
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await page.RouteAsync("**/_framework/blazor.web.js", async route =>
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (callbacks) callbacks.Add(completion.Task);
            bootstrapEntered.TrySetResult();
            try
            {
                await releaseBootstrap.Task.WaitAsync(lease.Token);
                await route.ContinueAsync();
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (lease.IsCancellationRequested) { completion.TrySetResult(); }
            catch (PlaywrightException) when (page.IsClosed) { completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        var path = CreateBoxStl(20, 20, 5);
        Exception? primary = null;
        Task? ready = null;
        try
        {
            var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
            await page.GotoAsync(quoteUrl, new() { WaitUntil = WaitUntilState.Commit, Timeout = 30000 });
            await bootstrapEntered.Task.WaitAsync(lease.Token);
            await page.Locator("#instant-quote-files").WaitForAsync(new()
            {
                State = WaitForSelectorState.Attached,
                Timeout = Math.Max(1, 30000 - readinessTimer.ElapsedMilliseconds),
            });
            Assert.True(await page.EvaluateAsync<bool>(
                "() => typeof document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId === 'undefined'"));
            ready = WaitForInteractiveInputFileAsync(page, readinessTimer);
            Assert.False(ready.IsCompleted);
            Assert.Equal(0, upload.VerifiedUploads);
            releaseBootstrap.TrySetResult();
            await ready;
            Task[] started;
            lock (callbacks) started = callbacks.ToArray();
            await Task.WhenAll(started).WaitAsync(lease.Token);
            Assert.True(await page.EvaluateAsync<bool>(
                "() => typeof document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId === 'number'"));
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await page.SetInputFilesAsync("#instant-quote-files", path);
            await page.Locator("[data-workflow-price-tier]").Last.WaitForAsync();
            var partId = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
            await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(partId);
            Assert.Equal(1, upload.StartedUploads);
            Assert.Equal(1, upload.VerifiedUploads);
            var stored = await factory.Services.GetRequiredService<IInstantQuotationSessionStore>()
                .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
            Assert.NotNull(stored);
            Assert.NotNull(stored.QuoteAuthorization);
            var part = Assert.Single(stored.Parts);
            Assert.Equal(partId, part.PartId);
            var receipt = Assert.Single(stored.PhysicalReceipts!,
                candidate => candidate.MaterialKey == part.Configuration.MaterialKey);
            Assert.Equal(partId, receipt.PartId);
            Assert.Equal(stored.SessionId, receipt.SessionId);
            Assert.Equal(part.Geometry.Sha256, receipt.UploadSha256);
            Assert.True(receipt.MotionSeconds > 0);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { releaseBootstrap.TrySetResult(); lease.Cancel(); return ValueTask.CompletedTask; },
                async () => await page.CloseAsync(),
                async () =>
                {
                    Task[] started;
                    lock (callbacks) started = callbacks.ToArray();
                    await Task.WhenAll(started).WaitAsync(TimeSpan.FromSeconds(5));
                },
                async () =>
                {
                    if (ready is not null)
                    {
                        try { await ready.WaitAsync(TimeSpan.FromSeconds(5)); }
                        catch (PlaywrightException) when (page.IsClosed) { }
                    }
                },
                () => { File.Delete(path); return ValueTask.CompletedTask; });
        }
    }

    private async Task ObserveRealUploadFailureAsync(Exception original, BrowserFailureEvidence evidence,
        IPage page, RealUploadTestingWebApplicationFactory factory, HashCheckingUploadClient upload)
    {
        await evidence.AttachAsync(original, async () =>
        {
            // Only fixed categories and bounded geometry; never DOM text, file names or session identities.
            var rendered = await page.EvaluateAsync<string>("""
                () => {
                    const number = value => Number.isFinite(value) ? Math.round(Math.max(-100000, Math.min(100000, value)) * 100) / 100 : null;
                    const rect = selector => {
                        const element = document.querySelector(selector), r = element?.getBoundingClientRect();
                        return r ? [r.left, r.top, r.right, r.bottom, r.width, r.height].map(number) : null;
                    };
                    const count = selector => Math.min(32, document.querySelectorAll(selector).length);
                    const zone = document.querySelector('.instant-quote__workflow');
                    const state = zone?.dataset.workflowState;
                    const rail = document.querySelector('[data-workflow-part-list]');
                    const canvas = document.querySelector('[data-workflow-viewer] > canvas');
                    const add = document.querySelector('[data-workflow-parts] .iq-compact-dropzone');
                    return JSON.stringify({
                        workflow: ['empty','uploading','uploaded','error','multipart','configured','review','customerdetails','submitted'].includes(state) ? state : 'unavailable',
                        viewport: [innerWidth, innerHeight, scrollX, scrollY].map(number),
                        rail: rect('[data-workflow-part-list]'), add: rect('[data-workflow-parts] .iq-compact-dropzone'),
                        selected: rect('[data-workflow-part]:has(button[aria-pressed="true"])'),
                        viewer: rect('[data-workflow-viewer]'), canvas: rect('[data-workflow-viewer] > canvas'),
                        backing: canvas ? [canvas.width, canvas.height].map(number) : null,
                        railScroll: rail ? [rail.scrollLeft, rail.scrollTop, rail.scrollWidth, rail.scrollHeight, rail.clientWidth, rail.clientHeight].map(number) : null,
                        railVisibleOverflow: !!rail && getComputedStyle(rail).overflowY === 'visible',
                        addSticky: !!add && getComputedStyle(add).position === 'sticky',
                        parts: count('[data-workflow-part]'), failed: count('[data-workflow-failed-preview]'),
                        pending: count('[data-workflow-pending-parse]'), uploadErrors: count('[data-upload-error]'),
                        reviewEnabled: !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)'),
                        progress: zone?.getAttribute('aria-busy') === 'true', repricing: !!document.querySelector('[data-pricing-loading-status]'),
                        inputReady: typeof document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId === 'number'
                    });
                }
                """);
            if (rendered.Length > 4096) throw new FormatException();
            output.WriteLine("[real-upload-geometry] " + rendered);
            using var document = JsonDocument.Parse(rendered);
            var state = document.RootElement.GetProperty("workflow").GetString();
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var session = await factory.Services.GetRequiredService<IInstantQuotationSessionStore>()
                    .GetAsync(upload.SessionId, upload.OwnerIdentity, deadline.Token);
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    Kind = "real-upload-authority",
                    SessionPresent = session is not null,
                    Authorized = session?.QuoteAuthorization is not null,
                    Parts = Math.Min(session?.Parts.Count ?? 0, 32),
                    Started = Math.Min(upload.StartedUploads, 32),
                    Verified = Math.Min(upload.VerifiedUploads, 32),
                    Rejected = Math.Min(upload.ValidationRejectedUploads, 32),
                    ExceptionTypes = factory.ExceptionKinds.Take(20).Select(kind => kind.Split(':')[0]).ToArray(),
                }));
            }
            catch { /* Secondary service observation must preserve the browser failure. */ }
            return JsonSerializer.Serialize(new { workflow = state });
        }, metadata => output.WriteLine("[real-upload-failure] " + metadata));
    }

    // Network quiet and SSR markup do not prove that this DOM input has a live .NET change binding.
    private static async Task WaitForInteractiveInputFileAsync(
        IPage page, System.Diagnostics.Stopwatch readinessTimer)
    {
        var remaining = 30000 - readinessTimer.ElapsedMilliseconds;
        if (remaining <= 0) throw new TimeoutException("The current file input was not ready within the navigation readiness budget.");
        var binding = await page.WaitForFunctionAsync(
            "() => { const input = document.querySelector('#instant-quote-files'); return !!input && !input.disabled && typeof input._blazorInputFileNextFileId === 'number'; }",
            options: new() { Timeout = remaining });
        await binding.DisposeAsync();
    }

    private static int ReserveFreePort() => BrowserTestServerPort.Reserve();

    [Theory]
    [InlineData("en", 1440)]
    [InlineData("th", 390)]
    public async Task RecoveredUpload_ShowsCurrentReceiptWarningWithoutManualAnalysisAndPreservesOriginalFile(
        string culture, int width)
    {
        var upload = new HashCheckingUploadClient { AllowVerifiedRemoval = true, OwnPhysicalReadLifetime = true };
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        factory.UseKestrel(origin.Port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync($"/instantquotation/3d-printing?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
        });
        await using var page = await context.NewPageAsync();
        using var lease = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
        var closedPath = string.Empty;
        var recoveryPath = string.Empty;
        Exception? primary = null;
        try
        {
            await page.AddInitScriptAsync("""
                (() => {
                  if (window !== window.top) return;
                  const records = [];
                  const clicks = [];
                  const snapshot = input => {
                    const addDisabled = document.querySelector('[data-workflow-add-file]')?.getAttribute('aria-disabled');
                    return {
                      disabled: input ? input.disabled : null,
                      bound: typeof input?._blazorInputFileNextFileId === 'number',
                      files: Math.min(100, input?.files?.length ?? 0),
                      parts: Math.min(32, document.querySelectorAll('[data-workflow-part]').length),
                      addFileDisabled: addDisabled === 'true' ? true : addDisabled === 'false' ? false : null
                    };
                  };
                  window.__malievRecoveredFileChanges = {
                    records, clicks,
                    current: () => snapshot(document.querySelector('#instant-quote-files'))
                  };
                  document.addEventListener('change', event => {
                    const input = event.target;
                    if (!(input instanceof HTMLInputElement) || input.id !== 'instant-quote-files' || records.length >= 4) return;
                    const record = { before: snapshot(input), after: null };
                    records.push(record);
                    queueMicrotask(() => { record.after = snapshot(input); });
                  }, true);
                  document.addEventListener('click', event => {
                    const target = event.target;
                    if (!(target instanceof Element) || clicks.length >= 4) return;
                    const card = target.closest('[data-workflow-material-picker] [data-material-selector] [role="radio"]');
                    if (!card) return;
                    const information = card.querySelector('.material-card__heading button');
                    clicks.push({ requestedPetg: card.getAttribute('data-material-key') === 'PETG',
                      targetIsInformationButton: !!information && information.contains(target),
                      x: Math.round(event.clientX * 100) / 100, y: Math.round(event.clientY * 100) / 100,
                      connected: card.isConnected, checked: card.getAttribute('aria-checked') === 'true',
                      informationExpanded: information?.getAttribute('aria-expanded') === 'true' });
                  }, true);
                })();
                """);
            closedPath = CreateBoxStl(10, 10, 10, "strict-original");
            recoveryPath = CreateRecoverableOpenBoxStl();
            var readinessTimer = System.Diagnostics.Stopwatch.StartNew();
            await page.GotoAsync(new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await WaitForInteractiveInputFileAsync(page, readinessTimer);
            await page.SetInputFilesAsync("#instant-quote-files", closedPath);
            await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(1);
            await MaterialCardBrowserActions.SelectAsync(page, "PETG");
            var strict = await CurrentReceiptAsync(null, "PETG", 1, Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard);
            var strictPart = Assert.Single(strict.Parts);
            Assert.Null(CurrentReceipt(strict, strictPart.PartId).GeometryReconstruction);
            await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-workflow-configuration] [data-geometry-reconstruction]")).ToHaveCountAsync(0);

            await page.SetInputFilesAsync("#instant-quote-files", recoveryPath);
            await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(2);
            var recoveryRow = page.Locator("[data-workflow-part]").Filter(new LocatorFilterOptions { HasText = Path.GetFileName(recoveryPath) });
            await recoveryRow.Locator("button").First.ClickAsync();
            var recoveryId = Guid.Parse((await recoveryRow.GetAttributeAsync("data-part-id"))!);
            var beforeBulk = await CurrentReceiptAsync(recoveryId, "PLA", 1, Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard);
            var beforeBulkPart = Assert.Single(beforeBulk.Parts, part => part.PartId == recoveryId);
            Assert.NotNull(CurrentReceipt(beforeBulk, recoveryId).GeometryReconstruction);
            await recoveryRow.Locator("[data-workflow-part-select]").CheckAsync();
            var strictRow = page.Locator($"[data-workflow-part][data-part-id='{strictPart.PartId}']");
            await strictRow.Locator("button").First.ClickAsync();
            await page.Locator("[data-workflow-apply-material]").ClickAsync();
            var current = await CurrentReceiptAsync(recoveryId, "PETG", 1, Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard);
            var afterBulkPart = Assert.Single(current.Parts, part => part.PartId == recoveryId);
            Assert.Equal("Any", afterBulkPart.Configuration.Color);
            Assert.Equal(beforeBulkPart.Configuration.Quantity, afterBulkPart.Configuration.Quantity);
            Assert.Equal(beforeBulkPart.Configuration.BuildPreference, afterBulkPart.Configuration.BuildPreference);
            Assert.Equal(beforeBulkPart.PhysicalAnalysisUpload, afterBulkPart.PhysicalAnalysisUpload);
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.JsonSerializer.SerializeToNode(beforeBulkPart.Geometry),
                System.Text.Json.JsonSerializer.SerializeToNode(afterBulkPart.Geometry)));
            Assert.Equal(strictPart.Configuration, Assert.Single(current.Parts, part => part.PartId == strictPart.PartId).Configuration);
            await recoveryRow.Locator("button").First.ClickAsync();
            var original = Assert.Single(current.Parts, part => part.PartId == recoveryId);
            var receipt = CurrentReceipt(current, recoveryId);
            Assert.Equal("voxel-majority-v1", receipt.GeometryReconstruction!.Method);
            Assert.Equal(0.2, receipt.GeometryReconstruction.VoxelSizeMm);
            Assert.True(receipt.DepositedMm3 > 0);
            Assert.True(receipt.MotionSeconds > 0);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(recoveryPath, lease.Token))), receipt.UploadSha256);
            Assert.Equal(original.PhysicalAnalysisUpload!.FileId, receipt.FileId);
            Assert.Equal(original.UploadReference.Value, receipt.FileId.ToString("D"));
            Assert.NotNull(current.QuoteAuthorization);
            var warning = page.Locator("[data-workflow-configuration] [data-geometry-reconstruction]");
            await Microsoft.Playwright.Assertions.Expect(warning).ToBeVisibleAsync();
            Assert.Contains(culture == "th" ? "ต้องให้วิศวกรตรวจสอบ" : "engineering review required",
                await warning.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains(culture == "th" ? "ต้องตรวจสอบไฟล์ต้นฉบับก่อนผลิต" : "the original file must be reviewed before manufacture",
                await warning.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Equal("status", await warning.GetAttributeAsync("role"));
            var priceText = await page.Locator("[data-workflow-part-price] > div:not([data-workflow-selected-print-time]):not([data-workflow-price-unavailable]) > dd").First.InnerTextAsync();
            Assert.True(double.TryParse(priceText.Replace("฿", "", StringComparison.Ordinal),
                System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price) && price > 0,
                "The recovered part must retain a positive server-derived displayed unit price.");

            await page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").ClickAsync();
            var reviewPart = page.Locator($"[data-workflow-review-part][data-review-part-id='{recoveryId}']");
            var details = reviewPart.Locator("[data-review-part-details]");
            await details.Locator("summary").ClickAsync();
            await details.Locator("summary").FocusAsync();
            var warnings = await reviewPart.Locator("[data-review-dfm-warnings] li").AllInnerTextsAsync();
            Assert.Contains(warnings, value => value.Contains(culture == "th" ? "ต้องให้วิศวกรตรวจสอบ" : "engineering review required", StringComparison.Ordinal));
            Assert.True(await details.EvaluateAsync<bool>("element => element.open"));
            await using (var preview = await page.RunAndWaitForPopupAsync(() => page.Locator("#preliminary-quotation-button").ClickAsync()))
            {
                await preview.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                var printable = preview.Locator(".iq-preliminary-quotation-part").Filter(
                    new LocatorFilterOptions { HasText = Path.GetFileName(recoveryPath) });
                Assert.Equal(warnings, await printable.Locator(".iq-preliminary-quotation-warnings li").AllInnerTextsAsync());
            }
            Assert.True(await details.EvaluateAsync<bool>("element => element.open"));
            Assert.True(await page.Locator("#preliminary-quotation-button").EvaluateAsync<bool>("element => document.activeElement === element"));
            await page.Locator("[data-review-back]").ClickAsync();

            upload.BeforePhysicalRead = async (partId, token) =>
            {
                if (partId != recoveryId) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            };
            var quantity = page.Locator($"#quantity-{recoveryId}");
            await quantity.FillAsync("2");
            await quantity.PressAsync("Tab");
            await entered.Task.WaitAsync(lease.Token);
            await Microsoft.Playwright.Assertions.Expect(warning).ToHaveCountAsync(0);
            release.TrySetResult();
            upload.BeforePhysicalRead = null;
            current = await CurrentReceiptAsync(recoveryId, "PETG", 2, Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard);
            await Microsoft.Playwright.Assertions.Expect(warning).ToBeVisibleAsync();
            await MaterialCardBrowserActions.SelectAsync(page, "ABS");
            current = await CurrentReceiptAsync(recoveryId, "ABS", 2, Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard);
            await page.Locator($"input[name='build-preference-{recoveryId}'][value='strength']").CheckAsync();
            current = await CurrentReceiptAsync(recoveryId, "ABS", 2, Legacy.Maliev.Web.Application.Pricing.BuildPreference.Strength);
            var repriced = Assert.Single(current.Parts, part => part.PartId == recoveryId);
            Assert.Equal(original.UploadReference, repriced.UploadReference);
            Assert.Equal(original.PhysicalAnalysisUpload, repriced.PhysicalAnalysisUpload);
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.JsonSerializer.SerializeToNode(original.Geometry),
                System.Text.Json.JsonSerializer.SerializeToNode(repriced.Geometry)),
                "The original admitted geometry must remain unchanged by voxel pricing.");
            Assert.NotNull(CurrentReceipt(current, recoveryId).GeometryReconstruction);
            Assert.NotNull(current.QuoteAuthorization);
            Assert.Equal(strictPart.UploadReference, Assert.Single(current.Parts, part => part.PartId == strictPart.PartId).UploadReference);
            await recoveryRow.Locator("button").Last.ClickAsync();
            await Microsoft.Playwright.Assertions.Expect(recoveryRow).ToHaveCountAsync(0);
            await Microsoft.Playwright.Assertions.Expect(warning).ToHaveCountAsync(0);
            current = await CurrentReceiptAsync(strictPart.PartId, "PETG", 1, Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard);
            Assert.DoesNotContain(current.Parts, part => part.PartId == recoveryId);
            Assert.DoesNotContain(current.PhysicalReceipts!, value => value.PartId == recoveryId);
            Assert.Null(CurrentReceipt(current, strictPart.PartId).GeometryReconstruction);
            Assert.NotNull(current.QuoteAuthorization);
            Assert.Equal(2, upload.VerifiedUploads);
            Assert.Equal(0, upload.ValidationRejectedUploads);
            await page.GotoAsync("about:blank");
            Assert.DoesNotContain(upload.PendingPhysicalReads, task => !task.IsCompleted);

            InstantQuotationPhysicalAnalysisReceipt CurrentReceipt(InstantQuotationSessionState state, Guid partId)
            {
                Assert.Equal(upload.SessionId, state.SessionId);
                Assert.Equal(upload.OwnerIdentity, state.OwnerIdentity);
                var part = Assert.Single(state.Parts, item => item.PartId == partId);
                return Assert.Single(state.PhysicalReceipts!, item => MatchesCurrentReceipt(item, state, part));
            }

            static bool MatchesCurrentReceipt(InstantQuotationPhysicalAnalysisReceipt candidate,
                InstantQuotationSessionState state, InstantQuotationPart part) =>
                part.PhysicalAnalysisUpload is { } source
                && candidate.SessionId == state.SessionId && candidate.OwnerIdentity == state.OwnerIdentity
                && candidate.PartId == part.PartId && candidate.FileId == source.FileId
                && string.Equals(candidate.UploadSha256, source.Sha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.UploadSha256, part.Geometry.Sha256, StringComparison.OrdinalIgnoreCase)
                && candidate.GltfUnitPolicy == source.GltfUnitPolicy
                && candidate.MaterialKey == part.Configuration.MaterialKey
                && candidate.ConfiguredMaterialKey == part.Configuration.MaterialKey
                && candidate.Quantity == part.Configuration.Quantity
                && candidate.BuildPreference == part.Configuration.BuildPreference;

            async Task<InstantQuotationSessionState> CurrentReceiptAsync(
                Guid? partId, string material, int quantityValue, Legacy.Maliev.Web.Application.Pricing.BuildPreference preference)
            {
                while (true)
                {
                    var state = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, lease.Token).WaitAsync(lease.Token);
                    var part = partId is { } target
                        ? state?.Parts.SingleOrDefault(item => item.PartId == target) : state?.Parts.SingleOrDefault();
                    var value = state is not null && part is not null
                        ? state.PhysicalReceipts?.SingleOrDefault(item => MatchesCurrentReceipt(item, state, part)) : null;
                    if (state?.QuoteAuthorization is not null && value is not null && value.MaterialKey == material
                        && value.Quantity == quantityValue && value.BuildPreference == preference)
                    {
                        Assert.Equal(upload.SessionId, state.SessionId);
                        Assert.Equal(upload.OwnerIdentity, state.OwnerIdentity);
                        Assert.Equal(value, CurrentReceipt(state, value.PartId));
                        return state;
                    }
                    await Task.Delay(25, lease.Token);
                }
            }
        }
        catch (Exception error)
        {
            primary = error;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var capture = page.EvaluateAsync<string>("""
                    () => {
                      const state = window.__malievRecoveredFileChanges;
                      return JSON.stringify(state ? { changes: state.records.slice(0, 4),
                        clicks: state.clicks.slice(0, 4), current: state.current() } : { unavailable: true });
                    }
                    """);
                _ = capture.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                var snapshot = await capture.WaitAsync(deadline.Token);
                if (snapshot.Length <= 4096) output.WriteLine("[recovered-input-state] " + snapshot);
            }
            catch { /* Secondary observation must preserve the original failure. */ }
            throw;
        }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                () => { release.TrySetResult(); upload.BeforePhysicalRead = null; return ValueTask.CompletedTask; },
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; },
                () => { if (closedPath.Length > 0) File.Delete(closedPath); return ValueTask.CompletedTask; },
                () => { if (recoveryPath.Length > 0) File.Delete(recoveryPath); return ValueTask.CompletedTask; });
        }
    }

    /// <summary>Checks the original optional private nine-file batch through real browser admission and server physical analysis.</summary>
    /// <returns>A task that completes after all nine files have protected positive prices and exactly seven selected receipts record reconstruction.</returns>
    [Fact]
    public async Task PrivateOriginalBatch_PricesEveryAcceptedFile()
    {
        var directory = Environment.GetEnvironmentVariable("MALIEV_MIXED_BATCH_FIXTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var files = Directory.GetFiles(directory, "*.stl", SearchOption.TopDirectoryOnly);
        Assert.Equal(9, files.Length);
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true, OwnUploadLifetime = true };
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService());
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        factory.UseKestrel(origin.Port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        var url = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync(new()
        { ViewportSize = new() { Width = 1440, Height = 1000 } });
        Exception? primary = null;
        TimeoutException? batchTimeout = null;
        try
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await page.WaitForFunctionAsync("""
                () => typeof document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId === 'number'
                """);
            await page.SetInputFilesAsync("#instant-quote-files", files);
            try
            {
                await page.WaitForFunctionAsync("""
                    () => document.querySelectorAll('[data-workflow-part]').length === 9
                      && document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button')?.disabled === false
                      && !document.querySelector('[data-workflow-pending-parse], [data-pricing-loading-status]')
                    """, null, new() { Timeout = 240000 });
            }
            catch (TimeoutException error) { batchTimeout = error; }
            await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(9);
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await Assertions.Expect(review).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("[data-workflow-failed-preview], [data-workflow-upload-item][data-upload-error]"))
                .ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("[data-workflow-pending-parse], [data-pricing-loading-status]"))
                .ToHaveCountAsync(0);
            Assert.Equal(9, upload.StartedUploads);
            Assert.Equal(9, upload.VerifiedUploads);
            Assert.Equal(0, upload.ValidationRejectedUploads);
            var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
            var current = Assert.IsType<InstantQuotationSessionState>(
                await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default));
            Assert.Equal(9, current.Parts.Count);
            Assert.Equal(9, current.Parts.Select(part => part.PartId).Distinct().Count());
            var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(current.QuoteAuthorization);
            Assert.Equal(9, authorization.LineTickets.Count);
            Assert.Equal(9, authorization.LineTickets.Distinct(StringComparer.Ordinal).Count());
            var reconstructed = 0;
            foreach (var part in current.Parts)
            {
                var file = Assert.IsType<InstantQuotationPhysicalAnalysisUpload>(part.PhysicalAnalysisUpload);
                Assert.Equal(file.FileId.ToString("D"), part.UploadReference.Value);
                Assert.Equal(part.Geometry.Sha256, file.Sha256);
                var receipt = Assert.Single(current.PhysicalReceipts!, candidate =>
                    candidate.PartId == part.PartId && candidate.MaterialKey == part.Configuration.MaterialKey);
                Assert.Equal(current.SessionId, receipt.SessionId);
                Assert.Equal(upload.OwnerIdentity, receipt.OwnerIdentity);
                Assert.Equal(file.FileId, receipt.FileId);
                Assert.Equal(file.Sha256, receipt.UploadSha256);
                Assert.Equal(part.Configuration.Quantity, receipt.Quantity);
                Assert.True(receipt.DepositedMm3 > 0);
                Assert.True(receipt.MotionSeconds > 0);
                if (receipt.GeometryReconstruction is { } reconstruction)
                {
                    Assert.Equal("voxel-majority-v1", reconstruction.Method);
                    reconstructed++;
                }
                var row = page.Locator($"[data-workflow-part][data-part-id='{part.PartId:D}']");
                await row.Locator("button[aria-label^='View']").ClickAsync();
                await page.WaitForFunctionAsync(
                    "id => document.querySelector('[data-workflow-material-picker] input[name=quantity]')?.id === 'quantity-' + id",
                    part.PartId.ToString("D"));
                await Assertions.Expect(row.Locator("button[aria-pressed]")).ToHaveAttributeAsync("aria-pressed", "true");
                await Assertions.Expect(page.Locator($"[data-workflow-material-picker] #quantity-{part.PartId:D}"))
                    .ToHaveCountAsync(1);
                var priceDetails = page.Locator("[data-workflow-configuration] [data-workflow-part-price]");
                await Assertions.Expect(priceDetails).ToHaveCountAsync(1);
                var priceText = await priceDetails.Locator(
                    ":scope > div:not([data-workflow-selected-print-time]):not([data-workflow-price-unavailable]) > dd")
                    .First.InnerTextAsync();
                Assert.True(double.TryParse(priceText.Replace("฿", "", StringComparison.Ordinal),
                    System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price)
                    && price > 0, "Every accepted private-corpus part must have a positive server-derived displayed unit price.");
            }
            Assert.Equal(7, reconstructed);
            Assert.True(System.Text.Json.JsonSerializer.Serialize(current) ==
                System.Text.Json.JsonSerializer.Serialize(await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default)),
                "Viewing each private-corpus part must not change its protected session or quote authorization.");
            await Assertions.Expect(review).ToBeEnabledAsync();
            if (batchTimeout is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(batchTimeout).Throw();
        }
        catch (Exception error)
        {
            primary = batchTimeout ?? error;
            if (batchTimeout is not null && !ReferenceEquals(batchTimeout, error))
                batchTimeout.Data["PrivateCorpusFinalStateFailure"] = error;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () =>
                {
                    var evidence = Environment.GetEnvironmentVariable("MALIEV_MIXED_BATCH_EVIDENCE_DIR");
                    if (string.IsNullOrWhiteSpace(evidence)) return;
                    Directory.CreateDirectory(evidence);
                    var state = upload.VerifiedUploads > 0
                        ? await factory.Services.GetRequiredService<IInstantQuotationSessionStore>()
                            .GetAsync(upload.SessionId, upload.OwnerIdentity, default)
                        : null;
                    var boundaries = await page.EvaluateAsync<int[]>("""
                        () => [
                          document.querySelectorAll('[data-workflow-part]').length,
                          document.querySelectorAll('[data-workflow-pending-parse]').length,
                          document.querySelectorAll('[data-workflow-failed-preview], [data-workflow-upload-item][data-upload-error]').length,
                          document.querySelectorAll('[data-pricing-loading-status]').length,
                          document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button')?.disabled === false ? 1 : 0
                        ]
                        """);
                    var receipts = state?.PhysicalReceipts;
                    // The explicit operator-owned destination receives no filenames, raw geometry, hashes or customer state.
                    await File.WriteAllTextAsync(Path.Combine(evidence, "original-batch.json"),
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            BatchTimedOut = batchTimeout is not null,
                            Boundaries = boundaries,
                            upload.StartedUploads,
                            upload.VerifiedUploads,
                            upload.ValidationRejectedUploads,
                            HasAuthorization = state?.QuoteAuthorization is not null,
                            TicketCount = state?.QuoteAuthorization?.LineTickets.Count ?? 0,
                            Parts = state?.Parts.Take(9).Select((part, index) => new
                            {
                                Ordinal = index + 1,
                                HasPhysicalUpload = part.PhysicalAnalysisUpload is not null,
                                HasSelectedReceipt = receipts?.Any(receipt =>
                                    receipt.PartId == part.PartId && receipt.MaterialKey == part.Configuration.MaterialKey) == true,
                                Reconstructed = receipts?.Any(receipt =>
                                    receipt.PartId == part.PartId && receipt.MaterialKey == part.Configuration.MaterialKey
                                    && receipt.GeometryReconstruction is not null) == true,
                            }).ToArray(),
                        }));
                    await page.ScreenshotAsync(new()
                    {
                        Path = Path.Combine(evidence, "original-batch.png"),
                        FullPage = true,
                        Mask = [page.Locator(".instant-quote__workflow")],
                    });
                },
                () => { upload.StopUploadAdmission(); return ValueTask.CompletedTask; },
                async () => await CloseOwnedPageAsync(page, playwright),
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                async () => await upload.DrainUploadsAsync(),
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                () => { upload.DisposeUploadLifetime(); return ValueTask.CompletedTask; },
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; });
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task OriginalLightweightGlb_MaterialCardsKeepUniformHeightAcrossSearch(int partCount)
    {
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true, OwnUploadLifetime = true };
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService());
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        factory.UseKestrel(origin.Port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        var url = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync(new()
        { ViewportSize = new() { Width = 1920, Height = 1080 } });
        Exception? primary = null;
        try
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await page.WaitForFunctionAsync("""
                () => typeof document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId === 'number'
                """);
            var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
            Assert.Equal(960, bytes.Length);
            Assert.Equal("2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc",
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
            await page.Locator("[data-workflow-gltf-unit]").SelectOptionAsync("millimetres");
            await page.SetInputFilesAsync("#instant-quote-files", Enumerable.Range(0, partCount).Select(index => new FilePayload
            {
                Name = $"card-layout-{index}.glb",
                MimeType = "model/gltf-binary",
                Buffer = bytes,
            }));
            await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(partCount,
                new() { Timeout = 90000 });
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
            await page.WaitForFunctionAsync("""
                () => !document.querySelector('[data-pricing-loading-status]')
                  && !document.querySelector('[data-material-card-price][data-price-status="pending"]')
                """, null, new() { Timeout = 30000 });
            await page.EvaluateAsync("() => document.fonts.ready");
            var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
            var before = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
            Assert.Equal(partCount, upload.VerifiedUploads);
            Assert.Equal(partCount, before.Parts.Select(part => part.PartId).Distinct().Count());
            Assert.Equal(partCount, before.Parts.Select(part => part.PhysicalAnalysisUpload!.FileId).Distinct().Count());
            Assert.NotNull(before.QuoteAuthorization);
            Assert.All(before.Parts, part => Assert.Contains(before.PhysicalReceipts!, receipt =>
                receipt.PartId == part.PartId && receipt.FileId == part.PhysicalAnalysisUpload!.FileId
                && receipt.UploadSha256 == Convert.ToHexStringLower(SHA256.HashData(bytes))
                && receipt.DepositedMm3 > 0 && receipt.MotionSeconds > 0));
            var protectedBefore = System.Text.Json.JsonSerializer.Serialize(before);
            var selector = page.Locator("[data-material-selector]");
            var catalogue = MaterialCardPresentationCatalog.ForMaterials(PricingCatalog.Materials.Values
                .Select(material => new InstantQuotationWorkflowMaterialOption(material.Key, material.DisplayName)).ToArray());
            var allKeys = catalogue.Select(material => material.Key).ToArray();
            var toggle = selector.Locator("[data-material-toggle]");
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
            await toggle.ClickAsync();
            await WaitForKeysAsync(allKeys);
            async Task<int[]> VisibleHeightsAsync() => await selector.EvaluateAsync<int[]>("""
                element => Array.from(element.querySelectorAll('[role="radio"]'))
                  .filter(card => card.offsetParent !== null)
                  .map(card => Math.round(card.getBoundingClientRect().height))
                """);
            var unfiltered = await VisibleHeightsAsync();
            Assert.True(unfiltered.Length >= 2, "The unfiltered catalogue must contain at least two material cards.");
            Assert.Single(unfiltered.Distinct());
            var baseline = unfiltered[0];
            Assert.True(baseline > 0);
            var search = selector.Locator("[data-material-search]");
            foreach (var query in new[] { "PETG", "Nylon", "PLA" })
            {
                var expectedKeys = catalogue.Where(material => MaterialCardPresentationCatalog.Matches(
                    material, query, static key => key)).Select(material => material.Key).ToArray();
                Assert.NotEmpty(expectedKeys);
                Assert.True(expectedKeys.Length < allKeys.Length);
                await search.FillAsync(query);
                await WaitForKeysAsync(expectedKeys);
                var filtered = await VisibleHeightsAsync();
                Assert.True(filtered.Length < unfiltered.Length, $"Search {query} must narrow the catalogue.");
                Assert.True(filtered.Distinct().Count() == 1 && filtered[0] == baseline,
                    $"Search {query} changed the uniform {baseline}px material-card height.");
            }
            await search.FillAsync(string.Empty);
            await WaitForKeysAsync(allKeys);
            Assert.Equal(unfiltered, await VisibleHeightsAsync());
            Assert.Equal(protectedBefore, System.Text.Json.JsonSerializer.Serialize(
                (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!));
            await Assertions.Expect(review).ToBeEnabledAsync();

            Task WaitForKeysAsync(string[] expected) => page.WaitForFunctionAsync("""
                expected => {
                  const keys = Array.from(document.querySelectorAll('[data-material-selector] [role="radio"]'))
                    .filter(card => card.offsetParent !== null).map(card => card.dataset.materialKey);
                  return keys.length === expected.length && keys.every((key, index) => key === expected[index]);
                }
                """, expected, new() { Timeout = 10000 });
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { upload.StopUploadAdmission(); return ValueTask.CompletedTask; },
                async () => await CloseOwnedPageAsync(page, playwright),
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                async () => await upload.DrainUploadsAsync(),
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                () => { upload.DisposeUploadLifetime(); return ValueTask.CompletedTask; },
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; });
        }
    }

    [Fact]
    public async Task OwnedPhysicalReadCleanup_StopsLateAdmissionAndDrainsTheActiveRead()
    {
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Exception? primary = null;
        try
        {
            var active = upload.StartPhysicalRead(async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable);
            }, default);
            await entered.Task.WaitAsync(lease.Token);
            upload.StopPhysicalReadAdmission();
            var invoked = false;
            var late = upload.StartPhysicalRead(_ =>
            {
                invoked = true;
                return Task.FromResult(InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable));
            }, default);
            Assert.True(late.IsCompletedSuccessfully);
            Assert.Equal(InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable, (await late).Failure);
            Assert.False(invoked);
            Assert.Contains(active, upload.ClosedPhysicalReads);
            Assert.Same(active, Assert.Single(upload.ClosedPhysicalReads));
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => active.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(active.IsCanceled);
            Assert.All(upload.ClosedPhysicalReads, task => Assert.True(task.IsCompleted));
            Assert.Equal(upload.ClosedPhysicalReads.Length, upload.PendingPhysicalReads.Count);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { upload.StopPhysicalReadAdmission(); return ValueTask.CompletedTask; },
                () => { release.TrySetResult(); return ValueTask.CompletedTask; },
                async () =>
                {
                    try { await Task.WhenAll(upload.ClosedPhysicalReads).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) when (upload.ClosedPhysicalReads.All(task => task.IsCompleted)) { }
                },
                () => { upload.DisposePhysicalReadLifetime(); return ValueTask.CompletedTask; });
        }
    }

    [Fact]
    public async Task OwnedUploadCleanup_StopsLateAdmissionAndDrainsEveryActiveUpload()
    {
        var upload = new HashCheckingUploadClient { OwnUploadLifetime = true };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        upload.BeforeUpload = () =>
        {
            if (Interlocked.Increment(ref started) == 2) entered.TrySetResult();
            return release.Task;
        };
        var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var claim = new InstantQuotationGeometryClaim(1, sha, 10, 10, 10, 1000, 600,
            null, null, 12, 1, true, false, false, 10);
        using var firstStream = new MemoryStream(bytes);
        using var secondStream = new MemoryStream(bytes);
        using var lateStream = new MemoryStream(bytes);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Exception? primary = null;
        try
        {
            var first = upload.UploadAsync("owned-session", "owned-owner", firstStream,
                "first.glb", "model/gltf-binary", bytes.Length, claim, "first", default);
            var second = upload.UploadAsync("owned-session", "owned-owner", secondStream,
                "second.glb", "model/gltf-binary", bytes.Length, claim, "second", default);
            await entered.Task.WaitAsync(lease.Token);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            upload.StopUploadAdmission();
            var late = upload.UploadAsync("owned-session", "owned-owner", lateStream,
                "late.glb", "model/gltf-binary", bytes.Length, claim, "late", default);
            Assert.True(late.IsCompletedSuccessfully);
            Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, (await late).ProblemCategory);
            Assert.Equal(2, upload.StartedUploads);
            Assert.Equal(2, started);
            Assert.Collection(upload.ClosedUploads,
                task => Assert.Same(first, task), task => Assert.Same(second, task));
            Assert.Contains(first, upload.ClosedUploads);
            Assert.Contains(second, upload.ClosedUploads);
            Assert.Same(second, upload.LastUploadTask);
            Assert.All(upload.ClosedUploads, task => Assert.False(task.IsCompleted));
            release.TrySetResult();
            await upload.DrainUploadsAsync();
            Assert.True(first.IsCanceled);
            Assert.True(second.IsCanceled);
            Assert.All(upload.ClosedUploads, task => Assert.True(task.IsCompleted));
            Assert.Equal(0, upload.VerifiedUploads);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { upload.StopUploadAdmission(); return ValueTask.CompletedTask; },
                () => { release.TrySetResult(); return ValueTask.CompletedTask; },
                async () => await upload.DrainUploadsAsync(),
                () => { upload.DisposeUploadLifetime(); return ValueTask.CompletedTask; });
        }
    }

    private static async ValueTask CloseOwnedPageAsync(IPage page, IPlaywright playwright)
    {
        var close = page.CloseAsync();
        Observe(close);
        try { await close.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception closeFailure)
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(closeFailure,
                () => { playwright.Dispose(); return ValueTask.CompletedTask; },
                async () => await close.WaitAsync(TimeSpan.FromSeconds(5)));
            throw;
        }

        static void Observe(Task task)
        {
            _ = task.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static string CreateRecoverableOpenBoxStl()
    {
        var closed = CreateBoxStl(10, 10, 10, "recovered-original");
        try
        {
            var bytes = File.ReadAllBytes(closed);
            var open = new byte[84 + 10 * 50];
            bytes.AsSpan(0, 84).CopyTo(open);
            bytes.AsSpan(184).CopyTo(open.AsSpan(84));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(open.AsSpan(80, 4), 10);
            var path = Path.Combine(Path.GetTempPath(), $"recoverable-original-{Guid.NewGuid():N}.stl");
            File.WriteAllBytes(path, open);
            return path;
        }
        finally { File.Delete(closed); }
    }

    private sealed class SupersededPetgPricing : IDisposable
    {
        internal sealed class Pending(Guid partId)
        {
            internal Guid PartId { get; } = partId;
            internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal CancellationToken ConsumerToken { get; set; }
            internal Task<InstantQuotationOrderQuote?>? Actual { get; set; }
            internal bool Started { get; set; }
        }

        private readonly object gate = new();
        private readonly Dictionary<Guid, Pending> pending = [];
        private readonly List<Task<InstantQuotationOrderQuote?>> actual = [];
        private readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(5));
        private CancellationTokenSource? lease;
        private InstantQuotationOrderQuote? currentPlaQuote;
        private int activeCurrent;
        private bool admissionStopped;

        internal int ActiveCurrent => Volatile.Read(ref activeCurrent);
        internal InstantQuotationOrderQuote? CurrentPlaQuote => Volatile.Read(ref currentPlaQuote);

        internal void Arm(IEnumerable<Guid> partIds)
        {
            lock (gate)
            {
                if (lease is not null) throw new InvalidOperationException("The obsolete batch is already armed.");
                foreach (var id in partIds) pending.Add(id, new(id));
                lease = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                lease.CancelAfter(TimeSpan.FromSeconds(90));
            }
        }

        internal Task EnteredAsync(Guid partId)
        {
            lock (gate) return pending[partId].Entered.Task;
        }

        internal Pending[] Snapshot()
        {
            lock (gate) return pending.Values.Where(item => item.Started).ToArray();
        }

        internal void ReleaseAll()
        {
            lock (gate)
                foreach (var item in pending.Values) item.Release.TrySetResult();
        }

        internal void StopAdmission()
        {
            lock (gate) admissionStopped = true;
        }

        internal void Decorate(IServiceCollection services)
        {
            var original = services.Single(item =>
                item.ServiceType == typeof(IInstantQuotationAuthoritativePricingService));
            services.RemoveAll<IInstantQuotationAuthoritativePricingService>();
            services.Add(new ServiceDescriptor(typeof(IInstantQuotationAuthoritativePricingService), provider =>
                new Pricing((IInstantQuotationAuthoritativePricingService)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)), this),
                original.Lifetime));
        }

        private Task<InstantQuotationOrderQuote?> Start(IInstantQuotationAuthoritativePricingService inner,
            InstantQuotationSessionState session, string? owner, bool comparisons,
            Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask>? observer,
            CancellationToken consumer)
        {
            lock (gate)
            {
                if (admissionStopped)
                    return Task.FromCanceled<InstantQuotationOrderQuote?>(new CancellationToken(canceled: true));
                var selectedPetg = session.Parts.Where(part => part.Configuration.MaterialKey == "PETG").ToArray();
                Pending? hold = null;
                if (session.Parts.Count == 9 && selectedPetg.Length == 1
                    && pending.TryGetValue(selectedPetg[0].PartId, out var entry) && !entry.Started)
                {
                    hold = entry;
                    hold.Started = true;
                    hold.ConsumerToken = consumer;
                }

                var task = hold is null ? CurrentAsync() : ObsoleteAsync(hold);
                actual.Add(task);
                if (hold is not null) hold.Actual = task;
                _ = task.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return task;
            }

            Task<InstantQuotationOrderQuote?> Invoke(CancellationToken token) => observer is null
                ? inner.QuoteAsync(session, owner, comparisons, token)
                : inner.QuoteAsync(session, owner, comparisons, observer, token);

            async Task<InstantQuotationOrderQuote?> CurrentAsync()
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(consumer, lifetime.Token);
                Interlocked.Increment(ref activeCurrent);
                try
                {
                    var result = await Invoke(linked.Token);
                    if (result is not null && session.Parts.Count == 9
                        && session.Parts.All(part => part.Configuration.MaterialKey == "PLA"))
                        Volatile.Write(ref currentPlaQuote, result);
                    return result;
                }
                finally { Interlocked.Decrement(ref activeCurrent); }
            }

            async Task<InstantQuotationOrderQuote?> ObsoleteAsync(Pending item)
            {
                // Source defers obsolete simulations before invoking them; only this owned test task ignores supersession.
                var token = lease!.Token;
                item.Entered.TrySetResult();
                await item.Release.Task.WaitAsync(token);
                return await Invoke(token);
            }
        }

        internal async Task DrainAsync(Exception? primary)
        {
            Task<InstantQuotationOrderQuote?>[] tasks;
            CancellationToken token;
            lock (gate) { tasks = actual.ToArray(); token = lifetime.Token; }
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary, tasks.Select(task =>
                (Func<ValueTask>)(async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                    task, static () => { }, ReleaseAll, token, null,
                    receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                    expectedTerminalFailure: false, afterWaitFailure: lifetime.Cancel,
                    taskKind: "actual-original-glb-batch-price"))).ToArray());
        }

        internal async Task CancelAndDrainRemainingAsync()
        {
            Task<InstantQuotationOrderQuote?>[] tasks;
            lock (gate) tasks = actual.ToArray();
            if (tasks.Any(task => !task.IsCompleted)) lifetime.Cancel();
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (tasks.All(task => task.IsCompleted)) { }
        }

        public void Dispose()
        {
            lock (gate)
            {
                // A failed drain reports the failure; keep its finite cancellation lease alive until actual work settles.
                if (actual.Any(task => !task.IsCompleted)) return;
                lease?.Dispose();
                lifetime.Dispose();
            }
        }

        private sealed class Pricing(IInstantQuotationAuthoritativePricingService inner, SupersededPetgPricing owner)
            : IInstantQuotationAuthoritativePricingService
        {
            public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
                string? identity, bool comparisons, CancellationToken token) =>
                owner.Start(inner, session, identity, comparisons, null, token);

            public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
                string? identity, bool comparisons,
                Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
                CancellationToken token) => owner.Start(inner, session, identity, comparisons, observer, token);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ColorEditingPreservesSourceDefaultsHexRestoreAndMultipartIsolation(string culture)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        });
        await using var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(quoteUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }
        await WaitForUploadInputReadyAsync(page);

        // Two disconnected, individually closed shells: actual mesh analysis owns BodyCount.
        var shell = WallThicknessParityBrowserFixture.Box(5);
        var multipart = new byte[84 + (2 * (shell.Length - 84))];
        Array.Copy(shell, multipart, shell.Length);
        BitConverter.GetBytes(24u).CopyTo(multipart, 80);
        Array.Copy(shell, 84, multipart, shell.Length, shell.Length - 84);
        for (var triangle = 0; triangle < 12; triangle++)
        {
            for (var vertex = 0; vertex < 3; vertex++)
            {
                var xOffset = shell.Length + (triangle * 50) + 12 + (vertex * 12);
                BitConverter.GetBytes(BitConverter.ToSingle(multipart, xOffset) + 40f).CopyTo(multipart, xOffset);
            }
        }
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "color-single.stl",
            MimeType = "model/stl",
            Buffer = shell,
        });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-workflow-part]').length === 1 && !!document.querySelector('[data-workflow-price-tier]')",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        var singleId = Guid.Parse((await page.Locator("[data-workflow-part]").GetAttributeAsync("data-part-id"))!);
        var singleColor = page.Locator($"#color-{singleId}");
        await Assertions.Expect(page.Locator("[data-workflow-summary-configuration]")).ToHaveTextAsync("PLA 1×");
        Assert.Equal("Black", await singleColor.InputValueAsync());
        await singleColor.SelectOptionAsync("White");
        await MaterialCardBrowserActions.SelectAsync(page, "PETG");
        await page.WaitForFunctionAsync(
            "id => document.querySelector('#color-' + id)?.value === 'White' && !document.querySelector('[data-pricing-loading-status]')",
            singleId.ToString(), new PageWaitForFunctionOptions { Timeout = 60_000 });
        Assert.Equal("White", await singleColor.InputValueAsync());
        // A compatible named color survives; an unsupported custom color uses the source default.
        await MaterialCardBrowserActions.SelectAsync(page, "PLA");
        await page.Locator("[data-workflow-material-picker] input[type='color']").EvaluateAsync(
            "input => { input.value = '#123abc'; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await page.WaitForFunctionAsync(
            "id => document.querySelector('#color-' + id)?.value?.toLowerCase() === '#123abc' && !document.querySelector('[data-pricing-loading-status]')",
            singleId.ToString(), new PageWaitForFunctionOptions { Timeout = 60_000 });
        await MaterialCardBrowserActions.SelectAsync(page, "PETG");
        await page.WaitForFunctionAsync(
            "id => document.querySelector('#color-' + id)?.value === 'Any' && !document.querySelector('[data-pricing-loading-status]')",
            singleId.ToString(), new PageWaitForFunctionOptions { Timeout = 60_000 });
        Assert.Equal("Any", await singleColor.InputValueAsync());
        var singleStrip = page.Locator($"[data-workflow-part][data-part-id='{singleId}']");
        Assert.Contains("color-single.stl", await singleStrip.InnerTextAsync(), StringComparison.Ordinal);
        var quantity = page.Locator($"#quantity-{singleId}");
        await quantity.FillAsync("2");
        await quantity.DispatchEventAsync("change");
        await page.WaitForFunctionAsync(
            """
            id => {
              const part = document.querySelector('[data-workflow-part][data-part-id="' + id + '"]');
              const price = part?.querySelector('[data-workflow-summary-unit-price]')?.textContent?.trim();
              return part?.querySelector('[data-workflow-summary-configuration]')?.textContent?.trim() === 'PETG 2×'
                && price && price !== '—' && !document.querySelector('[data-pricing-loading-status]');
            }
            """,
            singleId.ToString(), new PageWaitForFunctionOptions { Timeout = 60_000 });
        var unitPriceLabel = culture == "th" ? "ราคาต่อชิ้น" : "Unit price";
        var configuredUnitPrice = page.Locator("[data-workflow-part-price] > div")
            .Filter(new LocatorFilterOptions { HasText = unitPriceLabel }).Locator("dd");
        Assert.Equal(await configuredUnitPrice.InnerTextAsync(),
            await singleStrip.Locator("[data-workflow-summary-unit-price]").InnerTextAsync());
        Assert.Equal(0, await page.Locator("[data-workflow-material-picker] input[type='color']").CountAsync());
        await MaterialCardBrowserActions.SelectAsync(page, "PLA");
        var custom = page.Locator("[data-workflow-material-picker] input[type='color']");
        await custom.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        await custom.EvaluateAsync(
            "(input, color) => { input.value = color; input.dispatchEvent(new Event('change', { bubbles: true })); }",
            "#123abc");
        await page.WaitForFunctionAsync(
            "id => document.querySelector('#color-' + id)?.value?.toLowerCase() === '#123abc' && !document.querySelector('[data-pricing-loading-status]')",
            singleId.ToString(), new PageWaitForFunctionOptions { Timeout = 60_000 });
        var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        var saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(saved);
        Assert.Equal("#123abc", Assert.Single(saved.Parts).Configuration.Color.ToLowerInvariant());

        Assert.Equal("#123abc", (await singleColor.InputValueAsync()).ToLowerInvariant());
        Assert.Equal(1, await custom.CountAsync());
        Assert.Equal(1, upload.VerifiedUploads);
        Assert.Equal("PLA 2×", (await singleStrip.Locator("[data-workflow-summary-configuration]").InnerTextAsync()).Trim());
        Assert.Equal(await configuredUnitPrice.InnerTextAsync(),
            await singleStrip.Locator("[data-workflow-summary-unit-price]").InnerTextAsync());
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "color-multipart.stl",
            MimeType = "model/stl",
            Buffer = multipart,
        });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-workflow-part]').length === 2 && !document.querySelector('#instant-quote-files').disabled",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(saved);
        var multi = Assert.Single(saved.Parts, part => part.PartId != singleId);
        Assert.Equal(2, multi.Geometry.BodyCount);
        Assert.Equal("Any", multi.Configuration.Color);
        var multiId = multi.PartId;
        await page.Locator($"[data-workflow-part][data-part-id='{multiId}'] button[aria-pressed]").ClickAsync();
        var multiColor = page.Locator($"#color-{multiId}");
        await multiColor.WaitForAsync();
        Assert.True(await multiColor.IsDisabledAsync());
        Assert.Equal("Any", await multiColor.InputValueAsync());
        Assert.Equal(0, await page.Locator("[data-workflow-material-picker] input[type='color']").CountAsync());
        await MaterialCardBrowserActions.SelectAsync(page, "M68");
        await page.WaitForFunctionAsync(
            "id => document.querySelector('#color-' + id)?.disabled && document.querySelector('#color-' + id)?.value === 'Any' && !document.querySelector('[data-pricing-loading-status]')",
            multiId.ToString(), new PageWaitForFunctionOptions { Timeout = 60_000 });
        saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(saved);
        Assert.Equal("M68", Assert.Single(saved.Parts, part => part.PartId == multiId).Configuration.MaterialKey);
        Assert.Equal("Any", Assert.Single(saved.Parts, part => part.PartId == multiId).Configuration.Color);
        Assert.Equal("#123abc", Assert.Single(saved.Parts, part => part.PartId == singleId).Configuration.Color.ToLowerInvariant());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(shell)).ToLowerInvariant(),
            Assert.Single(saved.Parts, part => part.PartId == singleId).Geometry.Sha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(multipart)).ToLowerInvariant(), multi.Geometry.Sha256);
        var multiStrip = page.Locator($"[data-workflow-part][data-part-id='{multiId}']");
        Assert.Equal("PLA 2×", (await singleStrip.Locator("[data-workflow-summary-configuration]").InnerTextAsync()).Trim());
        Assert.Equal("M68 1×", (await multiStrip.Locator("[data-workflow-summary-configuration]").InnerTextAsync()).Trim());
        await page.WaitForFunctionAsync(
            """
            () => {
              const images = [...document.querySelectorAll('[data-workflow-thumbnail]')];
              return images.length === 2 && images.every(image => image.complete && image.naturalWidth > 0
                && image.src.startsWith('data:image/webp'));
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        var singleSnapshot = singleStrip.Locator("[data-workflow-thumbnail]");
        var multiSnapshot = multiStrip.Locator("[data-workflow-thumbnail]");
        Assert.True(await singleSnapshot.EvaluateAsync<bool>("async image => { await image.decode(); return image.naturalWidth > 0; }"));
        Assert.True(await multiSnapshot.EvaluateAsync<bool>("async image => { await image.decode(); return image.naturalWidth > 0; }"));
        Assert.NotEqual(await singleSnapshot.GetAttributeAsync("src"), await multiSnapshot.GetAttributeAsync("src"));
        Assert.DoesNotContain("data-review-thumbnail", await singleStrip.InnerHTMLAsync(), StringComparison.Ordinal);
        var singleStripPrice = (await singleStrip.Locator("[data-workflow-summary-unit-price]").InnerTextAsync()).Trim();
        var multiStripPrice = (await multiStrip.Locator("[data-workflow-summary-unit-price]").InnerTextAsync()).Trim();
        Assert.NotEqual("—", singleStripPrice);
        Assert.NotEqual("—", multiStripPrice);
        Assert.Equal(await configuredUnitPrice.InnerTextAsync(), multiStripPrice);
        var reviewButton = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        await reviewButton.ClickAsync(new LocatorClickOptions { Timeout = 60_000 });
        var singleReview = page.Locator($"[data-review-part-id='{singleId}'][data-workflow-review-part]");
        var multiReview = page.Locator($"[data-review-part-id='{multiId}'][data-workflow-review-part]");
        await singleReview.WaitForAsync();
        Assert.Equal(singleStripPrice, (await singleReview.Locator(".instant-quote__review-prices > div:first-child dd").InnerTextAsync()).Trim());
        Assert.Equal(multiStripPrice, (await multiReview.Locator(".instant-quote__review-prices > div:first-child dd").InnerTextAsync()).Trim());
        Assert.Equal("#123abc", (await singleReview.Locator($"#review-color-{singleId}").InputValueAsync()).ToLowerInvariant());
        Assert.True(await multiReview.Locator($"#review-color-{multiId}").IsDisabledAsync());
        Assert.Equal("Any", await multiReview.Locator($"#review-color-{multiId}").InputValueAsync());
        Assert.Equal(0, await multiReview.Locator("input[type='color']").CountAsync());
        await singleReview.Locator("[data-review-select-part]").ClickAsync();
        await page.WaitForFunctionAsync(
            "id => document.querySelector('[data-workflow-review-part][data-review-part-id=\"' + id + '\"]')?.dataset.selected === 'true'",
            singleId.ToString(), new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('[data-review-thumbnail]')].length === 2 && [...document.querySelectorAll('[data-review-thumbnail]')].every(image => image.complete && image.naturalWidth > 0 && image.src.startsWith('data:image/webp'))",
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
        foreach (var width in new[] { 390, 768, 1280 })
        {
            await page.SetViewportSizeAsync(width, 900);
            Assert.True(await page.Locator("[data-workflow-review-part]").EvaluateAllAsync<bool>(
                """
                cards => cards.length === 2 && cards.every(card => {
                  const prices = card.querySelector('.instant-quote__review-prices');
                  return prices.getBoundingClientRect().width >= card.getBoundingClientRect().width * 0.8
                    && [...prices.querySelectorAll('dt, dd')].every(value => value.scrollWidth <= value.clientWidth);
                })
                """), "Both review prices must use the card row and display their complete labels and amounts.");
        }
        var beforeSingle = await singleReview.Locator("[data-review-thumbnail]").GetAttributeAsync("src");
        var beforeMulti = await multiReview.Locator("[data-review-thumbnail]").GetAttributeAsync("src");
        // Two native changes may overlap server repricing; the visible result must use the final accepted configuration.
        await singleReview.Locator("input[type='color']").EvaluateAsync(
            """
            input => {
              for (const color of ['#1122ee', '#ee1122']) {
                input.value = color;
                input.dispatchEvent(new Event('change', { bubbles: true }));
              }
            }
            """);
        await page.WaitForFunctionAsync(
            """
            args => {
              const single = document.querySelector('[data-workflow-review-part][data-review-part-id="' + args.id + '"]');
              return single?.querySelector('select[id^="review-color-"]')?.value?.toLowerCase() === '#ee1122'
                && single.querySelector('[data-review-thumbnail]')?.src !== args.before
                && !document.querySelector('[data-pricing-loading-status]');
            }
            """,
            new { id = singleId.ToString(), before = beforeSingle },
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        await page.WaitForFunctionAsync(
            """
            async args => {
              const article = document.querySelector('[data-workflow-review-part][data-review-part-id="' + args.id + '"]');
              const thumbnail = article?.querySelector('[data-review-thumbnail]');
              if (!thumbnail?.src.startsWith('data:image/webp') || document.querySelector('[data-pricing-loading-status]')) return false;
              const image = new Image();
              image.src = thumbnail.src;
              await image.decode();
              const canvas = document.createElement('canvas');
              canvas.width = image.naturalWidth;
              canvas.height = image.naturalHeight;
              const context = canvas.getContext('2d');
              context.drawImage(image, 0, 0);
              const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
              let red = 0;
              let blue = 0;
              for (let index = 0; index < pixels.length; index += 4) {
                if (pixels[index] > pixels[index + 2] * 1.5 && pixels[index] > pixels[index + 1] * 1.5) red++;
                if (pixels[index + 2] > pixels[index] * 1.5 && pixels[index + 2] > pixels[index + 1] * 1.5) blue++;
              }
              const multipart = document.querySelector('[data-workflow-review-part][data-review-part-id="' + args.multiId + '"] [data-review-thumbnail]');
              if (multipart?.getAttribute('src') !== args.beforeMulti) return false;
              await multipart.decode();
              return red > 20 && red > blue && thumbnail.isConnected && thumbnail.src === image.src
                && multipart.isConnected && multipart.naturalWidth > 0 && multipart.getAttribute('src') === args.beforeMulti;
            }
            """,
            new { id = singleId.ToString(), multiId = multiId.ToString(), beforeMulti },
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        Assert.Equal(beforeMulti, await multiReview.Locator("[data-review-thumbnail]").GetAttributeAsync("src"));
        Assert.Equal("true", await singleReview.GetAttributeAsync("data-selected"));
        saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(saved);
        Assert.Equal("#ee1122", Assert.Single(saved.Parts, part => part.PartId == singleId).Configuration.Color.ToLowerInvariant());
        Assert.Equal("Any", Assert.Single(saved.Parts, part => part.PartId == multiId).Configuration.Color);
        Assert.Equal(2, upload.VerifiedUploads);
        Assert.Equal(0, upload.ValidationRejectedUploads);

        // Browser model bytes live only until reload. Verify real WebP/pixel behavior above,
        // then verify protected configuration restoration without inventing durable mesh fetch.
        var screenshotDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "source-integration-browser");
        Directory.CreateDirectory(screenshotDirectory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDirectory, $"preview-colors-{culture}.png"), FullPage = true });
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-workflow-part]').length === 2 && !document.querySelector('[data-pricing-loading-status]')",
            null, new PageWaitForFunctionOptions { Timeout = 60_000 });
        await singleStrip.Locator("button[aria-pressed]").ClickAsync();
        await page.WaitForFunctionAsync(
            "id => document.querySelector('#color-' + id)?.value?.toLowerCase() === '#ee1122' && !document.querySelector('[data-pricing-loading-status]')",
            singleId.ToString(), new PageWaitForFunctionOptions { Timeout = 60_000 });
        Assert.Equal("#ee1122", (await singleColor.InputValueAsync()).ToLowerInvariant());
        Assert.Equal("2", await page.Locator($"#quantity-{singleId}").InputValueAsync());
        Assert.Equal(1, await custom.CountAsync());
        Assert.Equal("PLA 2×", (await singleStrip.Locator("[data-workflow-summary-configuration]").InnerTextAsync()).Trim());
        await page.WaitForFunctionAsync(
            """
            ([id, label]) => {
              const configured = [...document.querySelectorAll('[data-workflow-part-price] > div')]
                .find(row => row.querySelector('dt')?.textContent?.trim() === label)
                ?.querySelector('dd')?.textContent?.trim();
              const summary = document.querySelector('[data-workflow-part][data-part-id="' + id + '"] [data-workflow-summary-unit-price]')
                ?.textContent?.trim();
              return configured && summary && configured !== '—' && summary !== '—' && configured === summary;
            }
            """,
            new[] { singleId.ToString(), unitPriceLabel }, new PageWaitForFunctionOptions { Timeout = 30_000 });
        Assert.Equal(await configuredUnitPrice.InnerTextAsync(),
            await singleStrip.Locator("[data-workflow-summary-unit-price]").InnerTextAsync());
        await multiStrip.Locator("button[aria-pressed]").ClickAsync();
        await page.WaitForFunctionAsync(
            "id => document.querySelector('#color-' + id)?.disabled && document.querySelector('#color-' + id)?.value === 'Any'",
            multiId.ToString(), new PageWaitForFunctionOptions { Timeout = 30_000 });
        Assert.True(await multiColor.IsDisabledAsync());
        Assert.Equal("Any", await multiColor.InputValueAsync());
        Assert.Equal("1", await page.Locator($"#quantity-{multiId}").InputValueAsync());
        Assert.Equal(0, await custom.CountAsync());
        Assert.Equal("M68 1×", (await multiStrip.Locator("[data-workflow-summary-configuration]").InnerTextAsync()).Trim());
        Assert.Equal(await configuredUnitPrice.InnerTextAsync(),
            await multiStrip.Locator("[data-workflow-summary-unit-price]").InnerTextAsync());
        saved = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(saved);
        Assert.Equal("#ee1122", Assert.Single(saved.Parts, part => part.PartId == singleId).Configuration.Color.ToLowerInvariant());
        Assert.Equal("Any", Assert.Single(saved.Parts, part => part.PartId == multiId).Configuration.Color);
        Assert.Equal(2, upload.VerifiedUploads);
        Assert.Equal(0, upload.ValidationRejectedUploads);
        Assert.Empty(pageErrors);
    }


    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task FailedUploadDemoIsDevelopmentOnlyAdvisoryAndCannotSubmit(string culture)
    {
        var startup = new StartupDataProtectionMigrationFixture();
        try
        {
            await startup.InitializeAsync();
            foreach (var environment in new[] { "Development", "Production" })
            {
                var upload = new HashCheckingUploadClient();
                upload.FailNextUpload();
                var pricing = new CountingPricingService();
                var observation = new MaterialCompletionObservation();
                await using var factory = startup.CreateFactory(environment, builder =>
                {
                    builder.UseContentRoot(BrowserHostIdentityVerifier.SourceProjectDirectory());
                    // Production source hosts also need the SDK manifest for Blazor framework assets.
                    builder.UseStaticWebAssets();
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IInstantQuotationUploadClient>();
                        services.AddSingleton<IInstantQuotationUploadClient>(upload);
                        services.RemoveAll<IInstantQuotationPricingService>();
                        services.AddSingleton<IInstantQuotationPricingService>(pricing);
                        services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                        services.AddScoped<IInstantQuotationPhysicalAnalysisInputReader>(provider =>
                            new BrowserMultiUploadInputReader(upload,
                                provider.GetRequiredService<IInstantQuotationSessionStore>()));
                        observation.Decorate(services);
                    });
                });
                var port = ReserveFreePort();
                var origin = new Uri($"http://127.0.0.1:{port}");
                factory.UseKestrel(port);
                using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = origin,
                    AllowAutoRedirect = false,
                });
                using var readiness = await client.GetAsync($"/instantquotation/3d-printing?culture={culture}");
                Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
                var hostEnvironment = factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
                Assert.Equal(environment, hostEnvironment.EnvironmentName);
                Assert.Equal(typeof(Program).Assembly.GetName().Name, hostEnvironment.ApplicationName);
                Assert.Equal(Path.GetFullPath(BrowserHostIdentityVerifier.SourceProjectDirectory()),
                    Path.GetFullPath(hostEnvironment.ContentRootPath));
                Assert.Contains("/_framework/blazor.web.js", await readiness.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                // The production startup fixture intentionally leaves CNC unavailable; prove this host's assets directly.
                using var frameworkResponse = await client.GetAsync("/_framework/blazor.web.js");
                Assert.Equal(HttpStatusCode.OK, frameworkResponse.StatusCode);
                Assert.Contains("javascript", frameworkResponse.Content.Headers.ContentType?.MediaType ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
                Assert.NotEmpty(await frameworkResponse.Content.ReadAsByteArrayAsync());
                const string workflowAsset = "dist/instant-quotation-workflow.mjs";
                var expectedWorkflow = await File.ReadAllBytesAsync(Path.Combine(
                    BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", workflowAsset));
                using var workflowResponse = await client.GetAsync("/" + workflowAsset);
                Assert.Equal(HttpStatusCode.OK, workflowResponse.StatusCode);
                Assert.NotEmpty(expectedWorkflow);
                Assert.Equal(expectedWorkflow, await workflowResponse.Content.ReadAsByteArrayAsync());
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
                await using var page = await browser.NewPageAsync();
                var submissionRequests = 0;
                page.Request += (_, request) =>
                {
                    if (request.Method == "POST" && request.Url.Contains("handler=SubmitRequest", StringComparison.Ordinal))
                        Interlocked.Increment(ref submissionRequests);
                };
                await page.GotoAsync(new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString(),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
                var path = CreateBoxStl(20, 20, 5, "development-demo");
                try
                {
                    await WaitForUploadInputReadyAsync(page);
                    await page.SetInputFilesAsync("#instant-quote-files", path);
                    await Microsoft.Playwright.Assertions.Expect(page.Locator(".instant-quote__workflow"))
                        .ToHaveAttributeAsync("data-workflow-state", "error",
                            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
                    Assert.Equal(1, upload.StartedUploads);
                    Assert.Equal(0, upload.VerifiedUploads);
                    Assert.Equal(0, pricing.CallCount);
                    Assert.StartsWith("pricingEntered=0; pricingRunning=0;", observation.Describe(), StringComparison.Ordinal);
                    var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
                    var failedState = await store.GetAsync(upload.AttemptedSessionId!, upload.AttemptedOwnerIdentity, default);
                    Assert.NotNull(failedState);
                    Assert.Empty(failedState.Parts);
                    Assert.Null(failedState.QuoteAuthorization);
                    Assert.Empty(failedState.PhysicalReceipts ?? []);
                    var demo = page.Locator("[data-workflow-local-demo]");
                    if (environment == "Production")
                    {
                        Assert.Equal(0, await demo.CountAsync());
                        Assert.Equal(0, await page.Locator("[data-local-demo-badge]").CountAsync());
                        Assert.Equal(0, await page.Locator("#instant-quotation-form").CountAsync());
                        continue;
                    }

                    await demo.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
                    Assert.Equal(culture == "th" ? "สาธิต" : "Demo",
                        (await demo.Locator("[data-local-demo-badge]").InnerTextAsync()).Trim());
                    Assert.Contains(culture == "th" ? "ไม่สามารถส่ง" : "cannot be submitted",
                        await demo.InnerTextAsync(), StringComparison.Ordinal);
                    Assert.Equal("configuration", await demo.GetAttributeAsync("data-local-demo-step"));
                    Assert.Equal(0, await page.Locator("[data-workflow-part]").CountAsync());
                    await using var canvas = await demo.Locator("[data-local-demo-canvas]").ElementHandleAsync();
                    Assert.NotNull(canvas);
                    await Microsoft.Playwright.Assertions.Expect(demo.Locator("[data-local-demo-canvas]"))
                        .ToHaveAttributeAsync("data-local-demo-ready", "true",
                            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
                    var oldSubtotal = await demo.Locator("[data-local-demo-subtotal]").InnerTextAsync();
                    Assert.NotEqual("—", oldSubtotal.Trim());
                    await demo.Locator("#demo-material").SelectOptionAsync("PETG");
                    await demo.Locator("#demo-quantity").FillAsync("2");
                    await demo.Locator("#demo-quantity").DispatchEventAsync("change");
                    await Microsoft.Playwright.Assertions.Expect(demo.Locator("[data-local-demo-configuration]"))
                        .ToHaveTextAsync("PETG 2×", new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
                    Assert.NotEqual(oldSubtotal, await demo.Locator("[data-local-demo-subtotal]").InnerTextAsync());
                    Assert.NotEqual("—", (await demo.Locator("[data-local-demo-lead-time]").InnerTextAsync()).Trim());
                    await demo.Locator("[data-local-demo-review]").ClickAsync();
                    await Microsoft.Playwright.Assertions.Expect(demo)
                        .ToHaveAttributeAsync("data-local-demo-step", "review",
                            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
                    Assert.Contains(Path.GetFileName(path), await demo.InnerTextAsync(), StringComparison.Ordinal);
                    await demo.Locator("[data-local-demo-continue]").ClickAsync();
                    await Microsoft.Playwright.Assertions.Expect(demo)
                        .ToHaveAttributeAsync("data-local-demo-step", "contact",
                            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
                    await demo.Locator("#demo-contact-name").FillAsync("Synthetic demo");
                    await demo.Locator("#demo-contact-email").FillAsync("demo@example.invalid");
                    var submit = demo.Locator("[data-local-demo-no-submit]");
                    Assert.Equal("button", await submit.GetAttributeAsync("type"));
                    Assert.True(await submit.IsDisabledAsync());
                    Assert.Equal(0, await demo.Locator("form, [form], input[name='__RequestVerificationToken'], input[name*='Ticket']").CountAsync());
                    await demo.Locator("#demo-contact-email").PressAsync("Enter");
                    Assert.Equal(0, Volatile.Read(ref submissionRequests));
                    Assert.Equal(0, upload.FinalizeCalls);
                    Assert.Equal(0, pricing.CallCount);
                    Assert.StartsWith("pricingEntered=0; pricingRunning=0;", observation.Describe(), StringComparison.Ordinal);
                    failedState = await store.GetAsync(upload.AttemptedSessionId!, upload.AttemptedOwnerIdentity, default);
                    Assert.NotNull(failedState);
                    Assert.Empty(failedState.Parts);
                    Assert.Null(failedState.QuoteAuthorization);
                    Assert.Empty(failedState.PhysicalReceipts ?? []);

                    // The ordinary failed-row action and Demo action must share fresh-key rebinding.
                    upload.FailNextUpload();
                    var retryName = culture == "th"
                        ? $"ลองอัปโหลด {Path.GetFileName(path)} อีกครั้ง"
                        : $"Try upload again for {Path.GetFileName(path)}";
                    await page.Locator("[data-workflow-upload-item]").GetByRole(
                        AriaRole.Button, new LocatorGetByRoleOptions { Name = retryName, Exact = true }).ClickAsync();
                    await canvas.WaitForElementStateAsync(ElementState.Hidden, new() { Timeout = 30_000 });
                    await Microsoft.Playwright.Assertions.Expect(demo.Locator("[data-local-demo-canvas]"))
                        .ToHaveAttributeAsync("data-local-demo-ready", "true",
                            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
                    Assert.False(await canvas.EvaluateAsync<bool>("element => element.isConnected"));
                    Assert.Equal(2, upload.StartedUploads);
                    Assert.Equal(0, upload.VerifiedUploads);
                    Assert.StartsWith("pricingEntered=0; pricingRunning=0;", observation.Describe(), StringComparison.Ordinal);
                    await Microsoft.Playwright.Assertions.Expect(demo.Locator("[data-local-demo-configuration]"))
                        .ToHaveTextAsync("PETG 2×", new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
                    await using var retriedCanvas = await demo.Locator("[data-local-demo-canvas]").ElementHandleAsync();
                    Assert.NotNull(retriedCanvas);
                    await demo.Locator("[data-local-demo-retry]").ClickAsync();
                    await page.Locator("[data-workflow-part]").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
                    Assert.Equal(0, await demo.CountAsync());
                    Assert.False(await retriedCanvas.EvaluateAsync<bool>("element => element.isConnected"));
                    Assert.Equal(3, upload.StartedUploads);
                    Assert.Equal(1, upload.VerifiedUploads);
                    Assert.Equal(1, await page.Locator("[data-workflow-part]").CountAsync());
                    var admitted = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                    Assert.NotNull(admitted);
                    Assert.Single(admitted.Parts);
                    Assert.NotNull(Assert.Single(admitted.Parts).PhysicalAnalysisUpload);
                    await page.Locator("[data-workflow-price-tier]").First.WaitForAsync(
                        new LocatorWaitForOptions { Timeout = 60_000 });
                    Assert.DoesNotContain("pricingEntered=0;", observation.Describe(), StringComparison.Ordinal);
                    admitted = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                    Assert.NotNull(admitted);
                    Assert.NotNull(admitted.QuoteAuthorization);
                    Assert.Equal(0, Volatile.Read(ref submissionRequests));
                    Assert.Equal(0, upload.FinalizeCalls);
                }
                finally
                {
                    File.Delete(path);
                }
            }
        }
        finally
        {
            await startup.DisposeAsync();
        }
    }

    [Fact]
    public async Task DevelopmentDemoNeverRetainsCancelledOrMalformedUploads()
    {
        var startup = new StartupDataProtectionMigrationFixture();
        try
        {
            await startup.InitializeAsync();
            var upload = new HashCheckingUploadClient { HoldUploads = true };
            var pricing = new CountingPricingService();
            var observation = new MaterialCompletionObservation();
            await using var factory = startup.CreateFactory("Development", builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationUploadClient>();
                services.AddSingleton<IInstantQuotationUploadClient>(upload);
                services.RemoveAll<IInstantQuotationPricingService>();
                services.AddSingleton<IInstantQuotationPricingService>(pricing);
                observation.Decorate(services);
            }));
            var port = ReserveFreePort();
            var origin = new Uri($"http://127.0.0.1:{port}");
            factory.UseKestrel(port);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin, AllowAutoRedirect = false });
            using var readiness = await client.GetAsync("/instantquotation/3d-printing?culture=en");
            Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            await using var page = await browser.NewPageAsync();
            await page.GotoAsync(new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            var path = CreateBoxStl(20, 20, 5, "cancelled-demo");
            try
            {
                await WaitForUploadInputReadyAsync(page);
                await page.SetInputFilesAsync("#instant-quote-files", path);
                var cancel = page.Locator("[data-workflow-upload-item]").GetByRole(
                    AriaRole.Button, new LocatorGetByRoleOptions
                    {
                        Name = $"Cancel upload for {Path.GetFileName(path)}",
                        Exact = true,
                    });
                await cancel.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
                await upload.UploadEntered.WaitAsync(TimeSpan.FromSeconds(30));
                await cancel.ClickAsync();
                await Microsoft.Playwright.Assertions.Expect(page.Locator(".instant-quote__workflow"))
                    .ToHaveAttributeAsync("aria-busy", "false",
                        new LocatorAssertionsToHaveAttributeOptions { Timeout = 30_000 });
                await Microsoft.Playwright.Assertions.Expect(page.Locator("#instant-quote-files"))
                    .ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 30_000 });
                Assert.Equal(1, upload.CancellationObserved);
                Assert.Equal(0, await page.Locator("[data-workflow-local-demo]").CountAsync());
                Assert.Equal(0, upload.VerifiedUploads);
                Assert.Equal(0, pricing.CallCount);
                Assert.StartsWith("pricingEntered=0; pricingRunning=0;", observation.Describe(), StringComparison.Ordinal);
                await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
                {
                    Name = "malformed.stl",
                    MimeType = "model/stl",
                    Buffer = [1, 2, 3],
                });
                await Microsoft.Playwright.Assertions.Expect(page.Locator("[data-workflow-preview-status]"))
                    .ToContainTextAsync("unavailable", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
                Assert.Equal(0, await page.Locator("[data-workflow-local-demo]").CountAsync());
                Assert.Equal(0, upload.VerifiedUploads);
                Assert.Equal(0, pricing.CallCount);
                Assert.StartsWith("pricingEntered=0; pricingRunning=0;", observation.Describe(), StringComparison.Ordinal);
                Assert.Equal(0, upload.FinalizeCalls);
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            await startup.DisposeAsync();
        }
    }

    private static async Task WaitForUploadInputReadyAsync(IPage page)
    {
        // InputFile wiring and preview capture are separate from static HTML readiness.
        await using var readiness = await page.WaitForFunctionAsync(
            """
            () => {
              const zone = document.querySelector('.instant-quote__workflow');
              const input = document.querySelector('#instant-quote-files');
              if (!zone || !input || input._blazorInputFileNextFileId === undefined
                  || input.disabled || zone.getAttribute('aria-busy') === 'true') return false;
              const transfer = new DataTransfer();
              transfer.items.add(new File([], 'picker-readiness.stl', { type: 'model/stl' }));
              const probe = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: transfer });
              return !zone.dispatchEvent(probe) && probe.defaultPrevented;
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private sealed class RealUploadTestingWebApplicationFactory(
        string contentRoot,
        HashCheckingUploadClient upload,
        CountingPricingService pricing,
        MaterialCompletionObservation? observation = null,
        SupersededPetgPricing? obsolete = null,
        BatchEstimateGate? batchEstimates = null) : TestingWebApplicationFactory(contentRoot)
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> ExceptionKinds { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(new ExceptionKindProvider(ExceptionKinds));
                services.RemoveAll<IInstantQuotationUploadClient>();
                services.AddSingleton<IInstantQuotationUploadClient>(upload);
                services.RemoveAll<IInstantQuotationPricingService>();
                services.AddSingleton<IInstantQuotationPricingService>(pricing);
                services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                services.AddScoped<IInstantQuotationPhysicalAnalysisInputReader>(provider =>
                    new BrowserMultiUploadInputReader(upload,
                        provider.GetRequiredService<IInstantQuotationSessionStore>()));
                observation?.Decorate(services);
                obsolete?.Decorate(services);
                batchEstimates?.Decorate(services);
            });
        }
    }

    private sealed class ExceptionKindProvider(System.Collections.Concurrent.ConcurrentQueue<string> kinds)
        : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new KindLogger(kinds);
        public void Dispose() { }
        private sealed class KindLogger(System.Collections.Concurrent.ConcurrentQueue<string> kinds)
            : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => level >= Microsoft.Extensions.Logging.LogLevel.Error;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId eventId,
                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (exception is not null && kinds.Count < 20)
                {
                    var methods = System.Text.RegularExpressions.Regex.Matches(exception.Message,
                        @"\bat\s+([A-Za-z_$][A-Za-z0-9_.$]*)").Select(match => match.Groups[1].Value).Take(6);
                    var callers = System.Text.RegularExpressions.Regex.Matches(exception.StackTrace ?? string.Empty,
                        @"at (Legacy\.Maliev\.[A-Za-z0-9_.+<>`]+)\(").Select(match => match.Groups[1].Value).Take(6);
                    kinds.Enqueue($"{exception.GetType().Name}:{exception.TargetSite?.DeclaringType?.Name}.{exception.TargetSite?.Name};JS={string.Join(',', methods)};Callers={string.Join(',', callers)}");
                }
            }
        }
    }

    private static string CreateBoxStl(float width, float depth, float height, string fileStem = "maliev-thin-fdm")
    {
        float[][] points =
        [
            [0, 0, 0], [width, 0, 0], [width, depth, 0], [0, depth, 0],
            [0, 0, height], [width, 0, height], [width, depth, height], [0, depth, height],
        ];
        int[][] faces =
        [
            [0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7],
            [0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5],
            [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7],
        ];
        var path = Path.Combine(Path.GetTempPath(), $"{fileStem}-{Guid.NewGuid():N}.stl");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(new byte[80]);
        writer.Write((uint)faces.Length);
        foreach (var face in faces)
        {
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            foreach (var value in points[face[0]]) writer.Write(value);
            foreach (var value in points[face[1]]) writer.Write(value);
            foreach (var value in points[face[2]]) writer.Write(value);
            writer.Write((ushort)0);
        }

        return path;
    }

    private static string CreateOpenSheetStl()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maliev-open-sheet-{Guid.NewGuid():N}.stl");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(new byte[80]);
        writer.Write((uint)2);
        var points = new float[][]
        {
            [0, 0, 0], [20, 0, 0], [20, 20, 0], [0, 20, 0],
        };
        foreach (var face in new[] { new[] { 0, 2, 1 }, new[] { 0, 3, 2 } })
        {
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            foreach (var value in points[face[0]]) writer.Write(value);
            foreach (var value in points[face[1]]) writer.Write(value);
            foreach (var value in points[face[2]]) writer.Write(value);
            writer.Write((ushort)0);
        }

        return path;
    }

    private sealed class CountingPricingService : IInstantQuotationPricingService
    {
        private readonly InstantQuotationPricingService inner = new();
        private int calls;

        public int CallCount => Volatile.Read(ref calls);

        public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state)
        {
            Interlocked.Increment(ref calls);
            return inner.Quote(state);
        }
    }

    private sealed class HashCheckingUploadClient : IInstantQuotationUploadClient
    {
        private int startedUploads;
        private int failuresRemaining;
        private int finalizeCalls;
        private int cancellationObserved;
        private readonly TaskCompletionSource<bool> uploadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int validationRejectedUploads;
        private int verifiedUploads;
        private BrowserUpload? verifiedUpload;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, BrowserUpload> uploads = new();

        private readonly object uploadGate = new();
        private readonly List<Task<InstantQuotationUploadResult>> actualUploads = [];
        private CancellationTokenSource? uploadCancellation;
        private bool uploadAdmissionStopped;
        private Task<InstantQuotationUploadResult>[] closedUploads = [];

        public bool OwnUploadLifetime { get; init; }
        public Task<InstantQuotationUploadResult>[] ClosedUploads
        {
            get
            {
                lock (uploadGate)
                {
                    if (!uploadAdmissionStopped) throw new InvalidOperationException("Upload admission must stop before draining.");
                    return closedUploads;
                }
            }
        }

        public void StopUploadAdmission()
        {
            CancellationTokenSource? cancellation;
            lock (uploadGate)
            {
                if (!uploadAdmissionStopped)
                {
                    uploadAdmissionStopped = true;
                    closedUploads = actualUploads.ToArray();
                }
                cancellation = uploadCancellation;
            }
            cancellation?.Cancel();
        }

        public async Task DrainUploadsAsync()
        {
            var tasks = ClosedUploads;
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (tasks.All(task => task.IsCompleted)) { }
        }

        public void DisposeUploadLifetime()
        {
            lock (uploadGate)
            {
                if (!uploadAdmissionStopped || closedUploads.Any(task => !task.IsCompleted))
                    throw new InvalidOperationException("Actual uploads must settle before their lifetime is disposed.");
                uploadCancellation?.Dispose();
                uploadCancellation = null;
            }
        }

        private readonly object physicalReadGate = new();
        private CancellationTokenSource? physicalReadCancellation;
        private bool physicalReadAdmissionStopped;
        private Task[] closedPhysicalReads = [];

        public bool AllowVerifiedRemoval { get; init; }
        public bool OwnPhysicalReadLifetime { get; init; }
        public Func<Guid, CancellationToken, Task>? BeforePhysicalRead { get; set; }
        public System.Collections.Concurrent.ConcurrentBag<Task> PendingPhysicalReads { get; } = [];

        public Task[] ClosedPhysicalReads
        {
            get
            {
                lock (physicalReadGate)
                {
                    if (!physicalReadAdmissionStopped) throw new InvalidOperationException("Read admission must be stopped before draining.");
                    return closedPhysicalReads;
                }
            }
        }

        public Task<InstantQuotationPhysicalAnalysisInputResult> StartPhysicalRead(
            Func<CancellationToken, Task<InstantQuotationPhysicalAnalysisInputResult>> read,
            CancellationToken cancellationToken)
        {
            CancellationTokenSource? linked = null;
            TaskCompletionSource<InstantQuotationPhysicalAnalysisInputResult> admitted;
            lock (physicalReadGate)
            {
                if (physicalReadAdmissionStopped)
                    return Task.FromResult(InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                        InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable));
                if (OwnPhysicalReadLifetime)
                {
                    physicalReadCancellation ??= new CancellationTokenSource();
                    linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, physicalReadCancellation.Token);
                }

                admitted = new TaskCompletionSource<InstantQuotationPhysicalAnalysisInputResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                PendingPhysicalReads.Add(admitted.Task);
            }
            // Publish ownership before invoking even a synchronous producer prefix.
            _ = CompleteReadAsync(read, linked, cancellationToken, admitted);
            return admitted.Task;
        }

        private static async Task CompleteReadAsync(
            Func<CancellationToken, Task<InstantQuotationPhysicalAnalysisInputResult>> read,
            CancellationTokenSource? linked, CancellationToken cancellationToken,
            TaskCompletionSource<InstantQuotationPhysicalAnalysisInputResult> admitted)
        {
            InstantQuotationPhysicalAnalysisInputResult? result = null;
            Exception? failure = null;
            try
            {
                var effectiveToken = linked?.Token ?? cancellationToken;
                effectiveToken.ThrowIfCancellationRequested();
                result = await read(effectiveToken);
            }
            catch (Exception error) { failure = error; }
            finally { linked?.Dispose(); }
            if (failure is OperationCanceledException canceled) admitted.TrySetCanceled(canceled.CancellationToken);
            else if (failure is not null) admitted.TrySetException(failure);
            else admitted.TrySetResult(result!);
        }

        public void StopPhysicalReadAdmission()
        {
            CancellationTokenSource? cancellation;
            lock (physicalReadGate)
            {
                if (!physicalReadAdmissionStopped)
                {
                    physicalReadAdmissionStopped = true;
                    closedPhysicalReads = PendingPhysicalReads.ToArray();
                }
                cancellation = physicalReadCancellation;
            }
            // Cancellation can invoke continuations; never run them while holding the admission lock.
            cancellation?.Cancel();
        }

        public void DisposePhysicalReadLifetime()
        {
            lock (physicalReadGate)
            {
                if (!physicalReadAdmissionStopped || closedPhysicalReads.Any(task => !task.IsCompleted))
                    throw new InvalidOperationException("Actual physical reads must settle before their lifetime is disposed.");
                physicalReadCancellation?.Dispose();
                physicalReadCancellation = null;
            }
        }
        public bool HoldUploads { get; init; }
        public Task UploadEntered => uploadEntered.Task;
        public int CancellationObserved => Volatile.Read(ref cancellationObserved);
        public string? AttemptedSessionId { get; private set; }
        public string? AttemptedOwnerIdentity { get; private set; }
        public int FinalizeCalls => Volatile.Read(ref finalizeCalls);
        public void FailNextUpload() => Interlocked.Exchange(ref failuresRemaining, 1);

        public int VerifiedUploads => Volatile.Read(ref verifiedUploads);
        public int StartedUploads => Volatile.Read(ref startedUploads);
        public int ValidationRejectedUploads => Volatile.Read(ref validationRejectedUploads);

        public string SessionId => verifiedUpload!.SessionId;

        public string? OwnerIdentity => verifiedUpload!.OwnerIdentity;

        public async Task AssertAdmittedMeshMatchesBrowserUploadAsync(Guid partId)
        {
            BrowserUpload upload = Assert.IsType<BrowserUpload>(verifiedUpload);
            var reader = new BrowserUploadInputReader(upload, partId);
            var service = new InstantQuotationAdmittedMeshService(reader);
            var mesh = await service.ReadAsync(
                upload.SessionId, upload.OwnerIdentity, partId, upload.FileId, upload.Sha256, default);
            Assert.True(mesh.IsReady);
            Assert.Equal(upload.Sha256, mesh.UploadSha256);
            Assert.Equal(12, mesh.Mesh!.Triangles.Count);
            Assert.Equal(1, reader.ReadCount);
        }

        public string? RejectFileName { get; set; }
        public Func<Task>? BeforeUpload { get; set; }
        public Task? LastUploadTask { get; private set; }

        public Task<InstantQuotationUploadResult> UploadAsync(
            string sessionId, string? ownerIdentity, Stream content, string fileName, string contentType,
            long contentLength, InstantQuotationGeometryClaim geometryClaim, string operationId,
            CancellationToken cancellationToken)
        {
            CancellationTokenSource? linked = null;
            TaskCompletionSource<InstantQuotationUploadResult> admitted;
            lock (uploadGate)
            {
                if (uploadAdmissionStopped)
                    return Task.FromResult(InstantQuotationUploadResult.Unavailable(operationId));
                if (OwnUploadLifetime)
                {
                    uploadCancellation ??= new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, uploadCancellation.Token);
                }
                admitted = new TaskCompletionSource<InstantQuotationUploadResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                actualUploads.Add(admitted.Task);
                LastUploadTask = admitted.Task;
                _ = admitted.Task.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            // Register ownership before invoking even the synchronous upload producer prefix.
            _ = CompleteUploadAsync();
            return admitted.Task;

            async Task CompleteUploadAsync()
            {
                InstantQuotationUploadResult? result = null;
                Exception? failure = null;
                try
                {
                    var token = linked?.Token ?? cancellationToken;
                    token.ThrowIfCancellationRequested();
                    result = await UploadCoreAsync(sessionId, ownerIdentity, content, fileName, contentType,
                        contentLength, geometryClaim, operationId, token);
                }
                catch (Exception error) { failure = error; }
                finally { linked?.Dispose(); }
                if (failure is OperationCanceledException canceled) admitted.TrySetCanceled(canceled.CancellationToken);
                else if (failure is not null) admitted.TrySetException(failure);
                else admitted.TrySetResult(result!);
            }
        }

        private async Task<InstantQuotationUploadResult> UploadCoreAsync(
            string sessionId, string? ownerIdentity, Stream content, string fileName, string contentType,
            long contentLength, InstantQuotationGeometryClaim geometryClaim, string operationId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref startedUploads);
            if (BeforeUpload is { } before) await before();
            if (fileName == RejectFileName)
            {
                Interlocked.Increment(ref validationRejectedUploads);
                return InstantQuotationUploadResult.Failed(operationId, InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized, InstantQuotationProblemCategory.Validation);
            }
            using var cancellationObservation = cancellationToken.Register(() => Interlocked.Increment(ref cancellationObserved));
            uploadEntered.TrySetResult(true);
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var sha256 = Convert.ToHexString(SHA256.HashData(copy.GetBuffer().AsSpan(0, (int)copy.Length)))
                .ToLowerInvariant();
            if (copy.Length != contentLength || !string.Equals(sha256, geometryClaim.Sha256, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref validationRejectedUploads);
                return InstantQuotationUploadResult.Failed(
                    operationId,
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationProblemCategory.Validation);
            }

            AttemptedSessionId = sessionId;
            AttemptedOwnerIdentity = ownerIdentity;
            if (HoldUploads)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (Interlocked.Exchange(ref failuresRemaining, 0) > 0)
                return InstantQuotationUploadResult.Unavailable(operationId);

            var fileId = Guid.NewGuid();
            verifiedUpload = new BrowserUpload(sessionId, ownerIdentity, fileId, fileName,
                copy.ToArray(), sha256)
            {
                GltfUnitPolicy = geometryClaim.GltfUnitPolicy,
            };
            uploads[fileId] = verifiedUpload;
            Interlocked.Increment(ref verifiedUploads);
            return InstantQuotationUploadResult.Succeeded(
                operationId,
                new InstantQuotationUploadReference(fileId.ToString("D")),
                sha256,
                new InstantQuotationPhysicalAnalysisUpload(
                    fileId, fileName,
                    string.IsNullOrWhiteSpace(contentType) ? "model/stl" : contentType,
                    copy.Length, sha256, "clean")
                {
                    GltfUnitPolicy = geometryClaim.GltfUnitPolicy,
                });
        }

        public InstantQuotationPhysicalAnalysisInputResult Read(
            string sessionId, string? ownerIdentity, Guid fileId)
        {
            uploads.TryGetValue(fileId, out BrowserUpload? upload);
            return upload is not null
                && upload.SessionId == sessionId && upload.OwnerIdentity == ownerIdentity
                ? new InstantQuotationPhysicalAnalysisInputResult(
                    upload.Bytes, InstantQuotationPhysicalAnalysisInputFailure.None,
                    upload.FileId, upload.FileName, upload.Sha256)
                {
                    GltfUnitPolicy = upload.GltfUnitPolicy,
                }
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable);
        }

        public Task<InstantQuotationRemoveResult> RemoveAsync(
            string sessionId,
            string? ownerIdentity,
            InstantQuotationUploadReference uploadReference,
            string operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AllowVerifiedRemoval || !Guid.TryParse(uploadReference.Value, out var fileId)
                || !uploads.TryGetValue(fileId, out var item)
                || item.SessionId != sessionId || item.OwnerIdentity != ownerIdentity)
                return Task.FromResult(InstantQuotationRemoveResult.Unavailable(operationId));
            uploads.TryRemove(fileId, out _);
            return Task.FromResult(new InstantQuotationRemoveResult(operationId,
                InstantQuotationServiceStatus.Available, InstantQuotationAuthorizationStatus.Authorized,
                InstantQuotationOperationStatus.Succeeded, InstantQuotationProblemCategory.None));
        }

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(
            string sessionId,
            string? ownerIdentity,
            int quotationRequestId,
            IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref finalizeCalls);
            return Task.FromResult(InstantQuotationFinalizationResult.Unavailable(operationId));
        }
    }

    private sealed record BrowserUpload(
        string SessionId, string? OwnerIdentity, Guid FileId, string FileName, byte[] Bytes, string Sha256)
    {
        public InstantQuotationGltfUnitPolicy GltfUnitPolicy { get; init; } = InstantQuotationGltfUnitPolicy.Legacy;
    }

    private sealed class BrowserMultiUploadInputReader(
        HashCheckingUploadClient upload,
        IInstantQuotationSessionStore sessionStore) : IInstantQuotationPhysicalAnalysisInputReader
    {
        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            return upload.StartPhysicalRead(
                token => ReadCoreAsync(sessionId, ownerIdentity, partId, token), cancellationToken);
        }

        private async Task<InstantQuotationPhysicalAnalysisInputResult> ReadCoreAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            if (upload.BeforePhysicalRead is { } before) await before(partId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var session = await sessionStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            var fileId = session?.Parts.SingleOrDefault(part => part.PartId == partId)?
                .PhysicalAnalysisUpload?.FileId;
            return fileId is { } id
                ? upload.Read(sessionId, ownerIdentity, id)
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable);
        }
    }

    private sealed class BrowserUploadInputReader(BrowserUpload upload, Guid partId)
        : IInstantQuotationPhysicalAnalysisInputReader
    {
        public int ReadCount { get; private set; }

        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid requestedPartId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(sessionId == upload.SessionId
                && ownerIdentity == upload.OwnerIdentity
                && requestedPartId == partId
                ? new InstantQuotationPhysicalAnalysisInputResult(
                    upload.Bytes, InstantQuotationPhysicalAnalysisInputFailure.None,
                    upload.FileId, upload.FileName, upload.Sha256)
                {
                    GltfUnitPolicy = upload.GltfUnitPolicy,
                }
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable));
        }
    }
}
