using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real DOM/circuit events over controlled historical protected input; not FileService or authentication acceptance.</summary>
public sealed class MaterialPricingDisplayBrowserTests
{
    [Theory]
    [InlineData("en", 375, ColorScheme.Light, InstantQuotationMaterialPricingStatus.Pending, "Calculating price…")]
    [InlineData("th", 320, ColorScheme.Dark, InstantQuotationMaterialPricingStatus.Completed, "600.00")]
    public async Task ActualQuantityEventShowsCurrentProgressBeforeFinalAuthority(string culture, int width,
        ColorScheme scheme, InstantQuotationMaterialPricingStatus status, string expected)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width, scheme);
        var gate = fixture.Control.Arm(2, status);
        try
        {
            await fixture.ChangeQuantityAsync(2);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Page.Locator("[data-workflow-material-comparison] > summary").ClickAsync();
            var row = fixture.Page.Locator("[data-workflow-material-price='M68']");
            await Assertions.Expect(row).ToBeVisibleAsync();
            await Assertions.Expect(row).ToContainTextAsync(expected);
            Assert.DoesNotContain("฿0", await row.InnerTextAsync(), StringComparison.Ordinal);
            if (status == InstantQuotationMaterialPricingStatus.Pending)
                Assert.Equal("status", await row.Locator("strong").GetAttributeAsync("role"));
            await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
            Assert.Null((await fixture.StoredAsync()).QuoteAuthorization);
            await fixture.CaptureAsync($"{culture}-{width}-{status}");
        }
        finally { gate.Release.TrySetResult(); }
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        var stored = await fixture.StoredAsync();
        Assert.Equal(2, Assert.Single(stored.Parts).Configuration.Quantity);
        var final = Assert.IsType<InstantQuotationOrderQuote>(fixture.Control.FinalQuote);
        Assert.Equal(600d, Assert.Single(final.Parts).UnitPrice);
        Assert.Equal(1200d, Assert.Single(final.Parts).Subtotal);
        Assert.Equal(1391d, final.FinalOrderPrice);
        Assert.NotNull(stored.QuoteAuthorization);
        Assert.True(fixture.Factory.Services.GetRequiredService<IInstantQuotationQuoteTicketService>()
            .Validate(stored, final, stored.QuoteAuthorization, DateTimeOffset.UtcNow));
        await fixture.CaptureAsync($"{culture}-{width}-final");
    }

    [Theory]
    [InlineData("en", 375, ColorScheme.Light)]
    [InlineData("th", 320, ColorScheme.Dark)]
    public async Task InputOnlyCannotShowOldNumbersOrEnableReviewForDifferentVisibleQuantity(string culture, int width, ColorScheme scheme)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width, scheme);
        var before = await fixture.StoredAsync();
        Assert.NotNull(before.QuoteAuthorization);
        await fixture.Quantity.FillAsync("3"); // actual input event; deliberately no blur/change
        await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("3");
        await Assertions.Expect(fixture.Page.Locator("[data-workflow-material-price] strong")).ToHaveCountAsync(0);
        Assert.Equal(1, Assert.Single((await fixture.StoredAsync()).Parts).Configuration.Quantity);
        await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
        var pending = (await fixture.StoredAsync()).QuoteAuthorization;
        Assert.True(pending is not null
            && string.Equals(before.QuoteAuthorization.OrderTicket, pending.OrderTicket, StringComparison.Ordinal)
            && before.QuoteAuthorization.LineTickets.SequenceEqual(pending.LineTickets, StringComparer.Ordinal),
            "Input-only presentation must retain the existing protected authorization.");
        await fixture.Quantity.PressAsync("Tab");
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        var accepted = await fixture.StoredAsync();
        Assert.Equal(3, Assert.Single(accepted.Parts).Configuration.Quantity);
        Assert.NotNull(accepted.QuoteAuthorization);
        Assert.False(string.Equals(before.QuoteAuthorization.OrderTicket, accepted.QuoteAuthorization.OrderTicket, StringComparison.Ordinal),
            "Accepted repricing must produce authorization bound to the accepted configuration.");
        var quote = Assert.IsType<InstantQuotationOrderQuote>(fixture.Control.FinalQuote);
        Assert.True(fixture.Factory.Services.GetRequiredService<IInstantQuotationQuoteTicketService>()
            .Validate(accepted, quote, accepted.QuoteAuthorization, DateTimeOffset.UtcNow));
        await fixture.Review.ClickAsync();
        await Assertions.Expect(fixture.Page.Locator("[data-workflow-review-part]")).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData("en", 375, ColorScheme.Light)]
    [InlineData("th", 320, ColorScheme.Dark)]
    public async Task QueuedQuantityThreeThenFourDoesNotRelabelOlderActualFrames(string culture, int width, ColorScheme scheme)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width, scheme);
        var two = fixture.Control.Arm(2, InstantQuotationMaterialPricingStatus.Completed);
        var three = fixture.Control.Arm(3, InstantQuotationMaterialPricingStatus.Completed);
        var four = fixture.Control.Arm(4, InstantQuotationMaterialPricingStatus.Completed);
        try
        {
            await fixture.ChangeQuantityAsync(2);
            await two.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.ChangeQuantityAsync(3);
            await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("3");
            await Assertions.Expect(fixture.Page.Locator("[data-workflow-material-price] strong")).ToHaveCountAsync(0);
            Assert.False(three.Entered.Task.IsCompleted, "A queued configuration cannot bypass the active state gate.");
            two.Release.TrySetResult();
            await three.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(3, Assert.Single((await fixture.StoredAsync()).Parts).Configuration.Quantity);
            await fixture.ChangeQuantityAsync(4);
            await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("4");
            await Assertions.Expect(fixture.Page.Locator("[data-workflow-material-price] strong")).ToHaveCountAsync(0);
            Assert.False(four.Entered.Task.IsCompleted);
            three.Release.TrySetResult();
            await four.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(4, Assert.Single((await fixture.StoredAsync()).Parts).Configuration.Quantity);
            await fixture.Page.Locator("[data-workflow-material-comparison] > summary").ClickAsync();
            await Assertions.Expect(fixture.Page.Locator("[data-workflow-material-price='M68']")).ToBeVisibleAsync();
            // This is a current-tuple/DOM routing control, not another commercial arithmetic golden.
            await Assertions.Expect(fixture.Page.Locator("[data-workflow-material-price='M68'] strong"))
                .ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^฿[0-9,.]+ (per piece|ต่อชิ้น)$"));
            await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
        }
        finally
        {
            await fixture.CaptureDiagnosticsAsync($"queue-{culture}");
            two.Release.TrySetResult(); three.Release.TrySetResult(); four.Release.TrySetResult();
        }
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        Assert.Equal(4, Assert.Single((await fixture.StoredAsync()).Parts).Configuration.Quantity);
        Assert.Equal(new[] { 2, 3, 4 }, fixture.Control.AcceptedQuantities.ToArray());
    }

    [Theory]
    [InlineData("en", 375, ColorScheme.Light)]
    [InlineData("th", 320, ColorScheme.Dark)]
    public async Task OlderAcceptedCompletionCannotEraseNewerInputOnlyDraft(string culture, int width, ColorScheme scheme)
    {
        await using var fixture = await Fixture.CreateAsync(culture, width, scheme);
        var three = fixture.Control.Arm(3, InstantQuotationMaterialPricingStatus.Completed);
        var four = fixture.Control.Arm(4, InstantQuotationMaterialPricingStatus.Completed);
        try
        {
            await fixture.ChangeQuantityAsync(3);
            await three.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Quantity.FillAsync("4"); // actual newer input; no acceptance/blur yet
            await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("4");
            await Assertions.Expect(fixture.Page.Locator("[data-workflow-material-price] strong")).ToHaveCountAsync(0);
            three.Release.TrySetResult();
            await three.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("4");
            await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
            var prior = await fixture.StoredAsync();
            Assert.Equal(3, Assert.Single(prior.Parts).Configuration.Quantity);
            Assert.NotNull(prior.QuoteAuthorization);
            Assert.False(four.Entered.Task.IsCompleted, "Input alone cannot submit the new configuration.");
            await fixture.Quantity.PressAsync("Tab");
            await four.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(4, Assert.Single((await fixture.StoredAsync()).Parts).Configuration.Quantity);
            await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
        }
        finally { three.Release.TrySetResult(); four.Release.TrySetResult(); }
        await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("4");
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        var accepted = await fixture.StoredAsync();
        Assert.NotNull(accepted.QuoteAuthorization);
        Assert.True(fixture.Factory.Services.GetRequiredService<IInstantQuotationQuoteTicketService>()
            .Validate(accepted, Assert.IsType<InstantQuotationOrderQuote>(fixture.Control.FinalQuote),
                accepted.QuoteAuthorization, DateTimeOffset.UtcNow));
    }

    private sealed class Gate(int quantity, InstantQuotationMaterialPricingStatus status)
    {
        public int Quantity { get; } = quantity;
        public InstantQuotationMaterialPricingStatus Status { get; } = status;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Claimed;
    }

    private sealed class Control
    {
        public ConcurrentQueue<Gate> Gates { get; } = new();
        public ConcurrentQueue<int> AcceptedQuantities { get; } = new();
        public ConcurrentQueue<(string Stage, int Quantity, bool Canceled)> Timeline { get; } = new();
        public InstantQuotationOrderQuote? FinalQuote { get; set; }
        public Gate Arm(int quantity, InstantQuotationMaterialPricingStatus status)
        { var gate = new Gate(quantity, status); Gates.Enqueue(gate); return gate; }
        public void ReleaseAll() { foreach (var gate in Gates) gate.Release.TrySetResult(); }
    }

    private sealed class Boundary(IInstantQuotationAuthoritativePricingService inner, Control control)
        : IInstantQuotationAuthoritativePricingService
    {
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, CancellationToken token) => inner.QuoteAsync(session, owner, comparisons, token);
        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer, CancellationToken token)
        {
            var quantity = session.Parts.Single().Configuration.Quantity;
            control.Timeline.Enqueue(("attempt", quantity, token.IsCancellationRequested));
            var gate = control.Gates.FirstOrDefault(candidate => candidate.Quantity == quantity && candidate.Claimed == 0);
            if (gate is not null) control.AcceptedQuantities.Enqueue(quantity);
            try
            {
                var result = await inner.QuoteAsync(session, owner, comparisons, async (frame, callbackToken) =>
                {
                    await observer(frame, callbackToken);
                    if (gate is not null && frame.MaterialKey == "M68" && frame.Status == gate.Status
                        && Interlocked.Exchange(ref gate.Claimed, 1) == 0)
                    {
                        gate.Entered.TrySetResult();
                        control.Timeline.Enqueue(("held", quantity, callbackToken.IsCancellationRequested));
                        await gate.Release.Task.WaitAsync(callbackToken);
                        control.Timeline.Enqueue(("released", quantity, callbackToken.IsCancellationRequested));
                    }
                }, token);
                control.FinalQuote = result;
                return result;
            }
            finally
            {
                control.Timeline.Enqueue(("finished", quantity, token.IsCancellationRequested));
                gate?.Completed.TrySetResult();
            }
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
                        original.ImplementationType!), control));
            });
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Control Control { get; } = new();
        public Host Factory { get; }
        private IPlaywright? playwright;
        private IBrowser? browser;
        private IBrowserContext? context;
        public IPage Page { get; private set; } = null!;
        private string sessionId = "";
        private int pageErrorCount;
        public ILocator Quantity => Page.Locator("input[name='quantity']");
        public ILocator Review => Page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        public Fixture() => Factory = new(Control);

        public static async Task<Fixture> CreateAsync(string culture, int width, ColorScheme scheme)
        {
            var fixture = new Fixture();
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                var origin = new Uri($"http://127.0.0.1:{port}");
                fixture.Factory.UseKestrel(port);
                using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
                fixture.playwright = await Playwright.CreateAsync();
                fixture.browser = await fixture.playwright.Chromium.LaunchAsync(new() { Headless = true });
                fixture.context = await fixture.browser.NewContextAsync(new()
                { ViewportSize = new() { Width = width, Height = 800 }, ColorScheme = scheme });
                fixture.Page = await fixture.context.NewPageAsync();
                fixture.Page.PageError += (_, _) => Interlocked.Increment(ref fixture.pageErrorCount);
                var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
                await fixture.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
                var consent = fixture.Page.Locator("#cookieConsent [data-consent-action='reject']");
                if (await consent.IsVisibleAsync()) await consent.ClickAsync();
                var cookie = Assert.Single(await fixture.context.CookiesAsync(), candidate => candidate.Name == InstantQuotationSessionIdentityCookie.CookieName);
                var http = new DefaultHttpContext();
                http.Request.Headers.Cookie = $"{cookie.Name}={cookie.Value}";
                fixture.sessionId = fixture.Factory.Services.GetRequiredService<InstantQuotationSessionIdentityCookie>().TryRead(http)!;
                Assert.True(!string.IsNullOrEmpty(fixture.sessionId), "The normal anonymous runtime cookie must resolve its protected session.");
                var store = fixture.Factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
                var stored = (await store.GetAsync(fixture.sessionId, null, default))!;
                var digest = new string('a', 64);
                var reference = new InstantQuotationUploadReference(Guid.NewGuid().ToString("D"));
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("synthetic-historical-browser", reference, digest),
                    new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(), 12, 1, true, false, false, 1))!;
                var part = new InstantQuotationPart(Guid.NewGuid(), "historical-controlled.stl", reference, geometry, new("M68", "White", 1));
                Assert.True(await store.PutAsync(stored with { RequestState = new([part]) }, null, default));
                await fixture.Page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
                await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
                await fixture.Page.EvaluateAsync("""
                    () => {
                        window.materialDraftDiagnostic = [];
                        for (const kind of ['input', 'change']) {
                            document.addEventListener(kind, event => {
                                if (!event.target.matches('input[name="quantity"]')) return;
                                const parsed = Number(event.target.value);
                                window.materialDraftDiagnostic.push({kind,
                                    quantity: Number.isInteger(parsed) && parsed >= 0 && parsed <= 10000 ? parsed : -1});
                            }, true);
                        }
                    }
                    """);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async Task ChangeQuantityAsync(int quantity)
        {
            Control.Timeline.Enqueue(("fill-start", quantity, false));
            await Quantity.FillAsync(quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Quantity.PressAsync("Tab");
            Control.Timeline.Enqueue(("tab-finished", quantity, false));
        }
        public async Task<InstantQuotationSessionState> StoredAsync() =>
            (await Factory.Services.GetRequiredService<IInstantQuotationSessionStore>().GetAsync(sessionId, null, default))!;
        public async Task CaptureAsync(string name)
        {
            var directory = Path.GetFullPath(Path.Combine("TestResults", "material-display-browser-controls"));
            Directory.CreateDirectory(directory);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png"), FullPage = true });
            var widths = await Page.EvaluateAsync<int[]>(
                "() => [innerWidth, document.documentElement.clientWidth, document.documentElement.scrollWidth]");
            await File.WriteAllTextAsync(Path.Combine(directory, name + "-widths.json"),
                System.Text.Json.JsonSerializer.Serialize(new { Viewport = widths[0], Client = widths[1], Scroll = widths[2] }));
            // Screenshot contains only this fixture's synthetic historical content; no cookies or private state.
        }
        public async Task CaptureDiagnosticsAsync(string name)
        {
            var directory = Path.GetFullPath(Path.Combine("TestResults", "material-display-browser-diagnostics"));
            Directory.CreateDirectory(directory);
            var dom = await Page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.materialDraftDiagnostic");
            var server = Control.Timeline.Select(step => new { step.Stage, step.Quantity, step.Canceled }).ToArray();
            await File.WriteAllTextAsync(Path.Combine(directory, $"{name}-{Guid.NewGuid():N}.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Dom = dom,
                    Server = server,
                    BrowserPageOpen = !Page.IsClosed,
                    PageErrors = pageErrorCount,
                    AcceptedQuantity = Assert.Single((await StoredAsync()).Parts).Configuration.Quantity,
                }));
        }
        public async ValueTask DisposeAsync()
        {
            Control.ReleaseAll();
            foreach (var gate in Control.Gates.Where(candidate => candidate.Claimed != 0))
                await gate.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (context is not null) await context.DisposeAsync();
            if (browser is not null) await browser.DisposeAsync();
            playwright?.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
