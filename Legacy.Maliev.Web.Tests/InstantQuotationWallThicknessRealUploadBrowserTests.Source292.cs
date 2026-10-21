using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

// Source292's three eight-quantity matrices over the existing real-reader fixture.
// Production submission service, recorded Quotation boundary and test CAS store; not Redis integration.
// Execution remains subject to the owner's unchanged native/financial capability admission.
public sealed partial class InstantQuotationWallThicknessRealUploadBrowserTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public Task Source292QuantityBoundary_BrowserDisplaysProtectedServerMarginOnce(int quantity) =>
        WithSource292PricedPartAsync(quantity, async state =>
        {
            var line = Assert.Single(state.Quote.Parts);
            var prices = state.Page.Locator("[data-workflow-part-price] > div");
            await Assertions.Expect(prices.Filter(new() { HasText = "Unit price" }).Locator("dd"))
                .ToHaveTextAsync(Source292Money(line.UnitPrice));
            await Assertions.Expect(prices.Filter(new() { HasText = "Subtotal" }).Locator("dd"))
                .ToHaveTextAsync(Source292Money(line.Subtotal));
            await Assertions.Expect(state.Page.Locator("input[name='quantity']"))
                .ToHaveValueAsync(quantity.ToString(CultureInfo.InvariantCulture));
            await Assertions.Expect(state.Page.Locator("[data-workflow-price-tier][aria-current='true']"))
                .ToContainTextAsync(quantity.ToString("N0", CultureInfo.GetCultureInfo("en-US")));
            var saved = Assert.IsType<InstantQuotationSessionState>(await state.Application.Services
                .GetRequiredService<IInstantQuotationSessionStore>()
                .GetAsync(state.Session.SessionId, state.Session.OwnerIdentity, default));
            Assert.Equal(state.Session.QuoteAuthorization!.LineTickets, saved.QuoteAuthorization!.LineTickets);
            Assert.Equal(state.Session.QuoteAuthorization.OrderTicket, saved.QuoteAuthorization.OrderTicket);
        });

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public Task Source292QuantityBoundary_ProtectedWorkflowTotalsKeepRoundedLinesAndVatOnce(int quantity) =>
        WithSource292PricedPartAsync(quantity, state =>
        {
            var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(state.Session.QuoteAuthorization);
            var line = state.Tickets.UnprotectLine(Assert.Single(authorization.LineTickets), DateTimeOffset.UtcNow);
            var order = state.Tickets.UnprotectOrder(authorization.OrderTicket, DateTimeOffset.UtcNow);
            var quote = Assert.Single(state.Quote.Parts);
            var subtotal = Convert.ToDecimal(quote.UnitPrice) * quantity;
            var priceBeforeVat = Convert.ToDecimal(state.Quote.Printing + state.Quote.ShippingCost);
            var vat = decimal.Round(priceBeforeVat * 0.07m, 2, MidpointRounding.AwayFromZero);

            Assert.Equal(quantity, line.Quantity);
            Assert.Equal(Convert.ToDecimal(quote.UnitPrice), line.UnitPriceThb);
            Assert.Equal(subtotal, line.SubtotalThb);
            Assert.Equal(subtotal, order.ItemsSubtotalThb);
            Assert.Equal(Convert.ToDecimal(state.Quote.MinimumOrderSurcharge), order.MinimumOrderSurchargeThb);
            Assert.Equal(Convert.ToDecimal(state.Quote.ShippingCost), order.ShippingThb);
            Assert.Equal(vat, order.VatThb);
            Assert.Equal(priceBeforeVat + vat, order.FinalOrderPriceThb);
            Assert.Equal(order.FinalOrderPriceThb, order.AllocatedLineTotalsThb.Sum());
            Assert.Equal(AdditiveQuoteTicketService.DigestTicket(authorization.LineTickets[0]),
                Assert.Single(order.LineTicketDigests));

            var part = Assert.Single(state.Session.Parts);
            var changed = state.Session with
            {
                RequestState = new([part with
                {
                    Configuration = part.Configuration with { Quantity = quantity + 1 },
                }]),
            };
            Assert.False(state.Tickets.Validate(changed, state.Quote, authorization, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public Task Source292QuantityBoundary_PersistedRequestIgnoresPostedMoney(int quantity) =>
        WithSource292PricedPartAsync(quantity, async state =>
        {
            await state.Page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").ClickAsync();
            await state.Page.Locator("[data-review-continue]").ClickAsync();
            await state.Page.Locator("#instant-quotation-form").WaitForAsync();
            var post = state.Page.EvaluateAsync<JsonElement>("""
                async () => {
                  const form = document.querySelector('#instant-quotation-form');
                  const body = new URLSearchParams(new FormData(form));
                  const customer = {
                    FirstName: 'Mali', LastName: 'Ev', Email: 'quantity@example.test',
                    Mobile: '0890000000', Telephone: '020000000', Country: 'Thailand',
                    BillingStreet1: 'Billing Road', BillingCity: 'Bangkok',
                    BillingProvince: 'Bangkok', BillingPostalCode: '10110',
                    ShipToBillingAddress: 'true', TaxNumber: '', TaxBranch: 'head-office',
                    Description: 'Please review the protected quotation.',
                    EstimatedUnitCost: '0.01 THB', EstimatedTotalCost: '0.01 THB',
                    FinalOrderPrice: '0.01', QuoteTicket: 'forged-browser-ticket',
                    SessionId: 'forged-browser-session',
                    Parts: '[{"quantity":9999,"unitPrice":0.01,"subtotal":0.01}]'
                  };
                  for (const [key, value] of Object.entries(customer)) body.set(key, value);
                  const response = await fetch(form.action, {
                    method: 'POST', body, credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                  });
                  const result = await response.json();
                  return { status: response.status, outcome: result.outcome };
                }
                """);
            state.OwnedPost = post;
            _ = post.ContinueWith(static done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var response = await post.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(200, response.GetProperty("status").GetInt32());
            Assert.Equal("terminal", response.GetProperty("outcome").GetString());
            var persisted = Assert.Single(state.Boundary.Requests);
            var result = Assert.Single(state.Boundary.Results);
            // The existing upload boundary refuses finalization after the real request checkpoint.
            Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
            Assert.Equal(417, result.RequestReference);
            Assert.Same(state.Checkpoints, state.Application.Services.GetRequiredService<IInstantQuotationSubmissionStore>());
            var checkpoint = Assert.IsType<InstantQuotationSubmissionCheckpoint>(state.Checkpoints.Checkpoint);
            Assert.Equal(state.Session.SubmissionId, checkpoint.SubmissionId);
            Assert.Equal(417, checkpoint.RequestReference);
            Assert.Equal(InstantQuotationSubmissionCheckpointStatus.Persisted, checkpoint.Status);
            Assert.Null(Assert.Single(state.Checkpoints.ExpectedPriorStatuses));
            Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
            Assert.Equal($"legacy-web-instant-quotation-{state.Session.SubmissionId.ToLowerInvariant()}",
                persisted.IdempotencyKey);
            var quote = Assert.Single(state.Quote.Parts);
            Assert.Contains($"Quantity: {quantity} piece(s)", persisted.Submission.Message, StringComparison.Ordinal);
            Assert.Contains($"Cost per unit: {quote.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)} THB",
                persisted.Submission.Message, StringComparison.Ordinal);
            Assert.Contains($"Total cost: {quote.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)} THB",
                persisted.Submission.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("0.01 THB", persisted.Submission.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("forged-browser", persisted.Submission.Message, StringComparison.Ordinal);
            Assert.Equal(quantity, Assert.Single((await state.Application.Services
                .GetRequiredService<IInstantQuotationSessionStore>().GetAsync(
                    state.Session.SessionId, state.Session.OwnerIdentity, default))!.Parts).Configuration.Quantity);
        });

    private async Task WithSource292PricedPartAsync(int quantity, Func<Source292PricedPart, Task> assertion)
    {
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true, OwnUploadLifetime = true };
        var boundary = new Source292RequestBoundary();
        var checkpoints = new InstantQuotationSubmissionTests.RecordingSubmissionStore();
        await using var baseFactory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService());
        await using var application = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQuotationClient>();
            services.AddSingleton<IQuotationClient>(boundary);
            services.RemoveAll<IInstantQuotationSubmissionStore>();
            services.AddSingleton<IInstantQuotationSubmissionStore>(checkpoints);
            services.RemoveAll<IInstantQuotationSubmissionService>();
            services.AddScoped<IInstantQuotationSubmissionService>(provider => new Source292SubmissionObserver(
                ActivatorUtilities.CreateInstance<InstantQuotationSubmissionService>(provider), boundary));
        }));
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        application.UseKestrel(origin.Port);
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        var url = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        using var readiness = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        Exception? primary = null;
        Source292PricedPart? state = null;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await WaitForInteractiveInputFileAsync(page, timer);
            var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
            Assert.Equal(960, bytes.Length);
            const string sourceHash = "2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc";
            Assert.Equal(sourceHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
            {
                Name = "source-292-quantity.glb",
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
            await page.WaitForFunctionAsync("""
                quantity => Number(document.querySelector('input[name="quantity"]').value) === quantity
                  && !document.querySelector('[data-pricing-loading-status]')
                """, quantity, new() { Timeout = 30000 });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var store = application.Services.GetRequiredService<IInstantQuotationSessionStore>();
            InstantQuotationSessionState? session;
            do
            {
                session = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, deadline.Token);
                if (session?.QuoteAuthorization is not null
                    && session.Parts.Count == 1 && session.Parts[0].Configuration.Quantity == quantity
                    && session.PhysicalReceipts?.Any(receipt => receipt.PartId == session.Parts[0].PartId
                        && receipt.MaterialKey == "PLA" && receipt.Quantity == quantity) is true) break;
                await Task.Delay(20, deadline.Token);
            } while (true);
            Assert.NotNull(session);
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Equal(1, upload.StartedUploads);
            var part = Assert.Single(session.Parts);
            Assert.Equal("PLA", part.Configuration.MaterialKey);
            Assert.Equal(sourceHash, part.Geometry.Sha256);
            var receipt = Assert.Single(session.PhysicalReceipts!, candidate =>
                candidate.PartId == part.PartId && candidate.MaterialKey == "PLA" && candidate.Quantity == quantity);
            Assert.Equal(session.SessionId, receipt.SessionId);
            Assert.Equal(session.OwnerIdentity, receipt.OwnerIdentity);
            Assert.Equal(part.PhysicalAnalysisUpload!.FileId, receipt.FileId);
            Assert.Equal(sourceHash, receipt.UploadSha256);
            Assert.True(receipt.DepositedMm3 > 0 && receipt.MotionSeconds > 0);
            await using var scope = application.Services.CreateAsyncScope();
            var authoritative = scope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>();
            var quote = Assert.IsType<InstantQuotationOrderQuote>(await authoritative.QuoteAsync(
                session, session.OwnerIdentity, includeComparisons: false, deadline.Token));
            var line = Assert.Single(quote.Parts);
            Assert.Equal(PrintProcess.Fdm, line.Process);
            Assert.Equal(receipt, line.PhysicalReceipt);
            Assert.Equal(quantity, line.Quantity);
            var material = PricingCatalog.ResolveMaterial("PLA")!;
            var expected = FdmQuantityMarginTests.ExpectedUnitPrice(line.DirectCostPerUnit, material, quantity,
                PricingCatalog.ResolveTier(quantity).TargetMargin);
            Assert.Equal(expected, line.UnitPrice);
            Assert.Equal(expected * quantity, line.Subtotal);
            Assert.Equal(0, line.UnitPrice % 10);
            var tickets = Assert.IsType<AdditiveQuoteTicketService>(
                scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>());
            Assert.True(tickets.Validate(session, quote, session.QuoteAuthorization!, DateTimeOffset.UtcNow));
            state = new(page, application, session, quote, tickets, boundary, checkpoints);
            await assertion(state);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () => { upload.StopUploadAdmission(); return ValueTask.CompletedTask; },
                async () => await CloseOwnedPageAsync(page, playwright),
                async () =>
                {
                    if (state?.OwnedPost is not { } post) return;
                    await MaterialPricingTestCleanup.DrainQuoteAsync(
                        post, static () => { }, static () => { }, CancellationToken.None, primary,
                        receipt =>
                        {
                            state.PostCleanup = receipt;
                            if (primary is not null) primary.Data["Source292PostCleanup"] = receipt;
                        },
                        TimeSpan.FromSeconds(5), taskKind: "browser-post");
                    if (state.PostCleanup is { ActualTaskSettled: false } pending)
                    {
                        var failure = new TimeoutException("The owned browser POST did not settle after page closure.");
                        failure.Data["Source292OwnedPost"] = post;
                        failure.Data["Source292PostCleanup"] = pending;
                        throw failure;
                    }
                },
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

    private static string Source292Money(double amount) =>
        "฿" + amount.ToString("N2", CultureInfo.GetCultureInfo("en-US"));

    private sealed record Source292PricedPart(
        IPage Page, WebApplicationFactory<Program> Application, InstantQuotationSessionState Session,
        InstantQuotationOrderQuote Quote, AdditiveQuoteTicketService Tickets, Source292RequestBoundary Boundary,
        InstantQuotationSubmissionTests.RecordingSubmissionStore Checkpoints)
    {
        public Task<JsonElement>? OwnedPost { get; set; }
        public MaterialPricingQuoteCleanupReceipt? PostCleanup { get; set; }
    }

    private sealed record Source292Request(QuotationRequestSubmission Submission, string IdempotencyKey);

    private sealed class Source292RequestBoundary : IQuotationClient
    {
        public ConcurrentQueue<Source292Request> Requests { get; } = new();
        public ConcurrentQueue<InstantQuotationSubmissionResult> Results { get; } = new();

        public Task<QuotationRequestResult> CreateRequestAsync(
            QuotationRequestSubmission submission, string idempotencyKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(new(submission, idempotencyKey));
            return Task.FromResult(new QuotationRequestResult(
                417, ServiceAvailable: true, Authorized: true, TransactionId: "request-417", JourneyId: submission.JourneyId));
        }
    }

    private sealed class Source292SubmissionObserver(
        IInstantQuotationSubmissionService inner, Source292RequestBoundary boundary) : IInstantQuotationSubmissionService
    {
        public async Task<InstantQuotationSubmissionResult> SubmitAsync(
            string sessionId, string? ownerIdentity, InstantQuotationCustomerSubmission customer, CancellationToken cancellationToken)
        {
            var result = await inner.SubmitAsync(sessionId, ownerIdentity, customer, cancellationToken);
            boundary.Results.Enqueue(result);
            return result;
        }
    }
}
