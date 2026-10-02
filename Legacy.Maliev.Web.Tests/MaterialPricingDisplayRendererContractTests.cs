using System.Globalization;
using System.Net;
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

        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, CancellationToken token) => inner.QuoteAsync(session, owner, comparisons, token);

        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? owner,
            bool comparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken token)
        {
            ObserverRoute = true;
            bool paused = false;
            FinalQuote = await inner.QuoteAsync(session, owner, comparisons, async (frame, callbackToken) =>
            {
                await observer(frame, callbackToken);
                if (!paused && frame.Status == pauseAt)
                {
                    paused = true;
                    PausedFrame = frame;
                    Entered.TrySetResult();
                    await Release.Task.WaitAsync(callbackToken);
                }
            }, token);
            return FinalQuote;
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

        public static async Task<Fixture> CreateAsync(string culture, InstantQuotationMaterialPricingStatus pauseAt)
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
                var saved = await fixture.Store.CreateAsync(null, new([part]), default);
                fixture.SessionId = saved.SessionId;
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
                fixture.rendererServices = services.BuildServiceProvider();
                fixture.renderer = new(fixture.rendererServices, fixture.rendererServices.GetRequiredService<ILoggerFactory>());
                fixture.root = await fixture.renderer.Dispatcher.InvokeAsync(() =>
                    fixture.renderer.BeginRenderingComponent<InstantQuotationWorkflow>());
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
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
            Pricing.Release.TrySetResult();
            await root.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        public async Task DisposeRendererAsync()
        {
            if (renderer is null) return;
            await renderer.DisposeAsync();
            renderer = null;
        }
        public async ValueTask DisposeAsync()
        {
            Pricing.Release.TrySetResult();
            await DisposeRendererAsync();
            if (rendererServices is not null) await rendererServices.DisposeAsync();
            await scope.DisposeAsync();
            await factory.DisposeAsync();
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
