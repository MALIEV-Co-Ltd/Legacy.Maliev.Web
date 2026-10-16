using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(MeasurementLifecycleBrowserCollection.Name)]
public sealed class MeasurementLifecycleBrowserTests(MeasurementLifecycleBrowserFixture fixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task FreshVisit_DefaultsDeniedAndDoesNotRequestExternalAnalytics(int width)
    {
        await using var session = await OpenFreshAsync(width);

        Assert.Equal("denied", await ConsentStateAsync(session.Page, "default"));
        Assert.True(await OptionalStorageDefaultsDeniedAsync(session.Page));
        Assert.Equal(-1, await GtmEventIndexAsync(session.Page));
        Assert.True(await session.Page.Locator("#cookieConsent").IsVisibleAsync());
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task Accept_DuplicateDispatchFlushesControllerQueuedQuotationOnceWithoutReplay(int width)
    {
        await using var session = await OpenFreshAsync(width, submitPersistedQuotation: true);
        Assert.Equal("denied", await ConsentStateAsync(session.Page, "default"));
        Assert.True(await OptionalStorageDefaultsDeniedAsync(session.Page));
        Assert.Equal(-1, await GtmEventIndexAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);

        await session.Page.Locator("#cookieConsent [data-consent-action='accept']")
            .EvaluateAsync("button => { button.click(); button.click(); }");

        Assert.Equal(1, await CountConsentUpdatesAsync(session.Page, "granted"));
        Assert.True(await GtmEventIndexAsync(session.Page) > await ConsentDefaultIndexAsync(session.Page));
        Assert.Equal(0, await session.Page.Locator("#cookieConsent").CountAsync());
        Assert.Equal(2, await CountLeadEventsAsync(session.Page));
        var leadEventsJson = await session.Page.EvaluateAsync<string>(
            "() => JSON.stringify(dataLayer.filter(item => item.event === 'maliev_lead_submitted' || item.event === 'generate_lead'))");
        Assert.True(await session.Page.EvaluateAsync<bool>(@"() => {
            const events = dataLayer.filter(item => item.event === 'maliev_lead_submitted' || item.event === 'generate_lead');
            return events.length === 2
                && events[0].event === 'maliev_lead_submitted'
                && events[1].event === 'generate_lead'
                && events.every(item => item.transaction_id === 'request-42'
                    && item.journey_id === '11111111-2222-3333-4444-555555555555'
                    && item.lead_status === 'persisted'
                    && item.lead_type === 'manual_quote'
                    && item.service === 'custom_manufacturing'
                    && item.has_files === false
                    && !Object.keys(item).some(key =>
                        ['email', 'phone', 'telephone', 'name', 'company', 'message'].includes(key.toLowerCase()))
                    && !JSON.stringify(item).includes('browser-measurement@example.test')
                    && !JSON.stringify(item).includes('Synthetic contact lifecycle test.'));
        }"), leadEventsJson);
        Assert.Contains(session.BlockedUrls, IsExternalAnalyticsUrl);

        await session.Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await session.Page.Locator("#cookieConsent").CountAsync());
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));

        await session.Page.GotoAsync(new Uri(fixture.Origin, "/services/3d-printing?culture=en").ToString());
        await session.Page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
    }

    [Theory]
    [InlineData("en", 390)]
    [InlineData("th", 1440)]
    public async Task PersistedContact_DelayedConsentEmitsOnlyDiagnosticOnceAndReloadDoesNotReplay(
        string culture, int width)
    {
        await using var session = await OpenContactFreshAsync(culture, width);
        Assert.Equal(-1, await GtmEventIndexAsync(session.Page));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);

        await SubmitContactAsync(session, culture);
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        Assert.Equal(0, await CountContactDiagnosticsAsync(session.Page));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);

        await session.Page.Locator("#cookieConsent [data-consent-action='accept']")
            .EvaluateAsync("button => { button.click(); button.click(); }");

        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        await AssertSingleContactDiagnosticAsync(session.Page);
        await session.Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await CountContactDiagnosticsAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
    }

    [Theory]
    [InlineData("en", 1440)]
    [InlineData("th", 390)]
    public async Task PersistedContact_PreviouslyGrantedConsentEmitsOnlyDiagnostic(
        string culture, int width)
    {
        await using var session = await OpenContactFreshAsync(culture, width);
        await session.Page.Locator("#cookieConsent [data-consent-action='accept']").ClickAsync();

        await SubmitContactAsync(session, culture);

        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        await AssertSingleContactDiagnosticAsync(session.Page);
        await session.Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await CountContactDiagnosticsAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
    }

    [Theory]
    [InlineData("en", 390)]
    [InlineData("th", 1440)]
    public async Task PersistedContact_RejectedConsentDiscardsDiagnosticWithoutConversion(
        string culture, int width)
    {
        await using var session = await OpenContactFreshAsync(culture, width);
        await SubmitContactAsync(session, culture);
        Assert.Equal(0, await CountContactDiagnosticsAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));

        await session.Page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        Assert.Equal(0, await CountContactDiagnosticsAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        await session.Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await CountContactDiagnosticsAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);
    }

    [Fact]
    public async Task Reject_DuplicateDispatchDiscardsLeadAndDiagnosticsCannotBecomeConversions()
    {
        await using var session = await OpenFreshAsync(390, submitPersistedQuotation: true);
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);

        await session.Page.Locator("#cookieConsent [data-consent-action='reject']")
            .EvaluateAsync("button => { button.click(); button.click(); }");

        Assert.Equal(1, await CountConsentUpdatesAsync(session.Page, "denied"));
        Assert.Equal(-1, await GtmEventIndexAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        Assert.Contains("maliev_tracking_consent=denied", (await session.Context.CookiesAsync())
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);

        await session.Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await session.Page.Locator("#cookieConsent").CountAsync());
        Assert.Equal(-1, await GtmEventIndexAsync(session.Page));
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        await session.Page.EvaluateAsync(
            "() => window.dataLayer.push({ event: 'service_finder_completed', finder_session_id: crypto.randomUUID() })");
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        await session.Page.GotoAsync(new Uri(fixture.Origin, "/services/3d-printing?culture=en").ToString());
        await session.Page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.Equal(0, await CountLeadEventsAsync(session.Page));
        Assert.DoesNotContain(session.BlockedUrls, IsExternalAnalyticsUrl);
    }

    private static bool IsExternalAnalyticsUrl(string url) =>
        url.Contains("googletagmanager.com", StringComparison.OrdinalIgnoreCase)
        || url.Contains("google-analytics.com", StringComparison.OrdinalIgnoreCase)
        || url.Contains("googleadservices.com", StringComparison.OrdinalIgnoreCase);

    private static Task<int> CountLeadEventsAsync(IPage page) => page.EvaluateAsync<int>(
        "() => dataLayer.filter(item => item.event === 'maliev_lead_submitted' || item.event === 'generate_lead').length");

    private static Task<int> CountContactDiagnosticsAsync(IPage page) => page.EvaluateAsync<int>(
        "() => dataLayer.filter(item => item.event === 'maliev_contact_submitted').length");

    private static async Task AssertSingleContactDiagnosticAsync(IPage page)
    {
        var eventsJson = await page.EvaluateAsync<string>(
            "() => JSON.stringify(dataLayer.filter(item => item.event === 'maliev_contact_submitted'))");
        using var document = JsonDocument.Parse(eventsJson);
        var events = document.RootElement;
        Assert.Equal(JsonValueKind.Array, events.ValueKind);
        Assert.Equal(1, events.GetArrayLength());
        var diagnostic = events[0];
        Assert.Equal(
            ["event", "service", "submission_status"],
            diagnostic.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("maliev_contact_submitted", diagnostic.GetProperty("event").GetString());
        Assert.Equal("general_contact", diagnostic.GetProperty("service").GetString());
        Assert.Equal("persisted", diagnostic.GetProperty("submission_status").GetString());
    }

    private static Task<string> ConsentStateAsync(IPage page, string command) => page.EvaluateAsync<string>(
        "command => { const entry = dataLayer.find(item => item[0] === 'consent' && item[1] === command); return entry?.[2]?.analytics_storage ?? ''; }",
        command);

    private static Task<bool> OptionalStorageDefaultsDeniedAsync(IPage page) => page.EvaluateAsync<bool>(
        "() => { const entry = dataLayer.find(item => item[0] === 'consent' && item[1] === 'default'); "
        + "return Boolean(entry) && ['ad_storage', 'analytics_storage', 'ad_user_data', 'ad_personalization']"
        + ".every(key => entry[2][key] === 'denied'); }");

    private static Task<int> ConsentDefaultIndexAsync(IPage page) => page.EvaluateAsync<int>(
        "() => dataLayer.findIndex(item => item[0] === 'consent' && item[1] === 'default')");

    private static Task<int> GtmEventIndexAsync(IPage page) => page.EvaluateAsync<int>(
        "() => dataLayer.findIndex(item => item.event === 'gtm.js')");

    private static Task<int> CountConsentUpdatesAsync(IPage page, string state) => page.EvaluateAsync<int>(
        "state => dataLayer.filter(item => item[0] === 'consent' && item[1] === 'update' && item[2].analytics_storage === state).length",
        state);

    private async Task<BrowserSession> OpenContactFreshAsync(string culture, int width)
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
        });
        try
        {
            var blockedUrls = new ConcurrentBag<string>();
            var origin = fixture.Origin.ToString().TrimEnd('/');
            await context.RouteAsync("**/*", async route =>
            {
                if (route.Request.Url.StartsWith(origin + "/", StringComparison.Ordinal))
                {
                    await route.ContinueAsync();
                    return;
                }

                blockedUrls.Add(route.Request.Url);
                await route.AbortAsync();
            });
            var page = await context.NewPageAsync();
            await page.GotoAsync(new Uri(fixture.Origin, $"/Contact?culture={culture}").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(page.Locator("#contact-us")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#cookieConsent")).ToBeVisibleAsync();
            return new BrowserSession(context, page, blockedUrls);
        }
        catch (Exception primary)
        {
            try
            {
                await context.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                primary.Data["ContactContextCleanupFailure"] = cleanup;
            }

            throw;
        }
    }

    private async Task SubmitContactAsync(BrowserSession session, string culture)
    {
        var before = fixture.ContactClient.SubmissionCount;
        var redirectedDocument = new TaskCompletionSource<IPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDomContentLoaded(object? sender, IPage page) => redirectedDocument.TrySetResult(page);
        session.Page.DOMContentLoaded += OnDomContentLoaded;
        try
        {
            var response = await session.Page.RunAndWaitForResponseAsync(
                () => session.Page.Locator("#contact-us").EvaluateAsync("""
                    form => {
                        const fields = {
                            FirstName: 'Browser',
                            LastName: 'Contact',
                            Email: 'synthetic-contact@example.test',
                            Country: 'Thailand',
                            Message: 'Synthetic contact measurement test.',
                            'g-recaptcha-response': 'fixture-token',
                        };
                        for (const [name, value] of Object.entries(fields)) form.elements.namedItem(name).value = value;
                        form.submit();
                    }
                    """),
                candidate => candidate.Request.Method == "POST"
                    && new Uri(candidate.Url).AbsolutePath.Equals("/Contact", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(302, response.Status);
            Assert.Same(session.Page, await redirectedDocument.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(before + 1, fixture.ContactClient.SubmissionCount);
            Assert.EndsWith($"/Contact?culture={culture}", session.Page.Url, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            session.Page.DOMContentLoaded -= OnDomContentLoaded;
        }
    }

    private async Task<BrowserSession> OpenFreshAsync(int width, bool submitPersistedQuotation = false)
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
        });
        var blockedUrls = new ConcurrentBag<string>();
        var origin = fixture.Origin.ToString().TrimEnd('/');
        await context.RouteAsync("**/*", async route =>
        {
            if (route.Request.Url.StartsWith(origin + "/", StringComparison.Ordinal))
            {
                await route.ContinueAsync();
                return;
            }

            blockedUrls.Add(route.Request.Url);
            await route.AbortAsync();
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(fixture.Origin, "/Quotation?culture=en&measurement=preflight").ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.Load });
        if (submitPersistedQuotation)
        {
            var before = fixture.QuotationClient.SubmissionCount;
            var post = await page.RunAndWaitForResponseAsync(
                () => page.Locator("#quotation-form").EvaluateAsync(@"form => {
                    const fields = {
                        FirstName: 'Browser',
                        LastName: 'Measurement',
                        Email: 'browser-measurement@example.test',
                        Country: 'Thailand',
                        Message: 'Synthetic contact lifecycle test.',
                        'g-recaptcha-response': 'fixture-token',
                    };
                    for (const [name, value] of Object.entries(fields)) form.elements.namedItem(name).value = value;
                    form.submit();
                }"),
                response => response.Request.Method == "POST"
                    && new Uri(response.Url).AbsolutePath.Equals("/Quotation", StringComparison.OrdinalIgnoreCase));
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var errors = await page.Locator("#quotation-form").InnerTextAsync();
            Assert.True(fixture.QuotationClient.SubmissionCount == before + 1,
                $"Quotation POST did not persist: HTTP {post.Status}, URL {page.Url}, form {errors}");
            Assert.EndsWith("/Quotation?culture=en", page.Url, StringComparison.OrdinalIgnoreCase);
        }

        return new BrowserSession(context, page, blockedUrls);
    }

    private sealed class BrowserSession(IBrowserContext context, IPage page, ConcurrentBag<string> blockedUrls)
        : IAsyncDisposable
    {
        public IBrowserContext Context { get; } = context;
        public IPage Page { get; } = page;
        public ConcurrentBag<string> BlockedUrls { get; } = blockedUrls;
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}

public sealed class MeasurementLifecycleBrowserFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? factory;
    private IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = null!;
    public Uri Origin { get; private set; } = null!;
    public RecordingQuotationClient QuotationClient { get; } = new();
    public RecordingContactClient ContactClient { get; } = new();

    public async Task InitializeAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Origin = new Uri($"http://127.0.0.1:{port}");

        factory = new MeasurementTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), QuotationClient, ContactClient);
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = Origin,
        });
        using var response = await client.GetAsync("/Quotation?culture=en");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        playwright?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }
    }

    public sealed class RecordingContactClient : IContactClient
    {
        private int submissionCount;
        public int SubmissionCount => Volatile.Read(ref submissionCount);

        public Task<ContactSubmissionResult> SubmitAsync(
            ContactSubmission submission, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref submissionCount);
            return Task.FromResult(new ContactSubmissionResult(42, true, true));
        }
    }

    public sealed class RecordingQuotationClient : IQuotationClient
    {
        private int submissionCount;
        public int SubmissionCount => Volatile.Read(ref submissionCount);
        public Task<QuotationRequestResult> CreateRequestAsync(
            QuotationRequestSubmission submission, string idempotencyKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref submissionCount);
            return Task.FromResult(new QuotationRequestResult(
                42, true, true, "request-42", Guid.Parse("11111111-2222-3333-4444-555555555555")));
        }
    }

    private sealed class MeasurementTestingWebApplicationFactory(
        string contentRoot, RecordingQuotationClient quotationClient, RecordingContactClient contactClient)
        : TestingWebApplicationFactory(contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
                services.RemoveAll<IQuotationClient>();
                services.AddSingleton<IQuotationClient>(quotationClient);
                services.RemoveAll<IContactClient>();
                services.AddSingleton<IContactClient>(contactClient);
                services.RemoveAll<IContactTrustedCustomerLoader>();
                services.AddSingleton<IContactTrustedCustomerLoader, AnonymousContactCustomerLoader>();
                services.RemoveAll<IQuotationFileClient>();
                services.AddSingleton<IQuotationFileClient, StubQuotationFileClient>();
                services.RemoveAll<IAntiBotVerifier>();
                services.AddSingleton<IAntiBotVerifier, StubAntiBotVerifier>();
                services.RemoveAll<INotificationClient>();
                services.AddSingleton<INotificationClient, StubNotificationClient>();
            });
        }
    }

    private sealed class AnonymousContactCustomerLoader : IContactTrustedCustomerLoader
    {
        public Task<ContactTrustedCustomerLoadResult> LoadAsync(
            HttpContext context, IReadOnlyList<Country> countries, CancellationToken cancellationToken) =>
            Task.FromResult(new ContactTrustedCustomerLoadResult(null, false, true));
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)], true));
    }

    private sealed class StubAntiBotVerifier : IAntiBotVerifier
    {
        public Task<bool> VerifyAsync(string? token, string expectedAction, CancellationToken cancellationToken) =>
            Task.FromResult(token == "fixture-token" && expectedAction == "submit");
    }

    private sealed class StubNotificationClient : INotificationClient
    {
        public Task<NotificationResult> SendAsync(
            NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationResult(true, true, true));
    }

    private sealed class StubQuotationFileClient : IQuotationFileClient
    {
        public Task<QuotationFileResult> UploadAndLinkAsync(
            int requestId, Guid submissionId, IReadOnlyList<QuotationUpload> files, CancellationToken cancellationToken) =>
            Task.FromResult(new QuotationFileResult(true, true, true, false));
    }
}

[CollectionDefinition(Name)]
public sealed class MeasurementLifecycleBrowserCollection : ICollectionFixture<MeasurementLifecycleBrowserFixture>
{
    public const string Name = "Measurement lifecycle browser";
}
