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
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real DOM/circuit events over controlled historical protected input; not FileService or authentication acceptance.</summary>
public sealed class MaterialPricingDisplayBrowserTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("en", 375, ColorScheme.Light)]
    [InlineData("th", 1280, ColorScheme.Dark)]
    public async Task MaterialCardsSearchBothLanguagesAndArrowFocusDoesNotChangeSelection(string culture, int width, ColorScheme scheme)
    {
        await using var fixture = await Fixture.CreateAsync(output, culture, width, scheme);
        var selector = fixture.Page.Locator("[data-material-selector]");
        var selected = selector.Locator("[role='radio'][aria-checked='true']");
        await Assertions.Expect(selected).ToHaveAttributeAsync("data-material-key", "M68");
        var initialUnitPrice = Assert.Single(Assert.IsType<InstantQuotationOrderQuote>(fixture.Control.FinalQuote).Parts).UnitPrice;
        await Assertions.Expect(selector.Locator("[data-material-card-price='M68']")).ToContainTextAsync(initialUnitPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture));
        var cardLayout = await selected.EvaluateAsync<double[]>("""
            card => {
                const name = card.querySelector('strong').getBoundingClientRect();
                const price = card.querySelector('[data-material-card-price]').getBoundingClientRect();
                return [name.top, price.top, price.right, card.getBoundingClientRect().right];
            }
            """);
        Assert.True(cardLayout[1] > cardLayout[0], "The price belongs in the source secondary row.");
        Assert.True(cardLayout[2] <= cardLayout[3], "Card price must remain inside the narrow card.");
        var firstRow = await selector.EvaluateAsync<double[]>("""
            selector => {
                const first = selector.querySelector('[data-material-key="PLA"]').getBoundingClientRect();
                const second = selector.querySelector('[data-material-key="PETG"]').getBoundingClientRect();
                return [first.top, second.top, first.left, second.left];
            }
            """);
        Assert.InRange(Math.Abs(firstRow[0] - firstRow[1]), 0, 1);
        Assert.True(firstRow[3] > firstRow[2], "The source picker uses two columns in both locales.");
        var informationBounds = await selected.EvaluateAsync<double[]>("""
            card => {
                const button = card.querySelector('button').getBoundingClientRect();
                const price = card.querySelector('[data-material-card-price]').getBoundingClientRect();
                const bounds = card.getBoundingClientRect();
                return [button.left, button.top, button.right, button.bottom, bounds.left, bounds.top, bounds.right, bounds.bottom, price.top];
            }
            """);
        Assert.True(informationBounds[0] >= informationBounds[4] && informationBounds[1] >= informationBounds[5]);
        Assert.True(informationBounds[2] <= informationBounds[6] && informationBounds[3] <= informationBounds[7]);
        Assert.True(informationBounds[3] <= informationBounds[8], "The info hit area must not overlap the price row.");
        await selected.Locator("button").ClickAsync();
        await Assertions.Expect(selected).ToHaveAttributeAsync("aria-checked", "true");
        await Assertions.Expect(selected.Locator("button")).ToHaveAttributeAsync("aria-expanded", "true");
        await selected.Locator("button").ClickAsync();
        var otherInformation = selector.Locator("[data-material-key='PLA'] button");
        await otherInformation.ClickAsync();
        await Assertions.Expect(selector.Locator("[data-material-key='PLA']")).ToHaveAttributeAsync("aria-checked", "false");
        Assert.Equal("M68", Assert.Single((await fixture.StoredAsync()).Parts).Configuration.MaterialKey);
        await otherInformation.ClickAsync();
        await selected.FocusAsync();
        await selected.PressAsync("ArrowDown");
        await Assertions.Expect(selected).ToHaveAttributeAsync("data-material-key", "M68");
        await Assertions.Expect(selector.Locator("[data-material-key='K']")).ToBeFocusedAsync();
        var search = selector.Locator("[data-material-search]");
        await search.FillAsync("carbon nanotubes");
        await Assertions.Expect(selector.Locator("[role='radio']")).ToHaveCountAsync(1);
        await Assertions.Expect(selector.Locator("[data-material-key='PA612-ESD']")).ToBeVisibleAsync();
        await search.FillAsync("เป็นมิตร");
        await Assertions.Expect(selector.Locator("[data-material-key='PLA']")).ToBeVisibleAsync();
        await search.FillAsync("not-a-material-550");
        await Assertions.Expect(selector.Locator("[data-material-category]")).ToHaveCountAsync(0);
        await Assertions.Expect(selector.Locator("[role='radio']")).ToHaveCountAsync(0);
        await search.FillAsync(string.Empty);
        await Assertions.Expect(selector.Locator("[data-material-key='M68']")).ToHaveAttributeAsync("aria-checked", "true");
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        var scroller = selector.Locator("[data-material-card-scroll]");
        await scroller.HoverAsync();
        var outerScroll = await fixture.Page.EvaluateAsync<double>("() => window.scrollY");
        await fixture.Page.Mouse.WheelAsync(0, 300);
        await fixture.Page.WaitForFunctionAsync("() => document.querySelector('[data-material-card-scroll]').scrollTop > 0");
        Assert.Equal(outerScroll, await fixture.Page.EvaluateAsync<double>("() => window.scrollY"));
        Assert.Equal("M68", Assert.Single((await fixture.StoredAsync()).Parts).Configuration.MaterialKey);
        await selected.FocusAsync();
        await selected.PressAsync("ArrowDown");
        await selector.Locator("[data-material-key='K']").PressAsync(" ");
        await Assertions.Expect(selector.Locator("[data-material-key='K']")).ToHaveAttributeAsync("aria-checked", "true");
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        Assert.Equal("K", Assert.Single((await fixture.StoredAsync()).Parts).Configuration.MaterialKey);
        await fixture.CaptureAsync($"source-material-consumer-{culture}-{width}");
    }

    [Theory]
    [InlineData("en", ColorScheme.Light)]
    [InlineData("th", ColorScheme.Dark)]
    public async Task ShortDesktopMaterialCardClickIsNotCoveredBySummaryFooter(string culture, ColorScheme scheme)
    {
        await using var fixture = await Fixture.CreateAsync(output, culture, 1280, scheme, height: 720,
            displayFileName: "maliev-one-millimeter-fdm-00000000000000000000000000000000.stl");
        var card = fixture.Page.Locator("[data-material-selector] [role='radio'][data-material-key='ABS']");
        await card.ScrollIntoViewIfNeededAsync();
        var hitArea = await card.EvaluateAsync<double[]>("""
            card => {
                const bounds = card.getBoundingClientRect();
                const content = card.closest('[data-workflow-configuration-content]').getBoundingClientRect();
                const footer = card.closest('[data-workflow-configuration]').querySelector('.instant-quote__configuration-footer').getBoundingClientRect();
                const hit = card.ownerDocument.elementFromPoint(bounds.left + bounds.width / 2, bounds.top + bounds.height / 2);
                return [card.contains(hit) ? 1 : 0, bounds.top, bounds.bottom, content.top, content.bottom, footer.top];
            }
            """);
        var evidenceName = $"source-material-safe-hit-area-before-{culture}-1280-720";
        await fixture.CaptureAsync(evidenceName);
        await File.WriteAllTextAsync(Path.GetFullPath(Path.Combine("TestResults", "material-display-browser-controls", evidenceName + "-geometry.json")),
            System.Text.Json.JsonSerializer.Serialize(new { Culture = culture, Width = 1280, Height = 720, Geometry = hitArea }));
        Assert.True(hitArea[0] == 1 && hitArea[1] >= hitArea[3] - 1 && hitArea[2] <= hitArea[4] + 1 && hitArea[2] <= hitArea[5] + 1,
            "The complete material card and its normal click target must stay above the summary footer.");
        await Assertions.Expect(card.Locator("[data-material-card-price]")).ToHaveAttributeAsync("data-price-status", "unavailable");
        // This historical fixture has resin authority; retain ABS geometry coverage before selecting supported resin.
        card = fixture.Page.Locator("[data-material-selector] [role='radio'][data-material-key='K']");
        await card.ClickAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("aria-checked", "true");
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        Assert.Equal("K", Assert.Single((await fixture.StoredAsync()).Parts).Configuration.MaterialKey);
        await Assertions.Expect(fixture.Page.Locator(".instant-quote__configuration-footer")).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.Locator("[data-summary-legal]")).ToBeVisibleAsync();
        await fixture.CaptureAsync($"source-material-safe-hit-area-{culture}-1280-720");
    }

    [Theory]
    [InlineData("en", 375, ColorScheme.Light, InstantQuotationMaterialPricingStatus.Pending, "Calculating price…")]
    [InlineData("th", 320, ColorScheme.Dark, InstantQuotationMaterialPricingStatus.Completed, "600.00")]
    public async Task ActualQuantityEventShowsCurrentProgressBeforeFinalAuthority(string culture, int width,
        ColorScheme scheme, InstantQuotationMaterialPricingStatus status, string expected)
    {
        await using var fixture = await Fixture.CreateAsync(output, culture, width, scheme);
        var gate = fixture.Control.Arm(2, status);
        try
        {
            await fixture.ChangeQuantityAsync(2);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Page.Locator("[data-workflow-material-comparison] > summary").ClickAsync();
            var row = fixture.Page.Locator("[data-workflow-material-price='M68']");
            await Assertions.Expect(row).ToBeVisibleAsync();
            var cardPrice = fixture.Page.Locator("[data-material-card-price='M68']");
            await Assertions.Expect(cardPrice).ToHaveAttributeAsync("data-price-status", status.ToString().ToLowerInvariant());
            await Assertions.Expect(cardPrice).ToContainTextAsync(culture == "th" && status == InstantQuotationMaterialPricingStatus.Pending
                ? "กำลังคำนวณราคา" : expected);
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
        await using var fixture = await Fixture.CreateAsync(output, culture, width, scheme);
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
        await using var fixture = await Fixture.CreateAsync(output, culture, width, scheme);
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
        await using var fixture = await Fixture.CreateAsync(output, culture, width, scheme);
        var three = fixture.Control.Arm(3, InstantQuotationMaterialPricingStatus.Completed, holdAuthorization: true);
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
            await three.AuthorizationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("4");
            await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
            var awaitingAuthorization = await fixture.StoredAsync();
            Assert.Equal(3, Assert.Single(awaitingAuthorization.Parts).Configuration.Quantity);
            Assert.Null(awaitingAuthorization.QuoteAuthorization);
            Assert.False(three.AuthorizationStored.Task.IsCompleted,
                "Pricing return must not stand in for the protected store commit.");
            three.AuthorizationRelease.TrySetResult();
            await three.AuthorizationStored.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var prior = await fixture.StoredAsync();
            Assert.Equal(3, Assert.Single(prior.Parts).Configuration.Quantity);
            Assert.NotNull(prior.QuoteAuthorization);
            Assert.False(four.Entered.Task.IsCompleted, "Input alone cannot submit the new configuration.");
            await fixture.Quantity.PressAsync("Tab");
            await four.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(4, Assert.Single((await fixture.StoredAsync()).Parts).Configuration.Quantity);
            await Assertions.Expect(fixture.Review).ToBeDisabledAsync();
        }
        finally
        {
            three.Release.TrySetResult(); four.Release.TrySetResult();
            three.AuthorizationRelease.TrySetResult();
        }
        await Assertions.Expect(fixture.Quantity).ToHaveValueAsync("4");
        await Assertions.Expect(fixture.Review).ToBeEnabledAsync();
        var accepted = await fixture.StoredAsync();
        Assert.NotNull(accepted.QuoteAuthorization);
        Assert.True(fixture.Factory.Services.GetRequiredService<IInstantQuotationQuoteTicketService>()
            .Validate(accepted, Assert.IsType<InstantQuotationOrderQuote>(fixture.Control.FinalQuote),
                accepted.QuoteAuthorization, DateTimeOffset.UtcNow));
    }

    private sealed class Gate(int quantity, InstantQuotationMaterialPricingStatus status, bool holdAuthorization)
    {
        public int Quantity { get; } = quantity;
        public InstantQuotationMaterialPricingStatus Status { get; } = status;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldAuthorization { get; } = holdAuthorization;
        public TaskCompletionSource AuthorizationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AuthorizationRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AuthorizationStored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AuthorizationFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Claimed;
        public int AuthorizationClaimed;
    }

    private sealed class Control
    {
        public ConcurrentQueue<Gate> Gates { get; } = new();
        public ConcurrentQueue<int> AcceptedQuantities { get; } = new();
        public ConcurrentQueue<(string Stage, int Quantity, bool Canceled)> Timeline { get; } = new();
        public InstantQuotationOrderQuote? FinalQuote { get; set; }
        public Gate Arm(int quantity, InstantQuotationMaterialPricingStatus status, bool holdAuthorization = false)
        { var gate = new Gate(quantity, status, holdAuthorization); Gates.Enqueue(gate); return gate; }
        public void ReleaseAll()
        {
            foreach (var gate in Gates)
            {
                gate.Release.TrySetResult();
                gate.AuthorizationRelease.TrySetResult();
            }
        }
    }

    private sealed class AuthorizationStore(IInstantQuotationSessionStore inner, Control control)
        : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState> CreateAsync(string? owner, InstantQuotationOrderState request,
            CancellationToken token) => inner.CreateAsync(owner, request, token);
        public Task<InstantQuotationSessionState?> GetAsync(string id, string? owner, CancellationToken token) =>
            inner.GetAsync(id, owner, token);
        public Task<bool> RemoveAsync(string id, string? owner, CancellationToken token) => inner.RemoveAsync(id, owner, token);
        public async Task<bool> PutAsync(InstantQuotationSessionState session, string? owner, CancellationToken token)
        {
            var gate = session.QuoteAuthorization is null || session.Parts.Count != 1 ? null :
                control.Gates.FirstOrDefault(candidate => candidate.HoldAuthorization
                    && candidate.Quantity == session.Parts[0].Configuration.Quantity);
            if (gate is null || Interlocked.Exchange(ref gate.AuthorizationClaimed, 1) != 0)
                return await inner.PutAsync(session, owner, token);
            try
            {
                control.Timeline.Enqueue(("authorization-held", gate.Quantity, token.IsCancellationRequested));
                gate.AuthorizationEntered.TrySetResult();
                await gate.AuthorizationRelease.Task.WaitAsync(token);
                var stored = await inner.PutAsync(session, owner, token);
                if (stored)
                {
                    control.Timeline.Enqueue(("authorization-stored", gate.Quantity, token.IsCancellationRequested));
                    gate.AuthorizationStored.TrySetResult();
                }
                return stored;
            }
            finally { gate.AuthorizationFinished.TrySetResult(); }
        }
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
                var store = services.Single(item => item.ServiceType == typeof(IInstantQuotationSessionStore));
                services.RemoveAll<IInstantQuotationSessionStore>();
                services.Add(new ServiceDescriptor(typeof(IInstantQuotationSessionStore), provider =>
                    new AuthorizationStore((IInstantQuotationSessionStore)(store.ImplementationInstance
                        ?? store.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, store.ImplementationType!)), control), store.Lifetime));
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

        public static async Task<Fixture> CreateAsync(ITestOutputHelper output, string culture, int width, ColorScheme scheme, int height = 800,
            string displayFileName = "historical-controlled.stl")
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
                { ViewportSize = new() { Width = width, Height = height }, ColorScheme = scheme });
                fixture.Page = await fixture.context.NewPageAsync();
                fixture.Page.PageError += (_, _) => Interlocked.Increment(ref fixture.pageErrorCount);
                var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
                using (var initialNavigation = new MaterialInitialNavigationDiagnostics(fixture.Page, origin))
                {
                    await initialNavigation.RunAsync(
                        () => fixture.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle }), output.WriteLine);
                }
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
                var part = new InstantQuotationPart(Guid.NewGuid(), displayFileName, reference, geometry, new("M68", "White", 1));
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
            catch (Exception primary) { await fixture.DisposeAsync(primary); throw; }
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
        public ValueTask DisposeAsync() => DisposeAsync(null);

        private async ValueTask DisposeAsync(Exception? primary)
        {
            var releases = new List<Func<ValueTask>>
            {
                () => { Control.ReleaseAll(); return ValueTask.CompletedTask; },
            };
            foreach (var gate in Control.Gates)
            {
                releases.Add(async () =>
                {
                    if (gate.Claimed != 0) await gate.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                });
            }
            foreach (var gate in Control.Gates)
            {
                releases.Add(async () =>
                {
                    if (Volatile.Read(ref gate.AuthorizationClaimed) != 0)
                        await gate.AuthorizationFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
                });
            }
            releases.Add(async () => { if (context is not null) await context.DisposeAsync(); });
            releases.Add(async () => { if (browser is not null) await browser.DisposeAsync(); });
            releases.Add(() => { playwright?.Dispose(); return ValueTask.CompletedTask; });
            releases.Add(() => Factory.DisposeAsync());
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary, releases.ToArray());
        }
    }
}
