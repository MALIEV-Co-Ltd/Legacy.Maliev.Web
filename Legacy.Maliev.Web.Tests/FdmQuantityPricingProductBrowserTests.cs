using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationWallThicknessRealUploadBrowserTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public async Task FdmQuantityBoundary_RealUploadReviewAndNormalHttpPostPreserveMarginOnce(int quantity)
    {
        var fixture = new QuantityOwnedFixture();
        await RunQuantityScenarioAsync(async custody =>
        {
            // Own the unique namespace before the existing writer can create a partial file.
            custody.Own("fixture-file", fixture.CleanupAsync);
            await RunQuantityBodyAsync(quantity, fixture, custody);
        }, faults =>
        {
            foreach (var fault in faults)
            {
                QuantityPhase($"cleanup-fault-{fault.Resource}-{fault.Error.GetType().Name}", quantity);
            }

            QuantityPhase(faults.Count == 0 ? "upload-file-released" : "cleanup-with-faults", quantity);
        });
    }

    private async Task RunQuantityBodyAsync(int quantity, QuantityOwnedFixture fixture, QuantityScenarioCustody custody)
    {
        // Cooperative HTTP/service deadline; not a hard bound on factory or Playwright acquisition.
        // Native fixture admission and complete acquisition/cleanup evidence remain separate gates.
        var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        custody.Own("deadline", () => { deadline.Dispose(); return ValueTask.CompletedTask; });
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        var requests = new QuantityRequestClient();
        var storage = new InstantQuotationSubmissionStoreTests.FakeAtomicStorage();
        var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing,
            configureQuantityDependencies: services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, QuantityCountryClient>();
                services.RemoveAll<IQuotationClient>();
                services.AddSingleton<IQuotationClient>(requests);
                services.RemoveAll<IInstantQuotationSubmissionStore>();
                services.AddSingleton<IInstantQuotationSubmissionStore>(provider =>
                    new InstantQuotationSubmissionStore(storage,
                        provider.GetRequiredService<IDataProtectionProvider>(),
                        NullLogger<InstantQuotationSubmissionStore>.Instance));
            });
        custody.Own("factory", factory.DisposeAsync);
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        factory.UseKestrel(origin.Port);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        custody.Own("client", () => { client.Dispose(); return ValueTask.CompletedTask; });
        client.Timeout = TimeSpan.FromSeconds(30);
        var readiness = await client.GetAsync("/liveness", deadline.Token);
        custody.Own("readiness-response", () => { readiness.Dispose(); return ValueTask.CompletedTask; });
        Assert.True(readiness.IsSuccessStatusCode);
        QuantityPhase("host-ready", quantity);
        var playwright = await Playwright.CreateAsync();
        custody.Own("playwright-driver", () => { playwright.Dispose(); return ValueTask.CompletedTask; });
        var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 });
        custody.Own("browser", browser.DisposeAsync);
        var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        custody.Own("browser-context", context.DisposeAsync);
        var page = await context.NewPageAsync();
        custody.Own("page", page.DisposeAsync);
        page.SetDefaultTimeout(30000);
        page.SetDefaultNavigationTimeout(30000);
        await page.GotoAsync(new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.IsVisibleAsync()) await consent.ClickAsync();
        var path = fixture.Create(stem => CreateBoxStl(20, 20, 5, stem));
        {
            QuantityPhase("upload-start", quantity);
            await page.SetInputFilesAsync("#instant-quote-files", path);
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await Assertions.Expect(review).ToBeEnabledAsync();
            await page.Locator("[data-workflow-material-picker] select[name='material']").SelectOptionAsync("PLA");
            await Assertions.Expect(review).ToBeEnabledAsync();
            await page.Locator("input[name='quantity']").FillAsync(quantity.ToString(CultureInfo.InvariantCulture));
            await page.Locator("input[name='quantity']").PressAsync("Tab");
            await Assertions.Expect(review).ToBeEnabledAsync();
            var scope = factory.Services.CreateScope();
            custody.Own("service-scope", () => { scope.Dispose(); return ValueTask.CompletedTask; });
            var store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
            var session = Assert.IsType<InstantQuotationSessionState>(
                await store.GetAsync(upload.SessionId, upload.OwnerIdentity, deadline.Token));
            var part = Assert.Single(session.Parts);
            Assert.Equal(quantity, part.Configuration.Quantity);
            Assert.Equal("PLA", part.Configuration.MaterialKey);
            await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(part.PartId);
            var quote = Assert.IsType<InstantQuotationOrderQuote>(await scope.ServiceProvider
                .GetRequiredService<IInstantQuotationAuthoritativePricingService>()
                .QuoteAsync(session, upload.OwnerIdentity, false, deadline.Token));
            var line = Assert.Single(quote.Parts);
            var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
            Assert.Equal(quantity, receipt.Quantity);
            Assert.Equal(part.PartId, receipt.PartId);
            Assert.Equal(part.Geometry.Sha256, receipt.UploadSha256);
            Assert.Equal(session.SessionId, receipt.SessionId);
            Assert.True(receipt.MotionSeconds > 0);
            var material = PricingCatalog.ResolveMaterial("PLA")!;
            // Linkage agreement with the production commercial path, not an independent money oracle.
            // Task1's 80000mm3/3600s literals do not describe this genuine 20x20x5 upload.
            var expectedUnit = FdmQuantityMarginTests.ExpectedUnitPrice(line.DirectCostPerUnit,
                material, quantity, PricingCatalog.ResolveTier(quantity).TargetMargin, discount: 0);
            Assert.Equal(expectedUnit, line.UnitPrice);
            Assert.Equal(expectedUnit * quantity, line.Subtotal);
            var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(session.QuoteAuthorization);
            var tickets = Assert.IsType<AdditiveQuoteTicketService>(scope.ServiceProvider
                .GetRequiredService<IInstantQuotationQuoteTicketService>());
            Assert.True(tickets.Validate(session, quote, authorization, DateTimeOffset.UtcNow));
            var protectedLine = tickets.UnprotectLine(Assert.Single(authorization.LineTickets), DateTimeOffset.UtcNow);
            var protectedOrder = tickets.UnprotectOrder(authorization.OrderTicket, DateTimeOffset.UtcNow);
            Assert.Equal(PricingCatalog.AdditivePricingPolicyVersion, protectedLine.PolicyVersion);
            Assert.NotEqual("additive-2026-10-10.v10", protectedLine.PolicyVersion);
            Assert.Equal(quantity, protectedLine.Quantity);
            Assert.Equal(Convert.ToDecimal(expectedUnit), protectedLine.UnitPriceThb);
            Assert.Equal(Convert.ToDecimal(expectedUnit * quantity), protectedLine.SubtotalThb);
            Assert.Equal(Convert.ToDecimal(quote.FinalOrderPrice), protectedOrder.FinalOrderPriceThb);
            QuantityPhase("physical-ticket-ready", quantity);
            await Assertions.Expect(page.Locator("[data-workflow-material-price='PLA'] strong"))
                .ToHaveTextAsync($"฿{expectedUnit.ToString("N2", CultureInfo.InvariantCulture)} per piece");
            await review.ClickAsync();
            var reviewed = page.Locator("[data-workflow-review-part]");
            await Assertions.Expect(reviewed.Locator("input[type='number']")).ToHaveValueAsync(quantity.ToString(CultureInfo.InvariantCulture));
            await Assertions.Expect(reviewed.Locator(".instant-quote__review-prices dd").Nth(0))
                .ToHaveTextAsync($"฿{expectedUnit.ToString("N2", CultureInfo.InvariantCulture)}");
            await Assertions.Expect(reviewed.Locator(".instant-quote__review-prices dd").Nth(1))
                .ToHaveTextAsync($"฿{(expectedUnit * quantity).ToString("N2", CultureInfo.InvariantCulture)}");
            await Assertions.Expect(page.Locator("[data-workflow-review-content] .instant-quote__pricing-summary"))
                .ToContainTextAsync($"฿{quote.FinalOrderPrice.ToString("N2", CultureInfo.InvariantCulture)}");
            QuantityPhase("review-visible", quantity);
            await page.Locator("[data-review-continue]").ClickAsync();
            var form = page.Locator("#instant-quotation-form");
            await Assertions.Expect(form).ToBeVisibleAsync();
            foreach (var field in new Dictionary<string, string>
            {
                ["FirstName"] = "Quantity", ["LastName"] = "Regression", ["Email"] = "quantity@example.test",
                ["Mobile"] = "+66 80 000 0000", ["Telephone"] = "+66 80 000 0000", ["BillingBuilding"] = "88", ["BillingStreet1"] = "Test Road",
                ["BillingCity"] = "Bangkok", ["BillingProvince"] = "Bangkok", ["BillingPostalCode"] = "10110",
            }) await form.Locator($"[name='{field.Key}']").FillAsync(field.Value);
            await form.Locator("[name='Country']").SelectOptionAsync(new SelectOptionValue { Label = "Thailand" });
            await form.Locator("input[type='checkbox'][name='ShipToBillingAddress']").CheckAsync();
            // Add hostile browser money to the genuine form; the normal endpoint must use server authorization.
            await form.EvaluateAsync("""
                form => { for (const [name, value] of [['FinalOrderPrice','0.01'], ['Parts','[{"quantity":999,"unitPrice":0.01}]']]) {
                  const field = document.createElement('input'); field.type = 'hidden'; field.name = name; field.value = value; form.appendChild(field);
                } }
                """);
            QuantityPhase("normal-http-post-start", quantity);
            var response = await page.RunAndWaitForResponseAsync(
                () => page.Locator("[data-instant-quote-submit]").ClickAsync(),
                response => response.Request.Method == "POST" && response.Url.Contains("handler=SubmitRequest", StringComparison.Ordinal));
            Assert.InRange(response.Status, 200, 399);
            await response.FinishedAsync();
            Assert.True(requests.Created.Task.IsCompletedSuccessfully,
                "The completed normal POST must have created its request before returning.");
            var submitted = await requests.Created.Task;
            Assert.Contains($"Quantity: {quantity} piece(s)", submitted.Message, StringComparison.Ordinal);
            Assert.Contains($"Cost per unit: {expectedUnit.ToString("0.00", CultureInfo.InvariantCulture)} THB", submitted.Message, StringComparison.Ordinal);
            Assert.Contains($"Total cost: {(expectedUnit * quantity).ToString("0.00", CultureInfo.InvariantCulture)} THB", submitted.Message, StringComparison.Ordinal);
            Assert.Contains($"Pricing policy: {PricingCatalog.AdditivePricingPolicyVersion}", submitted.Message, StringComparison.Ordinal);
            var lease = await scope.ServiceProvider.GetRequiredService<IInstantQuotationSubmissionStore>()
                .TryAcquireAsync(session.SubmissionId, upload.OwnerIdentity ?? Convert.ToHexStringLower(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"instant-quotation-anonymous-checkpoint-owner:{session.SessionId}"))), deadline.Token);
            Assert.NotNull(lease);
            custody.Own("submission-lease", lease.DisposeAsync);
            var checkpoint = Assert.IsType<InstantQuotationSubmissionCheckpoint>((await lease.ReadAsync(deadline.Token)).Checkpoint);
            Assert.Equal(417, checkpoint.RequestReference);
            Assert.Equal(InstantQuotationSubmissionCheckpointStatus.Persisted, checkpoint.Status);
            Assert.Equal(1, requests.Count);
            QuantityPhase("request-checkpoint-persisted", quantity);
        }
    }

    private const string QuantityCleanupFaultsKey = "FdmQuantityCleanupFaults";

    private sealed record QuantityCleanupFault(string Resource, Exception Error);

    private sealed class QuantityScenarioCustody
    {
        private readonly List<(string Resource, Func<ValueTask> Cleanup)> owned = [];

        public void Own(string resource, Func<ValueTask> cleanup) => owned.Add((resource, cleanup));

        public async Task<List<QuantityCleanupFault>> CleanupAsync()
        {
            var faults = new List<QuantityCleanupFault>();
            for (var index = owned.Count - 1; index >= 0; index--)
            {
                try
                {
                    // Await the original cleanup to settlement. Cancellation is not proof of exit.
                    await owned[index].Cleanup();
                }
                catch (Exception error)
                {
                    faults.Add(new QuantityCleanupFault(owned[index].Resource, error));
                }
            }

            return faults;
        }
    }

    private static async Task RunQuantityScenarioAsync(
        Func<QuantityScenarioCustody, Task> body, Action<IReadOnlyList<QuantityCleanupFault>> report)
    {
        var custody = new QuantityScenarioCustody();
        ExceptionDispatchInfo? primary = null;
        try
        {
            await body(custody);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }

        var faults = await custody.CleanupAsync();
        if (primary is not null)
        {
            try
            {
                // Keep the live list so a subsequent reporting failure is retained too.
                primary.SourceException.Data[QuantityCleanupFaultsKey] = faults;
            }
            catch (Exception error)
            {
                faults.Add(new QuantityCleanupFault("primary-fault-metadata", error));
            }
        }

        try
        {
            report(faults);
        }
        catch (Exception error)
        {
            faults.Add(new QuantityCleanupFault("cleanup-report", error));
        }

        if (primary is not null) primary.Throw();
        if (faults.Count != 0)
        {
            throw new AggregateException(
                "Quantity cleanup failed: " + string.Join(", ", faults.Select(fault => fault.Resource)),
                faults.Select(fault => fault.Error));
        }
    }

    private sealed class QuantityOwnedFixture
    {
        private readonly string root = Path.GetFullPath(Path.GetTempPath());
        private readonly string stem = $"maliev-quantity-margin-{Guid.NewGuid():N}";
        private bool creationAttempted;

        public string Create(Func<string, string> create)
        {
            if (creationAttempted) throw new InvalidOperationException("Fixture creation was already attempted.");
            creationAttempted = true;
            var path = Path.GetFullPath(create(stem));
            RequireOwnedPath(path);
            return path;
        }

        public ValueTask CleanupAsync()
        {
            // Same unique-prefix recovery as CreatePreliminaryPdfBoxStl, bounded to one file.
            // The namespace is owned before the existing writer chooses its final suffix.
            var candidates = Directory.EnumerateFiles(root, stem + "-*.stl", SearchOption.TopDirectoryOnly)
                .Take(2).ToArray();
            if (candidates.Length > 1)
            {
                throw new InvalidOperationException("Unexpected fixture custody: preserve multiple matching files.");
            }

            foreach (var candidate in candidates)
            {
                var path = Path.GetFullPath(candidate);
                RequireOwnedPath(path);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("Fixture custody changed; preserve the reparse point.");
                }

                File.Delete(path);
                if (File.Exists(path)) throw new IOException("The owned fixture remains after deletion.");
            }

            return ValueTask.CompletedTask;
        }

        private void RequireOwnedPath(string path)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var name = Path.GetFileName(path);
            var prefix = stem + "-";
            if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(root), comparison)
                || !name.StartsWith(prefix, StringComparison.Ordinal)
                || !name.EndsWith(".stl", StringComparison.Ordinal)
                || !Guid.TryParseExact(name[prefix.Length..^4], "N", out _))
            {
                throw new InvalidOperationException("The fixture path is outside the exact owned namespace.");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuantityCustody_PrimaryIdentityAndStackSurviveDeleteDisposeAndReportFaults(bool cancellation)
    {
        Exception marker = cancellation ? new OperationCanceledException("synthetic cancellation")
            : new InvalidOperationException("synthetic body failure");
        var disposeFailure = new IOException("synthetic disposal failure");
        var deleteFailure = new IOException("synthetic deletion failure");
        var reportFailure = new IOException("synthetic reporting failure");
        string? originalStack = null;
        var actual = await Record.ExceptionAsync(() => RunQuantityScenarioAsync(async custody =>
        {
            custody.Own("file-delete", () => throw deleteFailure);
            custody.Own("page-dispose", () => throw disposeFailure);
            await Task.Yield();
            try { throw marker; }
            catch (Exception error) { originalStack = error.StackTrace; throw; }
        }, _ => throw reportFailure));

        Assert.Same(marker, actual);
        Assert.StartsWith(originalStack ?? throw new InvalidOperationException("Missing original stack."),
            actual?.StackTrace ?? throw new InvalidOperationException("Missing preserved stack."));
        var faults = Assert.IsType<List<QuantityCleanupFault>>(marker.Data[QuantityCleanupFaultsKey]);
        Assert.Collection(faults,
            fault => Assert.Same(disposeFailure, fault.Error),
            fault => Assert.Same(deleteFailure, fault.Error),
            fault => Assert.Same(reportFailure, fault.Error));
    }

    [Fact]
    public async Task QuantityCustody_CleanupFaultWithoutPrimaryFails()
    {
        var marker = new IOException("synthetic cleanup failure");
        var actual = await Assert.ThrowsAsync<AggregateException>(() => RunQuantityScenarioAsync(custody =>
        {
            custody.Own("file-delete", () => throw marker);
            return Task.CompletedTask;
        }, _ => { }));
        Assert.Same(marker, Assert.Single(actual.InnerExceptions));
    }

    [Fact]
    public async Task QuantityCustody_StartupFailureCleansPreviouslyAcquiredOwnersInReverseOrder()
    {
        var order = new List<string>();
        var marker = new InvalidOperationException("synthetic driver acquisition failure");
        var actual = await Record.ExceptionAsync(() => RunQuantityScenarioAsync(custody =>
        {
            foreach (var resource in new[] { "file", "factory", "client" })
            {
                custody.Own(resource, () => { order.Add(resource); return ValueTask.CompletedTask; });
            }

            throw marker;
        }, _ => order.Add("report")));
        Assert.Same(marker, actual);
        Assert.Equal(new[] { "client", "factory", "file", "report" }, order);
    }

    [Fact]
    public async Task QuantityCustody_PartialFileCreationFailureRecoversOnlyItsOwnedPath()
    {
        var fixture = new QuantityOwnedFixture();
        var marker = new IOException("synthetic partial write failure");
        string? partialPath = null;
        await RunQuantityScenarioAsync(async recovery =>
        {
            recovery.Own("fault-control-file-recovery", fixture.CleanupAsync);
            var actual = await Record.ExceptionAsync(() => RunQuantityScenarioAsync(custody =>
            {
                custody.Own("fixture-file", fixture.CleanupAsync);
                fixture.Create(stem =>
                {
                    partialPath = Path.Combine(Path.GetTempPath(), $"{stem}-{Guid.NewGuid():N}.stl");
                    File.WriteAllText(partialPath, "synthetic partial fixture");
                    throw marker;
                });
                return Task.CompletedTask;
            }, _ => { }));
            Assert.Same(marker, actual);
            Assert.NotNull(partialPath);
            Assert.False(File.Exists(partialPath));
        }, _ => { });
    }

    [Fact]
    public async Task QuantityCustody_DoesNotReportSettlementBeforeOriginalCleanupCompletes()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = false;
        var marker = new InvalidOperationException("synthetic body failure");
        var pending = RunQuantityScenarioAsync(custody =>
        {
            custody.Own("held-worker", async () => { entered.SetResult(true); await release.Task; });
            throw marker;
        }, _ => reported = true);
        ExceptionDispatchInfo? controlPrimary = null;
        try
        {
            await entered.Task;
            Assert.False(pending.IsCompleted);
            Assert.False(reported);
        }
        catch (Exception error)
        {
            controlPrimary = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            release.TrySetResult(true);
        }

        var actual = await Record.ExceptionAsync(() => pending);
        if (controlPrimary is not null) controlPrimary.Throw();
        Assert.Same(marker, actual);
        Assert.True(reported);
    }

    private sealed class QuantityCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(1, "Thailand", "Asia", "TH", "TH", "THA", null, null)], true));
    }

    private sealed class QuantityRequestClient : IQuotationClient
    {
        public TaskCompletionSource<QuotationRequestSubmission> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count { get; private set; }
        public Task<QuotationRequestResult> CreateRequestAsync(QuotationRequestSubmission submission, string idempotencyKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            Created.TrySetResult(submission);
            return Task.FromResult(new QuotationRequestResult(417, true, true, "request-417", submission.JourneyId));
        }
    }
}
