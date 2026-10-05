using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual circuit/DOM summary transitions over historical synthetic input, not FileService admission.</summary>
public sealed class InstantQuotationSummaryTransitionBrowserTests
{
    [Theory]
    [InlineData("en", 375)]
    [InlineData("th", 320)]
    [InlineData("en", 1280)]
    [InlineData("th", 1280)]
    public async Task ActualSummaryDisclosureKeepsLegalChoicesAndKeyboardBreakdownOperable(string culture, int width)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width);
        await fixture.AssertFinalAsync(2);
        var dock = fixture.Summary;
        var disclosure = dock.Locator("summary");
        var legal = fixture.Page.Locator("[data-summary-legal]");
        var consultation = fixture.Page.Locator("[data-summary-consultation]");
        // Shared fixture opens the dock for pricing assertions; close it through its real keyboard control.
        Assert.True(await dock.EvaluateAsync<bool>("element => element.open"));
        await disclosure.FocusAsync();
        await fixture.Page.Keyboard.PressAsync("Space");
        Assert.False(await dock.EvaluateAsync<bool>("element => element.open"));
        await Assertions.Expect(dock.Locator("[data-summary-show]")).ToBeVisibleAsync();
        await Assertions.Expect(dock.Locator("[data-summary-hide]")).ToBeHiddenAsync();
        await Assertions.Expect(disclosure.Locator("[data-workflow-lead-time]")).ToBeVisibleAsync();
        await Assertions.Expect(legal).ToBeVisibleAsync();
        Assert.True(await legal.EvaluateAsync<bool>("element => !element.closest('details')"));
        await Assertions.Expect(consultation).ToBeVisibleAsync();
        Assert.Equal("/contact#contact-us", await consultation.GetAttributeAsync("href"));
        Assert.Equal("_blank", await consultation.GetAttributeAsync("target"));
        Assert.Contains("noopener", await consultation.GetAttributeAsync("rel"), StringComparison.Ordinal);

        await disclosure.FocusAsync();
        await fixture.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(dock).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(dock.Locator("[data-summary-part-details]")).ToBeVisibleAsync();
        await Assertions.Expect(dock.Locator("[data-summary-hide]")).ToBeVisibleAsync();
        await disclosure.FocusAsync();
        await fixture.Page.Keyboard.PressAsync("Tab");
        Assert.True(await fixture.Breakdown.EvaluateAsync<bool>("element => document.activeElement === element"),
            "The actual summary breakdown must be reachable from its disclosure by keyboard.");
        Assert.Equal(culture == "th" ? "สรุปชิ้นงานและราคา" : "Part and price summary",
            await fixture.Breakdown.GetAttributeAsync("aria-label"));
        await disclosure.FocusAsync();
        await fixture.Page.Keyboard.PressAsync("Space");
        Assert.False(await dock.EvaluateAsync<bool>("element => element.open"));
        await Assertions.Expect(legal).ToBeVisibleAsync();
        await Assertions.Expect(disclosure.Locator("[data-workflow-lead-time]")).ToBeVisibleAsync();
        Assert.True(await fixture.Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        await fixture.CaptureAsync("keyboard-disclosure");
        await fixture.AssertHealthAsync();
    }

    [Theory]
    [InlineData("en", 375)]
    [InlineData("th", 320)]
    public async Task AcceptedRepriceHidesPriorSummaryAndDurationUntilCurrentFinalQuote(string culture, int width)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width);
        await fixture.AssertFinalAsync(2);
        fixture.Control.Armed = true;
        try
        {
            await fixture.Quantity.FillAsync("4");
            await fixture.Quantity.PressAsync("Tab");
            await fixture.Control.Pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.AssertBusyAsync();
            await fixture.CaptureAsync("pending");
            fixture.Control.Pending.Release.TrySetResult();
            await fixture.Control.Completed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.AssertBusyAsync(); // A Completed display frame is not an order quote.
            await fixture.CaptureAsync("completed-frame");
        }
        finally { fixture.Control.ReleaseAll(); }
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        await fixture.AssertFinalAsync(4);
        await fixture.CaptureAsync("final");
        await fixture.AssertHealthAsync();
    }

    [Theory]
    [InlineData("en", 375)]
    [InlineData("th", 320)]
    public async Task ActualRecalculationSpinnerHonorsReducedMotionWithoutHidingStatus(string culture, int width)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width, ReducedMotion.Reduce);
        fixture.Control.Armed = true;
        try
        {
            await fixture.Quantity.FillAsync("4");
            await fixture.Quantity.PressAsync("Tab");
            await fixture.Control.Pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.AssertBusyAsync();
            var spinner = fixture.Page.Locator("[data-pricing-loading-status] .instant-quote__pricing-spinner");
            await Assertions.Expect(spinner).ToBeVisibleAsync();
            Assert.Equal("none", await spinner.EvaluateAsync<string>("element => getComputedStyle(element).animationName"));
            await fixture.CaptureAsync("reduced-motion");
        }
        finally { fixture.Control.ReleaseAll(); }
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        await fixture.AssertFinalAsync(4);
        await fixture.AssertHealthAsync();
    }

    [Theory]
    [InlineData("en", 375)]
    [InlineData("th", 320)]
    public async Task InputOnlyDraftDoesNotPresentUnqualifiedPriorMoneyAndTimeAsCurrent(string culture, int width)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width);
        await fixture.AssertFinalAsync(2);
        var before = (await fixture.StoredAsync()).QuoteAuthorization!;
        await fixture.Quantity.FillAsync("4"); // Real input, deliberately no blur/accepted mutation.
        await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("4");
        await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
        var after = await fixture.StoredAsync();
        Assert.Equal(2, Assert.Single(after.Parts).Configuration.Quantity);
        Assert.True(after.QuoteAuthorization is not null
            && string.Equals(before.OrderTicket, after.QuoteAuthorization.OrderTicket, StringComparison.Ordinal)
            && before.LineTickets.SequenceEqual(after.QuoteAuthorization.LineTickets, StringComparer.Ordinal),
            "Input-only must retain the existing accepted authorization; values are redacted.");

        // Accept either hidden unqualified prior results or an observable explicit prior-quantity distinction.
        var priorStatus = fixture.Page.GetByRole(AriaRole.Status).Filter(new()
        {
            HasTextRegex = culture == "th"
                ? new Regex("(ก่อนหน้า|ที่ยอมรับ|ยืนยันแล้ว).*2|2.*(ก่อนหน้า|ที่ยอมรับ|ยืนยันแล้ว)")
                : new Regex("(previously accepted|accepted quantity|previous quote).*2|2.*(previously accepted|accepted quantity|previous quote)", RegexOptions.IgnoreCase),
        });
        var distinguished = await priorStatus.CountAsync() > 0 && await priorStatus.First.IsVisibleAsync();
        var summaryUnqualified = (await fixture.Summary.InnerTextAsync()).Contains("1,391.00", StringComparison.Ordinal);
        var durationUnqualified = await fixture.Duration.CountAsync() > 0 && await fixture.Duration.IsVisibleAsync();
        var partUnqualified = (await fixture.PartPrice.InnerTextAsync()).Contains("600.00", StringComparison.Ordinal)
            || (await fixture.PartPrice.InnerTextAsync()).Contains("1,200.00", StringComparison.Ordinal);
        await fixture.CaptureAsync("input-only");
        await fixture.AssertHealthAsync();
        Assert.True(distinguished || (!summaryUnqualified && !durationUnqualified && !partUnqualified),
            "Visible draft quantity4 must not present prior quantity2 money/time as unqualified current results.");
        await Assertions.Expect(fixture.Duration).ToHaveCountAsync(0);
        await Assertions.Expect(fixture.Page.Locator("[data-workflow-bulk-pricing], [data-workflow-bulk-savings], [data-technical-filament-minimum], [data-workflow-price-unavailable]")).ToHaveCountAsync(0);
        await Assertions.Expect(fixture.Breakdown.Locator("dd").Filter(new() { HasTextRegex = new Regex("฿") })).ToHaveCountAsync(0);
        await Assertions.Expect(fixture.PartPrice.Locator("dd").Filter(new() { HasTextRegex = new Regex("฿") })).ToHaveCountAsync(0);
        await Assertions.Expect(fixture.Page.Locator("[data-workflow-lead-time] strong")).ToHaveTextAsync("—");
    }

    private sealed class Pause
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Control
    {
        public bool Armed;
        public InstantQuotationSessionState? Initial;
        public InstantQuotationSessionState? Captured;
        public InstantQuotationOrderQuote? Final;
        public Pause Pending { get; } = new();
        public Pause Completed { get; } = new();
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseAll() { Pending.Release.TrySetResult(); Completed.Release.TrySetResult(); }
    }

    private sealed class Boundary(IInstantQuotationAuthoritativePricingService inner,
        IInstantQuotationSessionStore store, Control control) : IInstantQuotationAuthoritativePricingService
    {
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, CancellationToken token) => inner.QuoteAsync(session, owner, comparisons, token);

        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken token)
        {
            var gated = control.Armed && session.Parts.Single().Configuration.Quantity == 4;
            if (gated)
            {
                var initial = control.Initial!;
                var expected = Assert.Single(initial.Parts);
                var part = Assert.Single(session.Parts);
                Assert.True(session.SessionId == initial.SessionId && session.SubmissionId == initial.SubmissionId
                    && session.OwnerIdentity == initial.OwnerIdentity && owner == initial.OwnerIdentity
                    && part.PartId == expected.PartId && part.UploadReference == expected.UploadReference
                    && part.PhysicalAnalysisUpload?.FileId == expected.PhysicalAnalysisUpload?.FileId
                    && part.Geometry.Sha256 == expected.Geometry.Sha256
                    && part.Configuration.MaterialKey == "M68" && part.Configuration.Color == "White"
                    && part.Configuration.BuildPreference == expected.Configuration.BuildPreference,
                    "Only the captured owned quantity4 request may pause; private tuple values are redacted.");
                var persisted = await store.GetAsync(session.SessionId, owner, token);
                Assert.True(persisted is not null && persisted.UpdatedAt == session.UpdatedAt
                    && Assert.Single(persisted.Parts).Configuration == part.Configuration,
                    "The paused request must match its persisted revision/configuration.");
                control.Captured = session;
            }
            try
            {
                var result = await inner.QuoteAsync(session, owner, comparisons, async (frame, callbackToken) =>
                {
                    await observer(frame, callbackToken);
                    if (!gated || frame.PartId != session.Parts.Single().PartId || frame.MaterialKey != "M68") return;
                    var pause = frame.Status == InstantQuotationMaterialPricingStatus.Pending ? control.Pending
                        : frame.Status == InstantQuotationMaterialPricingStatus.Completed ? control.Completed : null;
                    if (pause is null) return;
                    pause.Entered.TrySetResult();
                    await pause.Release.Task.WaitAsync(callbackToken);
                }, token);
                control.Final = result;
                return result;
            }
            finally { if (gated) control.Finished.TrySetResult(); }
        }
    }

    private sealed class Host(Control control) : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                var original = services.Single(item => item.ServiceType == typeof(IInstantQuotationAuthoritativePricingService));
                services.RemoveAll<IInstantQuotationAuthoritativePricingService>();
                services.AddScoped<IInstantQuotationAuthoritativePricingService>(provider =>
                    new Boundary((IInstantQuotationAuthoritativePricingService)ActivatorUtilities.CreateInstance(provider,
                        original.ImplementationType!), provider.GetRequiredService<IInstantQuotationSessionStore>(), control));
            });
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Control Control { get; } = new();
        private Host Factory { get; }
        private IPlaywright? playwright;
        private IBrowser? browser;
        private IBrowserContext? context;
        public IPage Page { get; private set; } = null!;
        private string sessionId = "";
        private string culture = "";
        private int pageErrors;
        private int consoleErrors;
        public ILocator Quantity => Page.Locator("input[name='quantity']");
        public ILocator Review => Page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        public ILocator Summary => Page.Locator("[data-workflow-summary-dock]");
        public ILocator Breakdown => Page.Locator("[data-workflow-order-summary]");
        public ILocator Duration => Page.Locator("[data-workflow-selected-print-time]");
        public ILocator PartPrice => Page.Locator("[data-workflow-part-price]");
        private Fixture() => Factory = new(Control);

        public static async Task<Fixture> CreateAsync(string culture, int width, ReducedMotion motion = ReducedMotion.NoPreference)
        {
            var fixture = new Fixture { culture = culture };
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                var origin = new Uri($"http://127.0.0.1:{port}");
                fixture.Factory.UseKestrel(port);
                using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
                fixture.playwright = await Playwright.CreateAsync();
                fixture.browser = await fixture.playwright.Chromium.LaunchAsync(new() { Headless = true });
                fixture.context = await fixture.browser.NewContextAsync(new()
                { ViewportSize = new() { Width = width, Height = 800 }, ColorScheme = ColorScheme.Light, ReducedMotion = motion });
                fixture.Page = await fixture.context.NewPageAsync();
                fixture.Page.PageError += (_, _) => Interlocked.Increment(ref fixture.pageErrors);
                fixture.Page.Console += (_, message) => { if (message.Type == "error") Interlocked.Increment(ref fixture.consoleErrors); };
                var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
                await fixture.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
                var consent = fixture.Page.Locator("#cookieConsent [data-consent-action='reject']");
                if (await consent.IsVisibleAsync()) await consent.ClickAsync();
                var cookie = Assert.Single(await fixture.context.CookiesAsync(), candidate => candidate.Name == InstantQuotationSessionIdentityCookie.CookieName);
                var http = new DefaultHttpContext(); http.Request.Headers.Cookie = $"{cookie.Name}={cookie.Value}";
                fixture.sessionId = fixture.Factory.Services.GetRequiredService<InstantQuotationSessionIdentityCookie>().TryRead(http)!;
                Assert.True(!string.IsNullOrEmpty(fixture.sessionId), "Normal runtime cookie must resolve; cookie values are redacted.");
                var store = fixture.Factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
                var stored = (await fixture.StoredAsync());
                var digest = new string('a', 64);
                var reference = new InstantQuotationUploadReference(Guid.NewGuid().ToString("D"));
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("synthetic-historical-summary", reference, digest),
                    new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(), 12, 1, true, false, false, 1))!;
                var part = new InstantQuotationPart(Guid.NewGuid(), "historical-summary.stl", reference, geometry, new("M68", "White", 2));
                Assert.True(await store.PutAsync(stored with { RequestState = new([part]) }, null, default));
                await fixture.Page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
                await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
                fixture.Control.Initial = await fixture.StoredAsync();
                await fixture.Summary.Locator(":scope > summary").ClickAsync();
                await Assertions.Expect(fixture.Breakdown).ToBeVisibleAsync();
                Assert.Equal(url, fixture.Page.Url);
                Assert.False(string.IsNullOrWhiteSpace(await fixture.Page.TitleAsync()));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async Task<InstantQuotationSessionState> StoredAsync() =>
            (await Factory.Services.GetRequiredService<IInstantQuotationSessionStore>().GetAsync(sessionId, null, default))!;

        private ILocator Row(string english, string thai) => Breakdown.Locator(":scope > div")
            .Filter(new() { Has = Page.Locator("dt", new() { HasTextRegex = new Regex("^" + Regex.Escape(culture == "th" ? thai : english) + "$") }) }).Locator("dd");

        private ILocator PartRow(string english, string thai) => PartPrice.Locator(":scope > div")
            .Filter(new() { Has = Page.Locator("dt", new() { HasTextRegex = new Regex("^" + Regex.Escape(culture == "th" ? thai : english) + "$") }) }).Locator("dd");

        public async Task AssertFinalAsync(int quantity)
        {
            var unit = quantity == 2 ? 600d : 520d;
            var subtotal = quantity == 2 ? 1200d : 2080d;
            var final = quantity == 2 ? 1391d : 2332.60d;
            var quote = Assert.IsType<InstantQuotationOrderQuote>(Control.Final);
            var part = Assert.Single(quote.Parts);
            Assert.Equal(unit, part.UnitPrice); Assert.Equal(subtotal, part.Subtotal);
            Assert.Equal(39.02d, part.PrintTimeMinutesPerUnit, 8);
            Assert.Equal(subtotal, quote.ItemsSubtotal); Assert.Equal(0, quote.MinimumOrderSurcharge);
            Assert.Equal(100, quote.ShippingCost); Assert.Equal(quantity == 2 ? 91d : 152.60d, quote.Vat);
            Assert.Equal(final, quote.FinalOrderPrice);
            await Assertions.Expect(Breakdown).ToHaveAttributeAsync("aria-busy", "false");
            await Assertions.Expect(Page.Locator("[data-pricing-loading-status]")).ToHaveCountAsync(0);
            await Assertions.Expect(Duration).ToBeVisibleAsync();
            await Assertions.Expect(Duration.Locator("dd")).ToHaveTextAsync(culture == "th" ? "39 นาที" : "39 minutes");
            await Assertions.Expect(PartRow("Unit price", "ราคาต่อชิ้น")).ToHaveTextAsync(quantity == 2 ? "฿600.00" : "฿520.00");
            await Assertions.Expect(PartRow("Subtotal", "ยอดรวมย่อย")).ToHaveTextAsync(quantity == 2 ? "฿1,200.00" : "฿2,080.00");
            await Assertions.Expect(Row("Subtotal", "ยอดรวมย่อย")).ToHaveTextAsync(quantity == 2 ? "฿1,200.00" : "฿2,080.00");
            await Assertions.Expect(Row("Shipping", "ค่าจัดส่ง")).ToHaveTextAsync("฿100.00");
            await Assertions.Expect(Row("VAT", "ภาษีมูลค่าเพิ่ม")).ToHaveTextAsync(quantity == 2 ? "฿91.00" : "฿152.60");
            await Assertions.Expect(Row("Total", "รวมทั้งหมด")).ToHaveTextAsync(quantity == 2 ? "฿1,391.00" : "฿2,332.60");
            await Assertions.Expect(Page.Locator("[data-workflow-minimum-order-surcharge]")).ToHaveCountAsync(0);
            await Assertions.Expect(Page.Locator("[data-workflow-lead-time] strong")).ToHaveTextAsync(culture == "th" ? "1–3 วันทำการ" : "1–3 business days");
            var stored = await StoredAsync(); Assert.Equal(quantity, Assert.Single(stored.Parts).Configuration.Quantity);
            Assert.True(stored.QuoteAuthorization is not null && Factory.Services.GetRequiredService<IInstantQuotationQuoteTicketService>()
                .Validate(stored, quote, stored.QuoteAuthorization, DateTimeOffset.UtcNow), "Final authorization must match accepted quantity; ticket values are redacted.");
        }

        public async Task AssertBusyAsync()
        {
            await Assertions.Expect(Breakdown).ToHaveAttributeAsync("aria-busy", "true");
            var status = Page.Locator("[data-pricing-loading-status]");
            await Assertions.Expect(status).ToBeVisibleAsync(); await Assertions.Expect(status).ToHaveAttributeAsync("role", "status");
            await Assertions.Expect(status).ToContainTextAsync(culture == "th" ? "กำลังอัปเดตราคาและระยะเวลาผลิต…" : "Updating price and lead time…");
            await Assertions.Expect(Duration).ToHaveCountAsync(0);
            await Assertions.Expect(Row("Subtotal", "ยอดรวมย่อย")).ToHaveTextAsync("—");
            await Assertions.Expect(Row("Total", "รวมทั้งหมด")).ToHaveTextAsync("—");
            await Assertions.Expect(Page.Locator("[data-workflow-lead-time] strong")).ToHaveTextAsync("—");
            await Assertions.Expect(Review).ToBeDisabledAsync();
            var stored = await StoredAsync(); Assert.Null(stored.QuoteAuthorization);
            Assert.True(Control.Captured is not null && stored.UpdatedAt == Control.Captured.UpdatedAt,
                "Paused callbacks must retain the captured persisted revision.");
        }

        public async Task AssertHealthAsync()
        {
            Assert.Equal(0, pageErrors); Assert.Equal(0, consoleErrors);
            await Assertions.Expect(Page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
            await Assertions.Expect(Summary).ToBeVisibleAsync();
        }

        public async Task CaptureAsync(string stage)
        {
            var directory = Path.GetFullPath(Path.Combine("TestResults", "summary-transition-browser"));
            Directory.CreateDirectory(directory);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{culture}-{stage}-{Guid.NewGuid():N}.png"), FullPage = true });
        }

        public async ValueTask DisposeAsync()
        {
            Control.ReleaseAll();
            if (Control.Captured is not null) await Control.Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (context is not null) await context.DisposeAsync();
            if (browser is not null) await browser.DisposeAsync();
            playwright?.Dispose(); await Factory.DisposeAsync();
        }
    }
}
