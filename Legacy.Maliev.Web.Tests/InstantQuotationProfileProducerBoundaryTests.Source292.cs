using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using StackExchange.Redis;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationProfileProducerBoundaryTests
{
    // Reuses this test's already-owned Auth/Customer/PG/Redis and browser; never a live service.
    private static async Task Source292ProfileFailureKeepsProtectedQuantityPricesAsync(
        Uri producerOrigin, Uri authOrigin, string ownerToken, string connection,
        IConnectionMultiplexer redis, IBrowser browser)
    {
        using var wire = new Wire(producerOrigin, authOrigin);
        var quotes = new Quotes(connection);
        var tempData = new TempData();
        var uploads = new Source292ProfileUploads();
        Exception? fixtureFailure = null;
        try
        {
            await using var web = new ProfileWebFactory(wire, ownerToken, quotes, tempData, redis, services =>
            {
                services.RemoveAll<IInstantQuotationSessionStore>();
                services.AddSingleton<IInstantQuotationSessionStore, DistributedInstantQuotationSessionStore>();
                services.RemoveAll<IInstantQuotationUploadClient>();
                services.AddSingleton<IInstantQuotationUploadClient>(uploads);
                services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                services.AddScoped<IInstantQuotationPhysicalAnalysisInputReader>(provider =>
                    new Source292ProfileInput(uploads, provider.GetRequiredService<IInstantQuotationSessionStore>()));
                services.RemoveAll<IInstantQuotationSubmissionService>();
                services.AddScoped<IInstantQuotationSubmissionService>(provider => new InstantQuotationSubmissionService(
                    provider.GetRequiredService<IInstantQuotationSessionStore>(),
                    provider.GetRequiredService<IInstantQuotationPricingService>(), quotes,
                    provider.GetRequiredService<IInstantQuotationSubmissionStore>(), uploads,
                    InstantQuotationSubmissionTests.SuccessfulRequestFileClient.Instance,
                    quoteTicketService: provider.GetRequiredService<IInstantQuotationQuoteTicketService>(),
                    authoritativePricingService: provider.GetRequiredService<IInstantQuotationAuthoritativePricingService>(),
                    profileCompletionClient: provider.GetRequiredService<IInstantQuotationProfileCompletionClient>()));
            });
            var origin = new Uri($"http://127.0.0.1:{FreePort()}");
            web.UseKestrel(origin.Port);
            using var readiness = web.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
            using var response = await readiness.GetAsync("/InstantQuotation/3D-Printing?culture=en");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
            const string sha = "2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc";
            Assert.Equal(960, bytes.Length);
            Assert.Equal(sha, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            foreach (var quantity in new[] { 1, 9, 10, 49, 50, 99, 100, 101 })
            {
                tempData.Values.Clear();
                Assert.Empty(tempData.Values);
                await using var context = await browser.NewContextAsync(new()
                {
                    ViewportSize = new() { Width = 1440, Height = 900 },
                    ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Profile-Test-Owner"] = "1" },
                });
                await context.RouteAsync("**/*", route => route.Request.Url.StartsWith(origin.ToString(), StringComparison.Ordinal)
                    ? route.ContinueAsync() : route.AbortAsync());
                await using var page = await context.NewPageAsync();
                Exception? primary = null;
                Task<JsonElement>? post = null;
                var receiptTableUnavailable = false;
                try
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    await page.GotoAsync($"{origin}InstantQuotation/3D-Printing?culture=en", new() { WaitUntil = WaitUntilState.NetworkIdle });
                    var reject = page.Locator("#cookieConsent [data-consent-action='reject']");
                    if (await reject.IsVisibleAsync()) await reject.ClickAsync();
                    var remaining = 30000 - timer.ElapsedMilliseconds;
                    Assert.True(remaining > 0);
                    await using (var binding = await page.WaitForFunctionAsync(
                        "() => { const input = document.querySelector('#instant-quote-files'); return !!input && !input.disabled && typeof input._blazorInputFileNextFileId === 'number'; }",
                        options: new() { Timeout = remaining })) { }
                    await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
                    {
                        Name = "source-292-authenticated.glb",
                        MimeType = "model/gltf-binary",
                        Buffer = bytes,
                    });
                    var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
                    await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(1, new() { Timeout = 90000 });
                    await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
                    await MaterialCardBrowserActions.SelectAsync(page, "PLA");
                    await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 30000 });
                    var input = page.Locator("input[name='quantity']");
                    await input.FillAsync(quantity.ToString(CultureInfo.InvariantCulture));
                    await input.PressAsync("Tab");
                    await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 30000 });

                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var store = web.Services.GetRequiredService<IInstantQuotationSessionStore>();
                    InstantQuotationSessionState? session;
                    do
                    {
                        session = await store.GetAsync(uploads.SessionId, "customer:1", deadline.Token);
                        if (session?.QuoteAuthorization is not null && session.Parts.Count == 1
                            && session.Parts[0].Configuration.Quantity == quantity
                            && session.PhysicalReceipts?.Any(item => item.PartId == session.Parts[0].PartId
                                && item.MaterialKey == "PLA" && item.Quantity == quantity) is true) break;
                        await Task.Delay(20, deadline.Token);
                    } while (true);
                    Assert.NotNull(session);
                    Assert.Equal("customer:1", session.OwnerIdentity);
                    var part = Assert.Single(session.Parts);
                    Assert.Equal("PLA", part.Configuration.MaterialKey);
                    Assert.Equal(quantity, part.Configuration.Quantity);
                    Assert.Equal(sha, part.Geometry.Sha256);
                    var physical = Assert.Single(session.PhysicalReceipts!, item =>
                        item.PartId == part.PartId && item.MaterialKey == "PLA" && item.Quantity == quantity);
                    Assert.Equal(session.SessionId, physical.SessionId);
                    Assert.Equal(session.OwnerIdentity, physical.OwnerIdentity);
                    Assert.Equal(part.PhysicalAnalysisUpload!.FileId, physical.FileId);
                    Assert.Equal(sha, physical.UploadSha256);
                    Assert.True(physical.DepositedMm3 > 0 && physical.MotionSeconds > 0);
                    await using var scope = web.Services.CreateAsyncScope();
                    var authority = scope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>();
                    var quote = Assert.IsType<InstantQuotationOrderQuote>(await authority.QuoteAsync(
                        session, "customer:1", false, deadline.Token));
                    var line = Assert.Single(quote.Parts);
                    Assert.Equal(PrintProcess.Fdm, line.Process);
                    Assert.Equal(quantity, line.Quantity);
                    Assert.Equal(physical, line.PhysicalReceipt);
                    var expected = FdmQuantityMarginTests.ExpectedUnitPrice(line.DirectCostPerUnit,
                        PricingCatalog.ResolveMaterial("PLA")!, quantity, PricingCatalog.ResolveTier(quantity).TargetMargin);
                    Assert.Equal(expected, line.UnitPrice);
                    Assert.Equal(expected * quantity, line.Subtotal);
                    var tickets = Assert.IsType<AdditiveQuoteTicketService>(
                        scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>());
                    Assert.True(tickets.Validate(session, quote, session.QuoteAuthorization!, DateTimeOffset.UtcNow));
                    var protectedLine = tickets.UnprotectLine(Assert.Single(session.QuoteAuthorization!.LineTickets), DateTimeOffset.UtcNow);
                    Assert.Equal(quantity, protectedLine.Quantity);
                    var protectedOrder = tickets.UnprotectOrder(session.QuoteAuthorization.OrderTicket, DateTimeOffset.UtcNow);
                    Assert.Equal(Convert.ToDecimal(expected * quantity), protectedOrder.ItemsSubtotalThb);
                    Assert.Equal(Convert.ToDecimal(expected), protectedLine.UnitPriceThb);
                    Assert.Equal(Convert.ToDecimal(expected * quantity), protectedLine.SubtotalThb);

                    await review.ClickAsync();
                    await page.Locator("[data-review-continue]").ClickAsync();
                    await page.Locator("#instant-quotation-form").WaitForAsync();
                    var previousQuotes = quotes.Count;
                    var previousPosts = wire.Posts.Count;
                    quotes.RemoveReadinessAfterPersist = true;
                    // This flag means the test-owned table may have been renamed even if the browser task fails.
                    receiptTableUnavailable = true;
                    post = page.EvaluateAsync<JsonElement>("""
                        async () => {
                          const form = document.querySelector('#instant-quotation-form');
                          const body = new URLSearchParams(new FormData(form));
                          const values = {
                            FirstName:'forged', LastName:'forged', Email:'forged@example.test',
                            Mobile:'0800000000', Telephone:'020000000', Company:'forged company',
                            Country:'Thailand', TaxNumber:'0115562011815', TaxBranch:'branch', TaxBranchCode:'3',
                            BillingBuilding:'Filled building', BillingStreet1:'forged billing',
                            BillingStreet2:'forged district', BillingCity:'forged city', BillingProvince:'forged province',
                            BillingPostalCode:'99999', ShipToBillingAddress:'true', ShippingStreet1:'forged shipping',
                            ShippingStreet2:'forged district', ShippingCountry:'Thailand',
                            Description:'Source292 protected authenticated quantity.',
                            EstimatedUnitCost:'0.01 THB', EstimatedTotalCost:'0.01 THB',
                            FinalOrderPrice:'0.01', SessionId:'forged-browser-session', QuoteTicket:'forged-browser-ticket'
                          };
                          for (const [key,value] of Object.entries(values)) body.set(key,value);
                          const result = await fetch(form.action, {method:'POST',credentials:'same-origin',
                            headers:{Accept:'application/json'},body});
                          return {status:result.status,body:await result.json()};
                        }
                        """);
                    _ = post.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    var result = await post.WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.Equal(200, result.GetProperty("status").GetInt32());
                    Assert.Equal("terminal", result.GetProperty("body").GetProperty("outcome").GetString());
                    Assert.Equal("partial", tempData.Values["InstantQuotationSubmissionStatus"]);
                    Assert.Equal(417, tempData.Values["InstantQuotationRequestReference"]);
                    Assert.Equal(previousQuotes + 1, quotes.Count);
                    Assert.Equal(previousPosts + 1, wire.Posts.Count);
                    Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, wire.PostStatuses[^1]);
                    Assert.Equal(0, uploads.FinalizationCalls);
                    var submission = quotes.Submissions[previousQuotes];
                    Assert.Equal("Owner", submission.FirstName);
                    Assert.Equal("current@example.test", submission.Email);
                    Assert.Contains($"Quantity: {quantity} piece(s)", submission.Message, StringComparison.Ordinal);
                    Assert.Contains($"Cost per unit: {expected.ToString("0.00", CultureInfo.InvariantCulture)} THB", submission.Message, StringComparison.Ordinal);
                    Assert.Contains($"Total cost: {(expected * quantity).ToString("0.00", CultureInfo.InvariantCulture)} THB", submission.Message, StringComparison.Ordinal);
                    Assert.DoesNotContain("0.01 THB", submission.Message, StringComparison.Ordinal);
                    Assert.DoesNotContain("forged-browser", submission.Message, StringComparison.Ordinal);
                    using var checkpointDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await using var lease = await web.Services.GetRequiredService<IInstantQuotationSubmissionStore>()
                        .TryAcquireAsync(session.SubmissionId, "customer:1", checkpointDeadline.Token);
                    Assert.NotNull(lease);
                    var checkpoint = Assert.IsType<InstantQuotationSubmissionCheckpoint>((await lease.ReadAsync(checkpointDeadline.Token)).Checkpoint);
                    Assert.Equal(417, checkpoint.RequestReference);
                    Assert.Equal(InstantQuotationSubmissionCheckpointStatus.Persisted, checkpoint.Status);
                    Assert.False(checkpoint.ProfileCompleted);
                    Assert.NotNull(checkpoint.FrozenCustomer?.ProfileCompletion);
                    Assert.Equal(wire.Posts[^1].Key, checkpoint.FrozenCustomer!.ProfileCompletion!.Key.ToString("D"));
                }
                catch (Exception error) { primary = error; throw; }
                finally
                {
                    await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                        async () =>
                        {
                            var close = page.CloseAsync();
                            _ = close.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                            try { await close.WaitAsync(TimeSpan.FromSeconds(5)); }
                            catch (Exception error) { error.Data["Source292OwnedPageClose"] = close; throw; }
                        },
                        async () =>
                        {
                            if (post is null) return;
                            MaterialPricingQuoteCleanupReceipt? observed = null;
                            await MaterialPricingTestCleanup.DrainQuoteAsync(post, static () => { }, static () => { },
                                CancellationToken.None, primary, receipt => observed = receipt, TimeSpan.FromSeconds(5), taskKind: "browser-post");
                            if (observed is { ActualTaskSettled: false })
                            {
                                var error = new TimeoutException("Owned authenticated browser POST did not settle after page closure.");
                                error.Data["Source292OwnedPost"] = post;
                                error.Data["Source292PostCleanup"] = observed;
                                throw error;
                            }
                        },
                        async () =>
                        {
                            if (!receiptTableUnavailable) return;
                            // Exactly this fixture's disposable receipt table; no shared/production connection.
                            await Sql(connection, """
                                DO $$ BEGIN
                                  IF to_regclass('"UnavailableProfileReceipt"') IS NOT NULL THEN
                                    ALTER TABLE "UnavailableProfileReceipt" RENAME TO "QuotationProfileCompletionOperation";
                                  END IF;
                                END $$;
                                """);
                        });
                }
            }
        }
        catch (Exception error) { fixtureFailure = error; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(fixtureFailure, async () => await uploads.DisposeAsync());
        }
    }

    private sealed class Source292ProfileUploads : IInstantQuotationUploadClient, IAsyncDisposable
    {
        private readonly CancellationTokenSource lifetime = new();
        private readonly object gate = new();
        private readonly List<Task> pending = [];
        private readonly Dictionary<Guid, Source292Upload> uploads = [];
        private bool closed;
        public string SessionId { get; private set; } = "";
        public int FinalizationCalls { get; private set; }
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content,
            string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim claim,
            string operationId, CancellationToken cancellationToken) => Own(async token =>
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, token);
            var bytes = copy.ToArray();
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(contentLength, bytes.LongLength);
            Assert.Equal(claim.Sha256, sha);
            var id = Guid.NewGuid();
            lock (gate) { SessionId = sessionId; uploads.Add(id, new(sessionId, ownerIdentity, fileName, bytes, sha)); }
            return InstantQuotationUploadResult.Succeeded(operationId, new(id.ToString("D")), sha,
                new(id, fileName, contentType, bytes.LongLength, sha, "clean"));
        }, cancellationToken);
        public Task<T> Own<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (closed) throw new InvalidOperationException("Fixture admission is closed.");
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var actual = RunAsync();
                pending.Add(actual);
                _ = actual.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                start.SetResult();
                return actual;

                async Task<T> RunAsync()
                {
                    await start.Task;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
                    return await action(linked.Token);
                }
            }
        }
        public InstantQuotationPhysicalAnalysisInputResult Read(string session, string? owner, Guid file)
        {
            lock (gate) return uploads.TryGetValue(file, out var upload) && upload.Session == session && upload.Owner == owner
                ? new(upload.Bytes, InstantQuotationPhysicalAnalysisInputFailure.None, file, upload.Name, upload.Sha)
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(InstantQuotationPhysicalAnalysisInputFailure.UploadMismatch);
        }
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity, int requestId,
            IReadOnlyList<InstantQuotationUploadReference> references, string operationId, CancellationToken cancellationToken) =>
            RecordUnexpectedFinalization();
        private Task<InstantQuotationFinalizationResult> RecordUnexpectedFinalization()
        {
            FinalizationCalls++;
            throw new InvalidOperationException("Profile failure must stop before file finalization.");
        }
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference reference, string operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async ValueTask DisposeAsync()
        {
            try { await DrainAsync(); }
            finally
            {
                lock (gate)
                    if (pending.All(task => task.IsCompleted)) lifetime.Dispose();
            }
        }
        public async ValueTask DrainAsync()
        {
            lock (gate) closed = true;
            lifetime.Cancel();
            Task[] tasks; lock (gate) tasks = pending.ToArray();
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (tasks.All(task => task.IsCompleted)) { }
            catch (Exception error)
            {
                error.Data["Source292OwnedUploadAndReadTasks"] = tasks;
                throw;
            }
        }
    }
    private sealed record Source292Upload(string Session, string? Owner, string Name, byte[] Bytes, string Sha);
    private sealed class Source292ProfileInput(Source292ProfileUploads upload, IInstantQuotationSessionStore store)
        : IInstantQuotationPhysicalAnalysisInputReader
    {
        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(string sessionId, string? ownerIdentity,
            Guid partId, CancellationToken cancellationToken) => upload.Own(async token =>
        {
            var session = await store.GetAsync(sessionId, ownerIdentity, token);
            var part = session?.Parts.SingleOrDefault(item => item.PartId == partId);
            return part?.PhysicalAnalysisUpload is { } physical
                ? upload.Read(sessionId, ownerIdentity, physical.FileId)
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable);
        }, cancellationToken);
    }
}
