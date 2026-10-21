using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual renderer and protected coordinator with synthetic historical resin input; not upload/auth acceptance.</summary>
public sealed class MaterialPricingDisplayRendererContractTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Renderer_SelectedCompletedAppearsBeforeFinalQuoteWithoutEnablingReview(string culture)
    {
        await using var fixture = await Fixture.CreateAsync(culture, InstantQuotationMaterialPricingStatus.Completed);
        try
        {
            await fixture.Pricing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.AssertNoFinalAuthorityAsync();
            var html = await fixture.HtmlAsync();
            AssertReviewDisabled(html);
            var row = MaterialRow(html, "M68");
            Assert.Contains("600.00", WebUtility.HtmlDecode(row), StringComparison.Ordinal);
            Assert.DoesNotContain("1391", row, StringComparison.Ordinal);
        }
        finally { await fixture.CompleteAsync(); }
    }

    [Theory]
    [InlineData("en", "Calculating price…")]
    [InlineData("th", "กำลังคำนวณราคา…")]
    public async Task Renderer_PendingIsLocalizedAndNeverNumericZero(string culture, string status)
    {
        await using var fixture = await Fixture.CreateAsync(culture, InstantQuotationMaterialPricingStatus.Pending);
        try
        {
            await fixture.Pricing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.AssertNoFinalAuthorityAsync();
            var html = await fixture.HtmlAsync();
            AssertReviewDisabled(html);
            var row = MaterialRow(html, "M68");
            Assert.Contains(status, WebUtility.HtmlDecode(row), StringComparison.Ordinal);
            Assert.DoesNotContain("฿0", WebUtility.HtmlDecode(row), StringComparison.Ordinal);
            Assert.Contains("role=\"status\"", row, StringComparison.Ordinal);
        }
        finally { await fixture.CompleteAsync(); }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Renderer_FinalQuoteRetainsLiteralMoneyAndEnablesReviewOnlyAfterCompletion(string culture)
    {
        await using var fixture = await Fixture.CreateAsync(culture, InstantQuotationMaterialPricingStatus.Completed);
        await fixture.Pricing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.AssertNoFinalAuthorityAsync();
        await fixture.CompleteAsync();
        var html = await fixture.HtmlAsync();
        Assert.DoesNotContain("disabled", ReviewButton(html), StringComparison.Ordinal);
        Assert.Contains("600.00", WebUtility.HtmlDecode(MaterialRow(html, "M68")), StringComparison.Ordinal);
        var stored = await fixture.Store.GetAsync(fixture.SessionId, null, default);
        Assert.NotNull(stored?.QuoteAuthorization);
        var quote = Assert.IsType<InstantQuotationOrderQuote>(fixture.Pricing.FinalQuote);
        Assert.Equal(600d, Assert.Single(quote.Parts).UnitPrice);
        Assert.Equal(1200d, Assert.Single(quote.Parts).Subtotal);
        Assert.Equal(1391d, quote.FinalOrderPrice);
        Assert.True(fixture.Tickets.Validate(stored, quote, stored.QuoteAuthorization, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("en", "Price unavailable — engineering review required.")]
    [InlineData("th", "ยังไม่สามารถแสดงราคาได้ ต้องให้วิศวกรตรวจสอบ")]
    public async Task Renderer_FinalQuoteRestoresComparisonRowsMissingFromPartialProgress(string culture, string unavailable)
    {
        await using var fixture = await Fixture.CreateAsync(culture, InstantQuotationMaterialPricingStatus.Completed);
        try
        {
            await fixture.Pricing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.AssertNoFinalAuthorityAsync();
            var partial = await fixture.HtmlAsync();
            AssertReviewDisabled(partial);
            Assert.Contains("600.00", WebUtility.HtmlDecode(MaterialRow(partial, "M68")), StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-material-price=\"PETG\"", partial, StringComparison.Ordinal);

            await fixture.CompleteAsync();

            var stored = await fixture.Store.GetAsync(fixture.SessionId, null, default);
            Assert.NotNull(stored?.QuoteAuthorization);
            var final = Assert.IsType<InstantQuotationOrderQuote>(fixture.Pricing.FinalQuote);
            var comparison = Assert.Single(Assert.Single(final.Parts).MaterialPrices, price => price.MaterialKey == "PETG");
            Assert.Null(comparison.UnitPrice);
            Assert.True(fixture.Tickets.Validate(stored, final, stored.QuoteAuthorization, DateTimeOffset.UtcNow));
            var html = await fixture.HtmlAsync();
            Assert.DoesNotContain("disabled", ReviewButton(html), StringComparison.Ordinal);
            var row = WebUtility.HtmlDecode(MaterialRow(html, "PETG"));
            Assert.Contains(unavailable, row, StringComparison.Ordinal);
            Assert.DoesNotContain("฿0", row, StringComparison.Ordinal);
            var card = Regex.Match(html,
                "<span\\b[^>]*data-material-card-price=\"PETG\"[^>]*>.*?</span>", RegexOptions.Singleline);
            Assert.True(card.Success, "The actual material card must retain the final comparison availability.");
            Assert.Contains("data-price-status=\"unavailable\"", card.Value, StringComparison.Ordinal);
            Assert.Contains(unavailable, WebUtility.HtmlDecode(card.Value), StringComparison.Ordinal);
        }
        finally { await fixture.CompleteAsync(); }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Renderer_HiddenPartFramesRetainSnapshotsWithoutRedundantRendersOrAuthority(string culture)
    {
        await RunProjectionAsync(culture, async fixture =>
        {
            await fixture.Pricing.FirstSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.AssertNoFinalAuthorityAsync();
            var before = fixture.RenderCount;
            var html = await fixture.HtmlAsync();
            fixture.Pricing.ReleaseFirst.TrySetResult();
            await fixture.Pricing.SecondSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, fixture.RenderCount);
            Assert.Equal(html, await fixture.HtmlAsync());
            Assert.Contains(fixture.Snapshot.Entries, row => row.PartId == fixture.SecondPartId
                && row.MaterialKey == "M68" && row.Status == InstantQuotationMaterialPricingStatus.Completed);
            await fixture.AssertNoFinalAuthorityAsync();
            fixture.Pricing.ReleaseSecond.TrySetResult();
            await fixture.Pricing.FinalComputed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.AssertNoFinalAuthorityAsync();
            var beforeFinal = fixture.RenderCount;
            await fixture.CompleteAsync();
            Assert.True(fixture.RenderCount > beforeFinal);
            var finalHtml = await fixture.HtmlAsync();
            Assert.DoesNotContain("disabled", ReviewButton(finalHtml), StringComparison.Ordinal);
            var stored = (await fixture.Store.GetAsync(fixture.SessionId, null, default))!;
            var quote = Assert.IsType<InstantQuotationOrderQuote>(fixture.Pricing.FinalQuote);
            Assert.Equal(2, quote.Parts.Count);
            Assert.True(fixture.Tickets.Validate(stored, quote, stored.QuoteAuthorization!, DateTimeOffset.UtcNow));
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Renderer_ViewSwitchMakesTheNewlyVisiblePartFramesRender(string culture)
    {
        await RunProjectionAsync(culture, async fixture =>
        {
            await fixture.Pricing.FirstSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.SelectPartAsync(fixture.SecondPartId);
            var before = fixture.RenderCount;
            fixture.Pricing.ReleaseFirst.TrySetResult();
            await fixture.Pricing.SecondSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.RenderCount > before);
            var html = await fixture.HtmlAsync();
            Assert.Contains("quantity-" + fixture.SecondPartId, html, StringComparison.Ordinal);
            Assert.Contains("600.00", WebUtility.HtmlDecode(MaterialRow(html, "M68")), StringComparison.Ordinal);
            AssertReviewDisabled(html);
            await fixture.AssertNoFinalAuthorityAsync();
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Renderer_NewGenerationRejectsStaleAndCanceledFramesWithoutPoisoningVisibleProjection(string culture)
    {
        await RunProjectionAsync(culture, async fixture =>
        {
            await fixture.CompleteAsync();
            var old = fixture.Snapshot;
            var beforeEdit = fixture.RenderCount;
            await fixture.BeginQuantityChangeAsync();
            await fixture.Pricing.FirstSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Snapshot.Generation > old.Generation);
            Assert.True(fixture.RenderCount > beforeEdit);
            var beforeStale = fixture.RenderCount;
            await fixture.NotifyAsync(old, default);
            Assert.Equal(beforeStale, fixture.RenderCount);
            await fixture.SelectPartAsync(fixture.SecondPartId);
            var beforeCanceled = fixture.RenderCount;
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await fixture.NotifyAsync(fixture.Snapshot, canceled.Token);
            Assert.Equal(beforeCanceled, fixture.RenderCount);
            await fixture.NotifyAsync(fixture.Snapshot, default);
            Assert.True(fixture.RenderCount > beforeCanceled);
            Assert.Contains("quantity-" + fixture.SecondPartId, await fixture.HtmlAsync(), StringComparison.Ordinal);
            Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, null, default))!.QuoteAuthorization);
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Renderer_DisposedProjectionCannotQueueLateRenderOrPublishAuthority(string culture)
    {
        await RunProjectionAsync(culture, async fixture =>
        {
            await fixture.Pricing.FirstSelected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = fixture.Snapshot;
            await fixture.DisposeRendererAsync();
            var before = fixture.RenderCount;
            await fixture.NotifyAsync(snapshot, default);
            Assert.Equal(before, fixture.RenderCount);
            await fixture.CompleteAsync();
            Assert.Null(fixture.Pricing.FinalQuote);
            Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, null, default))!.QuoteAuthorization);
        });
    }

    private static async Task RunProjectionAsync(string culture, Func<Fixture, Task> body)
    {
        Fixture? fixture = null;
        Exception? primary = null;
        try
        {
            fixture = await Fixture.CreateAsync(culture, InstantQuotationMaterialPricingStatus.Completed,
                partCount: 2, projectionControls: true);
            await body(fixture);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            if (fixture is not null)
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await fixture.CompleteAsync(), fixture.DisposeAsync);
        }
    }

    private static string MaterialRow(string html, string key)
    {
        var match = Regex.Match(html, $"<li\\b[^>]*data-workflow-material-price=\"{Regex.Escape(key)}\"[^>]*>.*?</li>", RegexOptions.Singleline);
        Assert.True(match.Success, "The actual component did not render the attempted material display row.");
        return match.Value;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Renderer_DisposalDuringCompletedCallbackCannotPublishFinalAuthority(string culture)
    {
        await using var fixture = await Fixture.CreateAsync(culture, InstantQuotationMaterialPricingStatus.Completed);
        await fixture.Pricing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.AssertNoFinalAuthorityAsync();
        Assert.Contains("600.00", WebUtility.HtmlDecode(MaterialRow(await fixture.HtmlAsync(), "M68")), StringComparison.Ordinal);
        await fixture.DisposeRendererAsync();
        await fixture.CompleteAsync();
        Assert.Null(fixture.Pricing.FinalQuote);
        Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, null, default))!.QuoteAuthorization);
    }

    private static string ReviewButton(string html)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(html), "<button\\b[^>]*>\\s*(Review|ตรวจสอบรายการ)\\s*</button>", RegexOptions.Singleline);
        Assert.True(match.Success, "The actual component review action was not rendered.");
        return match.Value;
    }

    private static void AssertReviewDisabled(string html)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(html), "<button\\b[^>]*>\\s*(Review|ตรวจสอบรายการ)\\s*</button>", RegexOptions.Singleline);
        // During restored initialization the action is absent; absence cannot authorize review.
        if (match.Success) Assert.Contains("disabled", match.Value, StringComparison.Ordinal);
    }

    private sealed class PricingBoundary(IInstantQuotationAuthoritativePricingService inner,
        InstantQuotationMaterialPricingStatus pauseAt) : IInstantQuotationAuthoritativePricingService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InstantQuotationMaterialPricingProgress? PausedFrame { get; private set; }
        public bool ObserverRoute { get; private set; }
        public InstantQuotationOrderQuote? FinalQuote { get; private set; }
        public bool ProjectionControls { get; set; }
        public TaskCompletionSource FirstSelected { get; private set; } = NewGate();
        public TaskCompletionSource ReleaseFirst { get; private set; } = NewGate();
        public TaskCompletionSource SecondSelected { get; private set; } = NewGate();
        public TaskCompletionSource ReleaseSecond { get; private set; } = NewGate();
        public TaskCompletionSource FinalComputed { get; private set; } = NewGate();
        public TaskCompletionSource ReleaseFinal { get; private set; } = NewGate();
        private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ResetProjection()
        {
            ProjectionControls = true;
            FirstSelected = NewGate(); ReleaseFirst = NewGate();
            SecondSelected = NewGate(); ReleaseSecond = NewGate();
            FinalComputed = NewGate(); ReleaseFinal = NewGate();
            FinalQuote = null;
        }
        public void ReleaseAll()
        {
            Release.TrySetResult();
            ReleaseFirst.TrySetResult(); ReleaseSecond.TrySetResult(); ReleaseFinal.TrySetResult();
        }

        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, CancellationToken token) => inner.QuoteAsync(session, owner, comparisons, token);

        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken token)
        {
            ObserverRoute = true;
            bool paused = false;
            int selected = 0;
            var quote = await inner.QuoteAsync(session, owner, comparisons, async (frame, callbackToken) =>
            {
                await observer(frame, callbackToken);
                if (ProjectionControls)
                {
                    if (frame.MaterialKey == "M68" && frame.Status == InstantQuotationMaterialPricingStatus.Completed)
                    {
                        if (++selected == 1)
                        {
                            FirstSelected.TrySetResult();
                            await ReleaseFirst.Task.WaitAsync(callbackToken);
                        }
                        else
                        {
                            SecondSelected.TrySetResult();
                            await ReleaseSecond.Task.WaitAsync(callbackToken);
                        }
                    }
                    return;
                }
                if (!paused && frame.Status == pauseAt)
                {
                    paused = true;
                    PausedFrame = frame;
                    Entered.TrySetResult();
                    await Release.Task.WaitAsync(callbackToken);
                }
            }, token);
            if (ProjectionControls)
            {
                FinalComputed.TrySetResult();
                await ReleaseFinal.Task.WaitAsync(token);
            }
            FinalQuote = quote;
            return FinalQuote;
        }
    }

    private sealed class CountingWorkflow : InstantQuotationWorkflow
    {
        private int renders;
        public int RenderCount => Volatile.Read(ref renders);
        protected override bool ShouldRender()
        {
            Interlocked.Increment(ref renders);
            return base.ShouldRender();
        }
    }

    private sealed class CountingActivator(Func<IServiceProvider> services) : IComponentActivator
    {
        public CountingWorkflow? Workflow { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            if (componentType != typeof(InstantQuotationWorkflow))
                return (IComponent)Activator.CreateInstance(componentType)!;
            var component = new CountingWorkflow();
            // The measured subclass still uses the actual workflow's private injected services.
            foreach (var property in typeof(InstantQuotationWorkflow).GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (property.GetCustomAttribute<InjectAttribute>() is not null)
                    property.SetValue(component, services().GetRequiredService(property.PropertyType));
            Workflow = component;
            return component;
        }
    }

    private sealed class SessionIdentity(string identity) : IInstantQuotationWorkflowSessionIdentityAccessor
    {
        public ValueTask<string?> GetProtectedSessionIdentityAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>(identity); }
        public ValueTask SetProtectedSessionIdentityAsync(string? value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Assert.Equal(identity, value); return ValueTask.CompletedTask; }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new InvalidOperationException("Static rendering must not invoke browser JavaScript."));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TestingWebApplicationFactory factory = new();
        private readonly AsyncServiceScope scope;
        private ServiceProvider? rendererServices;
        private HtmlRenderer? renderer;
        private HtmlRootComponent root;
        private bool rootCreated;
        private CountingActivator? counter;
        private Task? quantityChange;
        public int RenderCount => counter!.Workflow!.RenderCount;
        public Guid SecondPartId { get; private set; }
        private InstantQuotationWorkflowCoordinator Coordinator =>
            (InstantQuotationWorkflowCoordinator)typeof(InstantQuotationWorkflow).GetField(
                "workflow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(counter!.Workflow)!;
        public InstantQuotationMaterialPriceDisplaySnapshot Snapshot => Coordinator.MaterialPriceDisplay;
        public Task NotifyAsync(InstantQuotationMaterialPriceDisplaySnapshot snapshot, CancellationToken token) =>
            ((ValueTask)typeof(InstantQuotationWorkflow).GetMethod("MaterialDisplayChangedAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(counter!.Workflow, [snapshot, token])!).AsTask();
        public Task SelectPartAsync(Guid partId) => renderer!.Dispatcher.InvokeAsync(() =>
            EventCallback.Factory.Create<Guid>(counter!.Workflow!, id =>
                (Task)typeof(InstantQuotationWorkflow).GetMethod("SelectPreviewAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(counter.Workflow, [id])!)
                .InvokeAsync(partId));
        public Task BeginQuantityChangeAsync() => renderer!.Dispatcher.InvokeAsync(() =>
        {
            Pricing.ResetProjection();
            var first = Coordinator.Parts.First();
            Func<Task> update = () => Coordinator.UpdateConfigurationAsync(
                first.PartId, "M68", "White", 3, default);
            quantityChange = (Task)typeof(InstantQuotationWorkflow).GetMethod("RepriceAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(counter!.Workflow, [update, null, null])!;
        });
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        public IInstantQuotationSessionStore Store { get; }
        public PricingBoundary Pricing { get; }
        public IInstantQuotationQuoteTicketService Tickets => scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>();
        public string SessionId { get; private set; } = "";

        private Fixture(InstantQuotationMaterialPricingStatus pauseAt)
        {
            scope = factory.Services.CreateAsyncScope();
            Store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
            Pricing = new(scope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>(), pauseAt);
        }

        public static async Task<Fixture> CreateAsync(string culture, InstantQuotationMaterialPricingStatus pauseAt,
            int partCount = 1, bool projectionControls = false)
        {
            var fixture = new Fixture(pauseAt);
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var reference = new InstantQuotationUploadReference("66666666-6666-6666-6666-666666666666");
                var digest = new string('a', 64);
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("historical-renderer-fixture", reference, digest),
                    new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(), 12, 1, true, false, false, 1))!;
                var part = new InstantQuotationPart(Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    "controlled.stl", reference, geometry, new("M68", "White", 2));
                var parts = new List<InstantQuotationPart> { part };
                if (partCount == 2)
                {
                    var second = part with
                    {
                        PartId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                        DisplayFileName = "second-controlled.stl",
                    };
                    parts.Add(second);
                    fixture.SecondPartId = second.PartId;
                }
                var saved = await fixture.Store.CreateAsync(null, new(parts), default);
                fixture.SessionId = saved.SessionId;
                fixture.Pricing.ProjectionControls = projectionControls;
                var services = new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources")
                    .AddSingleton<IJSRuntime, NoJavaScript>()
                    .AddSingleton(fixture.scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>())
                    .AddSingleton(fixture.Store)
                    .AddSingleton(fixture.scope.ServiceProvider.GetRequiredService<IInstantQuotationUploadClient>())
                    .AddSingleton(fixture.scope.ServiceProvider.GetRequiredService<IInstantQuotationPricingService>())
                    .AddSingleton(fixture.scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>())
                    .AddSingleton(fixture.scope.ServiceProvider.GetRequiredService<FdmRuntimeProfileCatalog>())
                    .AddSingleton<IInstantQuotationAuthoritativePricingService>(fixture.Pricing)
                    .AddSingleton<IInstantQuotationWorkflowSessionIdentityAccessor>(new SessionIdentity(saved.SessionId));
                if (projectionControls)
                {
                    fixture.counter = new CountingActivator(() => fixture.rendererServices!);
                    services.AddSingleton<IComponentActivator>(fixture.counter);
                }
                fixture.rendererServices = services.BuildServiceProvider();
                fixture.renderer = new(fixture.rendererServices, fixture.rendererServices.GetRequiredService<ILoggerFactory>());
                fixture.root = await fixture.renderer.Dispatcher.InvokeAsync(() =>
                    fixture.renderer.BeginRenderingComponent<InstantQuotationWorkflow>());
                fixture.rootCreated = true;
                return fixture;
            }
            catch (Exception primary)
            {
                if (projectionControls)
                    await MaterialPricingTestCleanup.ReleaseAllAsync(primary, fixture.DisposeAsync);
                else
                    await fixture.DisposeAsync();
                throw;
            }
        }

        public Task<string> HtmlAsync() => renderer!.Dispatcher.InvokeAsync(root.ToHtmlString);
        public async Task AssertNoFinalAuthorityAsync()
        {
            Assert.True(Pricing.ObserverRoute, "The actual component coordinator did not reach the observer-producing backend.");
            Assert.Null((await Store.GetAsync(SessionId, null, default))!.QuoteAuthorization);
            Assert.False(root.QuiescenceTask.IsCompleted, "The gate must precede final component initialization completion.");
        }
        public async Task CompleteAsync()
        {
            Pricing.ReleaseAll();
            await Task.WhenAll(root.QuiescenceTask, quantityChange ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        public async Task DisposeRendererAsync()
        {
            if (renderer is null) return;
            await renderer.DisposeAsync();
            // Disposal cancels initialization, whose framework lifecycle continuation is still owned by the root.
            if (rootCreated) await root.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(10));
            renderer = null;
        }
        public async ValueTask DisposeAsync()
        {
            if (Pricing.ProjectionControls)
            {
                try
                {
                    await MaterialPricingTestCleanup.ReleaseAllAsync(null,
                        () => { Pricing.ReleaseAll(); return ValueTask.CompletedTask; },
                        async () =>
                        {
                            if (rootCreated)
                                await Task.WhenAll(root.QuiescenceTask, quantityChange ?? Task.CompletedTask)
                                    .WaitAsync(TimeSpan.FromSeconds(10));
                        },
                        async () => await DisposeRendererAsync(),
                        async () => { if (rendererServices is not null) await rendererServices.DisposeAsync(); },
                        scope.DisposeAsync, factory.DisposeAsync);
                }
                finally
                {
                    CultureInfo.CurrentCulture = previousCulture;
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }
                return;
            }
            Pricing.ReleaseAll();
            await DisposeRendererAsync();
            if (rendererServices is not null) await rendererServices.DisposeAsync();
            await scope.DisposeAsync();
            await factory.DisposeAsync();
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
