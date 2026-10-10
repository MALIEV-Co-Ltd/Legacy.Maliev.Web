using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Maliev.Aspire.ServiceDefaults.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class CountrySourceFailureObservationTests
{
    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, true)]
    public async Task NonretryTerminalStatusIsObservedOnceWithoutDuplicateCountryWarning(int status, bool available)
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(status)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await fixture.Country.GetCountriesAsync(deadline.Token);

        Assert.Equal(available, result.ServiceAvailable);
        Assert.Empty(result.Value!);
        Assert.Equal(1, fixture.Transport.Calls);
        fixture.AssertFailure(status);
    }

    [Fact]
    public async Task SuccessfulCountryResponseRetainsConsumerValuesAndStaysQuiet()
    {
        var expected = new Country(764, "Thailand", "Asia", "TH", "TH", "THA", null, null);
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = JsonContent.Create(new[] { expected }) }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await fixture.Country.GetCountriesAsync(deadline.Token);
        Assert.True(result.ServiceAvailable);
        Assert.Equal(expected, Assert.Single(result.Value!));
        Assert.Equal(1, fixture.Transport.Calls);
        fixture.AssertQuiet();
    }

    [Fact]
    public async Task Recovered503RetryDoesNotProduceTerminalObservation()
    {
        var calls = 0;
        using var fixture = new Fixture((_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1
            ? Response(503) : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<Country>()) }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await fixture.Country.GetCountriesAsync(deadline.Token);
        Assert.True(result.ServiceAvailable);
        Assert.Equal(2, fixture.Transport.Calls);
        fixture.AssertQuiet();
    }

    [Fact]
    public async Task Exhausted503RetriesProduceOneTerminalObservation()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(503)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await fixture.Country.GetCountriesAsync(deadline.Token);
        Assert.False(result.ServiceAvailable);
        Assert.Equal(4, fixture.Transport.Calls); // Existing standard policy: initial attempt and three retries.
        fixture.AssertFailure(503);
    }

    [Fact]
    public async Task CallerCancellationPropagatesAndRemainsQuiet()
    {
        using var fixture = new Fixture(Block);
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = fixture.Country.GetCountriesAsync(caller.Token);
        try
        {
            await fixture.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            fixture.AssertQuiet();
        }
        finally
        {
            caller.Cancel();
            await SettleAsync(pending);
        }
    }

    [Fact]
    public async Task NativeClientDeadlineReturnsUnavailableWithOneSafeObservation()
    {
        using var fixture = new Fixture(Block, TimeSpan.FromMilliseconds(100));
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = fixture.Country.GetCountriesAsync(caller.Token);
        try
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(caller.IsCancellationRequested);
            Assert.False(result.ServiceAvailable);
            Assert.Equal(1, fixture.Transport.Calls);
            fixture.AssertFailure(null);
        }
        finally
        {
            caller.Cancel();
            await SettleAsync(pending);
        }
    }

    [Fact]
    public async Task SuccessfulHeadersWithNativeBodyDeadlineRetainBufferingAndSafeFallbackWarning()
    {
        using var body = new CancellationBoundContent();
        using var fixture = new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = body }), TimeSpan.FromMilliseconds(100));
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = fixture.Country.GetCountriesAsync(caller.Token);
        try
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(body.Entered.Task.IsCompletedSuccessfully);
            Assert.True(body.CancellationSeen);
            Assert.False(caller.IsCancellationRequested);
            Assert.False(result.ServiceAvailable);
            Assert.Equal(1, fixture.Transport.Calls);
            // The handler returned successful headers. Body buffering cannot prove a handler-owned deadline.
            Assert.Empty(fixture.Events.Failures);
            var warning = Assert.Single(fixture.Events.CountryWarnings);
            Assert.Equal("Country service was unavailable while loading the contact form.", warning);
            Assert.Null(Assert.Single(fixture.Events.CountryWarningExceptions));
            Assert.DoesNotContain("private-", warning, StringComparison.Ordinal);
        }
        finally
        {
            caller.Cancel();
            await SettleAsync(pending);
            await body.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task UnexpectedExceptionIsObservedWithoutReplacementOrPrivateExport()
    {
        var failure = new InvalidOperationException("private-exception body bearer customer filename.stl");
        failure.Data["private-data"] = "private-customer";
        using var fixture = new Fixture((_, _) => Task.FromException<HttpResponseMessage>(failure));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var actual = await Record.ExceptionAsync(() => fixture.Country.GetCountriesAsync(deadline.Token));
        Assert.Same(failure, actual);
        Assert.Equal(1, fixture.Transport.Calls);
        fixture.AssertFailure(null);
    }

    [Fact]
    public async Task RealFactoryPreservesActiveNamedResilienceDeadlinesAndNoAddedAuthorization()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(503)), probeNamedPipeline: true);
        using var countries = fixture.Factory.CreateClient("countries");
        using var careers = fixture.Factory.CreateClient("careers");
        // Standard resilience overrides the configured ten-second HttpClient timeout.
        // Freeze the actual existing pipeline deadlines rather than changing production registration.
        Assert.Equal(Timeout.InfiniteTimeSpan, countries.Timeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, careers.Timeout);
        var options = fixture.Scope.ServiceProvider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>();
        var defaults = new HttpStandardResilienceOptions();
        foreach (var name in new[] { "countries-standard", "careers-standard" })
        {
            Assert.Equal(1, options.Get(name).Retry.MaxRetryAttempts);
            Assert.Equal(defaults.AttemptTimeout.Timeout, options.Get(name).AttemptTimeout.Timeout);
            Assert.Equal(defaults.TotalRequestTimeout.Timeout, options.Get(name).TotalRequestTimeout.Timeout);
        }
        // A typo resolves fallback options, but cannot pass the named retry marker or actual transport proof.
        Assert.Equal(defaults.Retry.MaxRetryAttempts, options.Get("misnamed-standard").Retry.MaxRetryAttempts);
        Assert.NotEqual(1, options.Get("misnamed-standard").Retry.MaxRetryAttempts);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var countryResponse = await countries.GetAsync("Countries", deadline.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, countryResponse.StatusCode);
        Assert.Equal(2, fixture.Transport.Calls);
        using var careerResponse = await careers.GetAsync("Careers", deadline.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, careerResponse.StatusCode);
        Assert.Equal(4, fixture.Transport.Calls);
        Assert.Null(countries.DefaultRequestHeaders.Authorization);
        Assert.Null(careers.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task UnselectedRealCareerConsumerDoesNotEmitCountryFailureObservation()
    {
        using var fixture = new Fixture((_, _) => Task.FromResult(Response(404)));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var career = fixture.Scope.ServiceProvider.GetRequiredService<ICareerClient>();
        var result = await career.GetOfferAsync(123, deadline.Token);
        Assert.True(result.ServiceAvailable);
        Assert.Null(result.Value);
        Assert.Equal(1, fixture.Transport.Calls);
        fixture.AssertQuiet();
    }

    private static HttpResponseMessage Response(int status) => new((HttpStatusCode)status)
    { Content = new StringContent("private-response-body customer filename.stl") };

    private static async Task<HttpResponseMessage> Block(HttpRequestMessage request, CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("Unreachable after cancellation.");
    }

    private static async Task SettleAsync(Task pending)
    {
        try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) { }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Capture Events = new();
        internal readonly Transport Transport;
        private readonly IHost host;
        internal IServiceScope Scope { get; }
        internal IHttpClientFactory Factory => host.Services.GetRequiredService<IHttpClientFactory>();
        internal ICountryClient Country => Scope.ServiceProvider.GetRequiredService<ICountryClient>();

        internal Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null, bool probeNamedPipeline = false)
        {
            Transport = new(send);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Logging.AddProvider(Events);
            builder.Configuration["Services:Country"] = "https://private-country-host.example/";
            builder.Configuration["Services:Career"] = "https://private-career-host.example/";
            builder.Services.AddLegacyServiceClients(builder.Configuration);
            if (probeNamedPipeline)
            {
                // Test-only marker: leave attempt/total deadlines intact and prove these names feed actual clients.
                builder.Services.Configure<HttpStandardResilienceOptions>("countries-standard", options => options.Retry.MaxRetryAttempts = 1);
                builder.Services.Configure<HttpStandardResilienceOptions>("careers-standard", options => options.Retry.MaxRetryAttempts = 1);
            }
            // Keep the real standard resilience pipeline; only its retry scheduling is shortened for this controlled transport.
            builder.Services.PostConfigureAll<HttpStandardResilienceOptions>(options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
                options.Retry.DelayGenerator = _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero);
            });
            builder.Services.AddHttpClient("countries", client =>
            {
                if (timeout is { } shortened) client.Timeout = shortened;
            }).ConfigurePrimaryHttpMessageHandler(() => Transport);
            builder.Services.AddHttpClient("careers").ConfigurePrimaryHttpMessageHandler(() => Transport);
            host = builder.Build();
            Scope = host.Services.CreateScope();
        }

        internal void AssertQuiet()
        {
            Assert.Empty(Events.Failures);
            Assert.Empty(Events.CountryWarnings);
        }

        internal void AssertFailure(int? status)
        {
            var entry = Assert.Single(Events.Failures);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Null(entry.Exception);
            Assert.Equal("CountryService", entry.Fields["Dependency"]);
            Assert.Equal("Countries.Get", entry.Fields["Operation"]);
            if (status is { } code) Assert.Equal(code, entry.Fields["StatusCode"]);
            else Assert.False(entry.Fields.ContainsKey("StatusCode"));
            Assert.Empty(Events.CountryWarnings);
            using var document = JsonDocument.Parse(entry.Wire);
            Assert.Equal(5101, document.RootElement.GetProperty("eventId").GetInt32());
            Assert.Equal("CountryService", document.RootElement.GetProperty("Dependency").GetString());
            Assert.Equal("Countries.Get", document.RootElement.GetProperty("Operation").GetString());
            Assert.Equal("ERROR", document.RootElement.GetProperty("severity").GetString());
            Assert.DoesNotContain("private-", entry.Wire, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("filename.stl", entry.Wire, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", entry.Wire, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            try { Scope.Dispose(); }
            finally
            {
                try { host.Dispose(); }
                finally { Transport.Dispose(); }
            }
        }
    }

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Assert.Null(request.Headers.Authorization);
            Entered.TrySetResult();
            return send(request, cancellationToken);
        }
    }

    private sealed record Entry(LogLevel Level, Exception? Exception, Dictionary<string, object?> Fields, string Wire);

    private sealed class CancellationBoundContent : HttpContent
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool CancellationSeen { get; private set; }
        internal CancellationBoundContent() => Headers.ContentType = new("application/json");
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Native buffering must supply its cancellation token.");
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally
            {
                CancellationSeen = cancellationToken.IsCancellationRequested;
                Finished.TrySetResult();
            }
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class Capture : ILoggerProvider
    {
        internal ConcurrentQueue<Entry> Failures { get; } = new();
        internal ConcurrentQueue<string> CountryWarnings { get; } = new();
        internal ConcurrentQueue<Exception?> CountryWarningExceptions { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);
        public void Dispose() { }
        private sealed class Logger(string category, Capture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category == typeof(CountryClient).FullName && level == LogLevel.Warning)
                {
                    owner.CountryWarnings.Enqueue(formatter(state, exception));
                    owner.CountryWarningExceptions.Enqueue(exception);
                }
                if (id.Id != 5101 || id.Name != "DependencyRequestFailure") return;
                var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state)
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                var entry = new LogEntry<TState>(level, category, id, state, exception, (_, _) => "private-rendered-text");
                using var writer = new StringWriter();
                new PrivateFailureConsoleFormatter().Write(in entry, null, writer);
                owner.Failures.Enqueue(new(level, exception, fields, writer.ToString()));
            }
        }
    }
}
