using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual Web Program with fixture-owned peers, clock, health check and Razor failure filter.</summary>
public sealed class PrivateRequestObservationConsumerTests
{
    private const string Diagnostic = "/internal/diagnostics/observability";
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string Sensitive = "private-consumer-sentinel";
    private const string ForgedIncident = "fixture-only-forged-incident";

    [Theory]
    [InlineData(null, "GET", "valid", null)]
    [InlineData(null, "GET", "valid", "127.0.0.1")]
    [InlineData("203.0.113.7", "GET", "valid", null)]
    [InlineData("203.0.113.7", "GET", "valid", "127.0.0.1")]
    [InlineData("127.0.0.1", "POST", "valid", null)]
    [InlineData("127.0.0.1", "GET", "missing", null)]
    [InlineData("127.0.0.1", "GET", "multiple", null)]
    [InlineData("127.0.0.1", "GET", "malformed", null)]
    [InlineData("127.0.0.1", "GET", "hyphenated", null)]
    [InlineData("127.0.0.1", "GET", "braced", null)]
    public async Task ActualProgram_InvalidAdmissionIsQuietAndDoesNotExposeIdentity(
        string? peer, string method, string kind, string? forwarded)
    {
        using var factory = new ObservationFactory(peer);
        using var client = factory.CreateClient();
        factory.Records.Clear();
        using var response = await SendAsync(client, Diagnostic, method, kind, forwarded);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertDeniedAsync(response);
        Assert.Empty(factory.Records.Failures);
        AssertNoRequestLogging(factory.Records.Snapshot);
        AssertPrivate(factory.Records.Snapshot);
        if (peer == "127.0.0.1" && method == "GET")
        {
            using var admitted = await SendAsync(client, Diagnostic);
            await AssertSyntheticAsync(admitted, AssertHealthMarker(admitted));
        }
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task ActualProgram_SyntheticFailureUsesFrameworkAndNeverCreatesCustomerIncident(string peer)
    {
        using var factory = new ObservationFactory(peer, overwriteSynthetic: true);
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/web/liveness");
        var instance = AssertHealthMarker(health);
        factory.Records.Clear();
        using var response = await SendAsync(client, Diagnostic + "?token=" + Sensitive, kind: "uppercase");
        await AssertSyntheticAsync(response, instance);
        Assert.Equal(1, factory.SyntheticOverwrites);
        AssertSyntheticEvents(factory.Records.Snapshot);
        AssertNoRequestLogging(factory.Records.Snapshot);
        AssertPrivate(factory.Records.Snapshot);
    }

    [Fact]
    public async Task ActualProgram_ThrottleIsSingletonResetsAtOneMinuteAndIsIndependentAcrossHosts()
    {
        using var factory = new ObservationFactory("127.0.0.1");
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        using var first = await SendAsync(firstClient, Diagnostic);
        var instance = AssertHealthMarker(first);
        await AssertSyntheticAsync(first, instance);
        factory.Records.Clear();
        factory.Clock.Advance(TimeSpan.FromSeconds(59));
        using var repeated = await SendAsync(secondClient, Diagnostic);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeated.StatusCode);
        await AssertDeniedAsync(repeated);
        Assert.Empty(factory.Records.Failures);
        AssertNoRequestLogging(factory.Records.Snapshot);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var reset = await SendAsync(secondClient, Diagnostic);
        await AssertSyntheticAsync(reset, instance);
        AssertSyntheticEvents(factory.Records.Snapshot);
        using var independent = new ObservationFactory("127.0.0.1");
        using var independentClient = independent.CreateClient();
        using var independentResponse = await SendAsync(independentClient, Diagnostic);
        var independentInstance = AssertHealthMarker(independentResponse);
        Assert.NotEqual(instance, independentInstance);
        await AssertSyntheticAsync(independentResponse, independentInstance);
    }

    [Theory]
    [InlineData("/web/liveness", false)]
    [InlineData("/web/readiness", false)]
    [InlineData("/web/readiness", true)]
    public async Task ActualProgram_RegisteredHealthyRoutesAreQuietAndRestoreLateHeaders(string route, bool overwrite)
    {
        using var factory = new ObservationFactory("127.0.0.1", overwriteHealth: overwrite);
        using var client = factory.CreateClient();
        factory.Records.Clear();
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHealthMarker(response);
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.False(response.Headers.Contains("X-Incident-Id"));
        Assert.Equal(overwrite ? 1 : 0, factory.HealthOverwrites);
        Assert.Empty(factory.Records.Failures);
        AssertNoRequestLogging(factory.Records.Snapshot);
    }

    [Fact]
    public async Task ActualProgram_OrdinaryRazorThrowReexecutesErrorWithExactlyOneSanitizedWebIncident()
    {
        using var factory = new ObservationFactory("127.0.0.1", ordinaryFailure: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        factory.Records.Clear();
        using var response = await client.GetAsync("/contact?token=" + Sensitive + "&culture=en");
        Assert.Equal(1, factory.PageFilter.Throws);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var incident = Assert.Single(response.Headers.GetValues("X-Incident-Id"));
        Assert.Matches("^[a-f0-9]{32}$", incident);
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains(incident, document, StringComparison.Ordinal);
        Assert.DoesNotContain(Sensitive, document, StringComparison.Ordinal);
        Assert.Equal(1, factory.PageFilter.Throws);
        Assert.Equal(1, factory.PageFilter.ErrorExecutions);
        var webIncident = Assert.Single(factory.Records.Snapshot, entry =>
            entry.Category == "Legacy.Maliev.Web.Middleware.ErrorIncidentMiddleware"
            && entry.Fields.GetValueOrDefault("EventName") as string == "UnhandledRequestFailure");
        Assert.Equal(LogLevel.Critical, webIncident.Level);
        Assert.Equal(incident, webIncident.Fields["IncidentId"]);
        Assert.DoesNotContain(factory.Records.Snapshot, entry =>
            entry.Fields.GetValueOrDefault("EventName") as string == "HandledOperationFailure");
        Assert.DoesNotContain(factory.Records.Snapshot, entry => entry.HasSyntheticScope);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        AssertPrivate(factory.Records.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualProgram_OrdinaryHandledFailureReexecutesWithoutInventingWebIncident(bool forgedIncidentHeader)
    {
        using var factory = new ObservationFactory("127.0.0.1", handledFailure: true,
            forgedIncidentHeader: forgedIncidentHeader);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        factory.Records.Clear();
        using var response = await client.GetAsync("/contact?culture=en");
        Assert.Equal(1, factory.PageFilter.HandledExecutions);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        if (forgedIncidentHeader)
            Assert.Equal(ForgedIncident, Assert.Single(response.Headers.GetValues("X-Incident-Id")));
        else
            Assert.False(response.Headers.Contains("X-Incident-Id"));
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.Equal(1, factory.PageFilter.ErrorExecutions);
        Assert.Equal(0, factory.PageFilter.Throws);
        Assert.DoesNotContain(factory.Records.Snapshot, entry =>
            entry.Category == "Legacy.Maliev.Web.Middleware.ErrorIncidentMiddleware");
        var observed = Assert.Single(factory.Records.Snapshot, entry =>
            entry.Fields.GetValueOrDefault("EventName") as string == "HandledOperationFailure");
        Assert.Equal(LogLevel.Error, observed.Level);
        Assert.DoesNotContain(factory.Records.Snapshot, entry => entry.HasSyntheticScope);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path,
        string method = "GET", string kind = "valid", string? forwarded = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var supplied = kind switch
        {
            "malformed" => "not-guid-n",
            "hyphenated" => "01234567-89ab-cdef-0123-456789abcdef",
            "braced" => "{01234567-89ab-cdef-0123-456789abcdef}",
            "uppercase" => Nonce.ToUpperInvariant(),
            _ => Nonce,
        };
        if (kind != "missing") request.Headers.TryAddWithoutValidation("X-Maliev-Diagnostic-Id",
            kind == "multiple" ? [Nonce, Nonce] : new[] { supplied });
        if (forwarded is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwarded);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Sensitive);
        request.Headers.TryAddWithoutValidation("Cookie", "fixture=" + Sensitive);
        return await client.SendAsync(request);
    }

    private static async Task AssertDeniedAsync(HttpResponseMessage response)
    {
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Null(response.Headers.Location);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.False(response.Headers.Contains("X-Incident-Id"));
    }

    private static string AssertHealthMarker(HttpResponseMessage response)
    {
        var instance = Assert.Single(response.Headers.GetValues("X-Maliev-Health-Instance"));
        Assert.Matches("^[a-f0-9]{32}$", instance);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return instance;
    }

    private static async Task AssertSyntheticAsync(HttpResponseMessage response, string instance)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal(instance, AssertHealthMarker(response));
        Assert.Equal(Nonce, Assert.Single(response.Headers.GetValues("X-Maliev-Diagnostic-Id")));
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.False(response.Headers.Contains("X-Incident-Id"));
        Assert.Null(response.Headers.Location);
    }

    private static void AssertSyntheticEvents(Record[] records)
    {
        var failures = records.Where(entry => entry.Level >= LogLevel.Warning).ToArray();
        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Error, LogLevel.Critical }, failures.Select(entry => entry.Level));
        Assert.Equal("ObservabilityPipelineProbe", failures[0].Fields["EventName"]);
        Assert.Equal("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", failures[1].Category);
        Assert.Equal(1, failures[1].EventId.Id);
        Assert.Equal("UnhandledException", failures[1].EventId.Name);
        Assert.Equal("Maliev.Aspire.ServiceDefaults.Diagnostics.ProductionObservabilityDiagnosticException", failures[1].ExceptionType);
        Assert.Equal("UnhandledRequestFailure", failures[2].Fields["EventName"]);
        Assert.All(failures, entry => Assert.True(entry.HasSyntheticScope));
        Assert.DoesNotContain(records, entry => entry.Category == "Legacy.Maliev.Web.Middleware.ErrorIncidentMiddleware");
    }

    private static void AssertNoRequestLogging(Record[] records) => Assert.DoesNotContain(records,
        entry => entry.Category.EndsWith("RequestLoggingMiddleware", StringComparison.Ordinal));

    private static void AssertPrivate(Record[] records) => Assert.DoesNotContain(Sensitive,
        JsonSerializer.Serialize(records), StringComparison.Ordinal);

    private sealed class ObservationFactory(string? peer, bool overwriteSynthetic = false,
        bool overwriteHealth = false, bool ordinaryFailure = false, bool handledFailure = false,
        bool forgedIncidentHeader = false) : TestingWebApplicationFactory
    {
        public RecordingProvider Records { get; } = new();
        public ObservationClock Clock { get; } = new();
        public FailurePageFilter PageFilter { get; } = new(ordinaryFailure, handledFailure, forgedIncidentHeader);
        public bool ShouldOverwriteHealth => overwriteHealth;
        public int SyntheticOverwrites;
        public int HealthOverwrites;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Program snapshots route switches during builder creation. Use the
            // same host-setting boundary as the retained Razor route regressions.
            builder.UseSetting("BlazorRouting:Contact", ordinaryFailure || handledFailure ? "false" : "true");
            builder.UseSetting("BlazorRouting:Error", ordinaryFailure || handledFailure ? "false" : "true");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Information",
                    ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
                    ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.7",
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<ILoggerProvider>(Records);
                services.AddSingleton<IStartupFilter>(new PeerFilter(peer));
                services.AddHttpContextAccessor();
                services.AddHealthChecks().AddCheck("owned-observation-control", new HeaderHealthCheck(this));
                services.Configure<RazorPagesOptions>(options => options.Conventions.ConfigureFilter(PageFilter));
                if (overwriteSynthetic) Records.OnFrameworkError = () =>
                {
                    var context = Services.GetRequiredService<IHttpContextAccessor>().HttpContext
                        ?? throw new InvalidOperationException("Actual framework event must carry its request context.");
                    context.Response.OnStarting(() =>
                    {
                        Interlocked.Increment(ref SyntheticOverwrites);
                        context.Response.Headers["X-Maliev-Health-Instance"] = "fixture-overwrite";
                        context.Response.Headers["X-Maliev-Diagnostic-Id"] = "fixture-overwrite";
                        context.Response.Headers.CacheControl = "public";
                        return Task.CompletedTask;
                    });
                };
            });
        }
    }

    private sealed class PeerFilter(string? peer) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
                await following(context);
            });
            next(app);
        };
    }

    private sealed class HeaderHealthCheck(ObservationFactory owner) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext,
            CancellationToken cancellationToken = default)
        {
            if (owner.HealthOverwrites == 0)
            {
                // Register after the real observer and actual endpoint selection, not in the outer fixture filter.
                var context = owner.Services.GetRequiredService<IHttpContextAccessor>().HttpContext;
                if (context is not null && owner.ShouldOverwriteHealth)
                    context.Response.OnStarting(() =>
                    {
                        Interlocked.Increment(ref owner.HealthOverwrites);
                        context.Response.Headers["X-Maliev-Health-Instance"] = "fixture-overwrite";
                        context.Response.Headers.CacheControl = "public";
                        return Task.CompletedTask;
                    });
            }
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }

    private sealed class FailurePageFilter(bool enabled, bool handled, bool forgedIncidentHeader) : IAsyncPageFilter
    {
        public int Throws;
        public int HandledExecutions;
        public int ErrorExecutions;
        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            if (enabled && context.HttpContext.Request.Path.Equals(new PathString("/contact")))
            {
                Interlocked.Increment(ref Throws);
                throw new InvalidOperationException(Sensitive);
            }
            if (handled && context.HttpContext.Request.Path.Equals(new PathString("/contact")))
            {
                Interlocked.Increment(ref HandledExecutions);
                // A fixture response header is not server-owned exception provenance.
                if (forgedIncidentHeader) context.HttpContext.Response.Headers["X-Incident-Id"] = ForgedIncident;
                context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
                return;
            }
            if ((enabled || handled) && context.HttpContext.Request.Path.Equals(new PathString("/Error")))
                Interlocked.Increment(ref ErrorExecutions);
            await next();
        }
    }

    private sealed class ObservationClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed record Record(string Category, LogLevel Level, EventId EventId,
        Dictionary<string, object?> Fields, string? ExceptionType, string Message, Dictionary<string, object?>[] Scopes)
    {
        public bool HasSyntheticScope => Scopes.Any(scope => scope.GetValueOrDefault("Synthetic") is true
            && scope.GetValueOrDefault("DiagnosticId") as string == Nonce);
    }

    private sealed class RecordingProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<Record> records = new();
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        public Action? OnFrameworkError { get; set; }
        public Record[] Snapshot => records.ToArray();
        public Record[] Failures => Snapshot.Where(entry => entry.Level >= LogLevel.Warning).ToArray();
        public void Clear() => records.Clear();
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void Dispose() { }
        private sealed class Recorder(RecordingProvider owner, string category) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(pair => pair.Key, pair => pair.Value) : new Dictionary<string, object?>();
                var snapshot = new List<Dictionary<string, object?>>();
                owner.scopes.ForEachScope((scope, list) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                        list.Add(pairs.ToDictionary(pair => pair.Key, pair => pair.Value));
                }, snapshot);
                owner.records.Enqueue(new(category, logLevel, eventId, fields, exception?.GetType().FullName,
                    formatter(state, exception), snapshot.ToArray()));
                if (category == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware" && eventId.Id == 1)
                    owner.OnFrameworkError?.Invoke();
            }
        }
    }
}
