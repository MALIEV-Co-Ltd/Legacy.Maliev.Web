using System.Net;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationWallThicknessRealUploadBrowserTests
{
    [Fact]
    public async Task Source292SevenPartBatch_SettledBrowserSubtotalMatchesProtectedLinesAndOrder()
    {
        const int partCount = 7;
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
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            Locale = "en-US",
        });
        Exception? primary = null;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            await WaitForInteractiveInputFileAsync(page, timer);
            var bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
            const string sourceHash = "2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc";
            Assert.Equal(960, bytes.Length);
            Assert.Equal(sourceHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            await page.Locator("[data-workflow-gltf-unit]").SelectOptionAsync("millimetres");
            await page.SetInputFilesAsync("#instant-quote-files", Enumerable.Range(1, partCount).Select(index => new FilePayload
            {
                Name = $"source-292-seven-{index}.glb",
                MimeType = "model/gltf-binary",
                Buffer = bytes,
            }));
            await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(partCount,
                new() { Timeout = 90000 });
            await Assertions.Expect(page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions > button"))
                .ToBeEnabledAsync(new() { Timeout = 90000 });
            await page.WaitForFunctionAsync("""
                () => !document.querySelector('[data-pricing-loading-status]')
                  && document.querySelector('[data-workflow-order-summary]')?.getAttribute('aria-busy') === 'false'
                """, null, new() { Timeout = 30000 });

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
            InstantQuotationSessionState? session;
            do
            {
                session = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, deadline.Token);
                if (session?.Parts.Count == partCount && session.QuoteAuthorization?.LineTickets.Count == partCount
                    && session.Parts.All(part => part.Configuration.MaterialKey == "PLA" && part.Configuration.Quantity == 1
                        && session.PhysicalReceipts?.Any(receipt => receipt.PartId == part.PartId
                            && receipt.MaterialKey == "PLA" && receipt.Quantity == 1) is true)) break;
                await Task.Delay(20, deadline.Token);
            } while (true);
            Assert.NotNull(session);
            Assert.Equal(partCount, upload.VerifiedUploads);
            Assert.Equal(partCount, upload.StartedUploads);
            Assert.Equal(partCount, session.Parts.Select(part => part.PartId).Distinct().Count());
            Assert.Equal(partCount, session.Parts.Select(part => part.PhysicalAnalysisUpload!.FileId).Distinct().Count());
            foreach (var part in session.Parts)
            {
                Assert.Equal(sourceHash, part.Geometry.Sha256);
                var physical = Assert.Single(session.PhysicalReceipts!, receipt =>
                    receipt.PartId == part.PartId && receipt.MaterialKey == "PLA" && receipt.Quantity == 1);
                Assert.Equal(part.PhysicalAnalysisUpload!.FileId, physical.FileId);
                Assert.Equal(session.SessionId, physical.SessionId);
                Assert.Equal(session.OwnerIdentity, physical.OwnerIdentity);
                Assert.Equal(sourceHash, physical.UploadSha256);
                Assert.True(physical.DepositedMm3 > 0 && physical.MotionSeconds > 0);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var quote = Assert.IsType<InstantQuotationOrderQuote>(await scope.ServiceProvider
                .GetRequiredService<IInstantQuotationAuthoritativePricingService>()
                .QuoteAsync(session, session.OwnerIdentity, includeComparisons: false, deadline.Token));
            Assert.Equal(partCount, quote.Parts.Count);
            Assert.All(quote.Parts, line =>
            {
                Assert.Equal(1, line.Quantity);
                Assert.True(line.Subtotal > 0);
                Assert.Equal(line.UnitPrice * line.Quantity, line.Subtotal);
            });
            var tickets = Assert.IsType<AdditiveQuoteTicketService>(
                scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>());
            var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(session.QuoteAuthorization);
            Assert.Equal(partCount, authorization.LineTickets.Distinct(StringComparer.Ordinal).Count());
            Assert.False(string.IsNullOrWhiteSpace(authorization.OrderTicket));
            Assert.True(tickets.Validate(session, quote, authorization, DateTimeOffset.UtcNow));
            var lines = authorization.LineTickets.Select(ticket => tickets.UnprotectLine(ticket, DateTimeOffset.UtcNow)).ToArray();
            Assert.Equal(partCount, lines.Select(line => line.PartId).Distinct().Count());
            Assert.All(lines, line =>
            {
                Assert.Equal(1, line.Quantity);
                Assert.True(line.SubtotalThb > 0);
                Assert.Equal(line.UnitPriceThb * line.Quantity, line.SubtotalThb);
            });
            var subtotal = lines.Sum(line => line.SubtotalThb);
            var order = tickets.UnprotectOrder(authorization.OrderTicket, DateTimeOffset.UtcNow);
            Assert.True(subtotal > 0);
            Assert.Equal(subtotal, order.ItemsSubtotalThb);
            Assert.Equal(Convert.ToDecimal(quote.ItemsSubtotal), subtotal);
            Assert.Equal(Convert.ToDecimal(quote.Parts.Sum(line => line.Subtotal)), subtotal);
            Assert.Equal(Convert.ToDecimal(quote.FinalOrderPrice), order.FinalOrderPriceThb);
            Assert.True(order.FinalOrderPriceThb > subtotal);
            Assert.Equal(authorization.LineTickets.Select(AdditiveQuoteTicketService.DigestTicket),
                order.LineTicketDigests);

            var summary = page.Locator("[data-workflow-order-summary]");
            await Assertions.Expect(summary).ToHaveAttributeAsync("aria-busy", "false");
            await Assertions.Expect(page.Locator("[data-pricing-loading-status]")).ToHaveCountAsync(0);
            var dock = page.Locator("[data-workflow-summary-dock]");
            if (!await dock.EvaluateAsync<bool>("element => element.open")) await dock.Locator("summary").ClickAsync();
            var displayedSubtotal = summary.GetByText("Subtotal", new() { Exact = true }).Locator("..").Locator("dd");
            var displayedTotal = summary.GetByText("Total", new() { Exact = true }).Locator("..").Locator("dd");
            await Assertions.Expect(displayedSubtotal).ToBeVisibleAsync();
            await Assertions.Expect(displayedSubtotal).ToHaveTextAsync(Source292Money(Convert.ToDouble(subtotal)));
            await Assertions.Expect(displayedTotal).ToHaveTextAsync(Source292Money(Convert.ToDouble(order.FinalOrderPriceThb)));
            var after = Assert.IsType<InstantQuotationSessionState>(
                await store.GetAsync(session.SessionId, session.OwnerIdentity, deadline.Token));
            Assert.Equal(authorization.LineTickets, after.QuoteAuthorization!.LineTickets);
            Assert.Equal(authorization.OrderTicket, after.QuoteAuthorization.OrderTicket);
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
}
