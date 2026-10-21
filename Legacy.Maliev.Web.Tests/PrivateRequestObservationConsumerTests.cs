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
        var formatted = AssertFormattedEvent(factory.Records.Formatted, "UnhandledRequestFailure", "CRITICAL");
        Assert.Equal(incident, formatted.GetProperty("IncidentId").GetString());
        Assert.Equal(webIncident.Fields["OccurredAtUtc"], formatted.GetProperty("OccurredAtUtc").GetString());
        Assert.Equal(typeof(InvalidOperationException).FullName, formatted.GetProperty("exceptionType").GetString());
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
        AssertFormattedEvent(factory.Records.Formatted, "HandledOperationFailure", "ERROR");
    }


    /// <summary>The actual Program selects the private native console sink without dropping metrics or traces.</summary>
    [Theory]
    [InlineData(LogLevel.Warning, "WARNING")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "CRITICAL")]
    public void ActualProgram_PrivateConsoleFormatsOnlySafeFailureMetadata(LogLevel level, string severity)
    {
        using var factory = new ObservationFactory("127.0.0.1", consoleOverride: true);
        using var client = factory.CreateClient();
        var selected = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>>().Value.FormatterName;
        Assert.Equal(Maliev.Aspire.ServiceDefaults.Logging.PrivateFailureConsoleFormatter.FormatterName, selected);
        Assert.Same(factory.Records.Formatter, Assert.Single(
            factory.Services.GetServices<Microsoft.Extensions.Logging.Console.ConsoleFormatter>(),
            formatter => formatter.Name == selected));
        var providers = factory.Services.GetServices<ILoggerProvider>().ToArray();
        Assert.Equal(2, providers.Length);
        Assert.Contains(providers, provider => provider is Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider);
        Assert.Contains(providers, provider => ReferenceEquals(provider, factory.Records));
        Assert.DoesNotContain(providers, provider => provider.GetType().Namespace?.StartsWith("OpenTelemetry", StringComparison.Ordinal) == true);
        Assert.NotNull(factory.Services.GetRequiredService<OpenTelemetry.Metrics.MeterProvider>());
        Assert.NotNull(factory.Services.GetRequiredService<OpenTelemetry.Trace.TracerProvider>());

        using var activity = new System.Diagnostics.Activity("WebPrivacyControl")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C).Start();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Web.PrivacyControl");
        var fields = new Dictionary<string, object?>
        {
            ["EventName"] = "BusinessOperationFailure",
            ["Operation"] = "Quotation",
            ["StatusCode"] = 503,
            ["Code"] = "quote_invalid",
            ["Path"] = "/uploads/" + Sensitive,
            ["Authorization"] = Sensitive,
            ["Cookie"] = Sensitive,
            ["Payload"] = new OpaqueCustomerPayload(),
            ["{OriginalFormat}"] = Sensitive,
            ["exceptionType"] = "ForgedCustomerException",
            ["sourceLocation"] = Sensitive,
            ["service"] = Sensitive,
            ["deploymentVersion"] = Sensitive,
            ["traceId"] = new string('1', 32),
            ["spanId"] = new string('2', 16),
        };
        using var scope = logger.BeginScope(fields);
        factory.Records.Clear();
        logger.Log(level, new EventId(79), fields, new InvalidOperationException(Sensitive),
            static (_, _) => Sensitive);
        var output = Assert.Single(factory.Records.Formatted);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal(severity, root.GetProperty("severity").GetString());
        Assert.Equal("BusinessOperationFailure", root.GetProperty("EventName").GetString());
        Assert.Equal("Quotation", root.GetProperty("Operation").GetString());
        Assert.Equal(503, root.GetProperty("StatusCode").GetInt32());
        Assert.Equal("quote_invalid", root.GetProperty("Code").GetString());
        Assert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("exceptionType").GetString());
        Assert.Equal(activity.TraceId.ToHexString(), root.GetProperty("traceId").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), root.GetProperty("spanId").GetString());
        foreach (var name in new[] { "Path", "Authorization", "Cookie", "Payload", "{OriginalFormat}", "State", "Scopes", "Message", "TraceId", "SpanId" })
            Assert.False(root.TryGetProperty(name, out _));
        Assert.DoesNotContain(Sensitive, output, StringComparison.Ordinal);
        Assert.DoesNotContain("ForgedCustomerException", output, StringComparison.Ordinal);
        Assert.Equal(root.EnumerateObject().Count(), root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A provider-specific Trace override cannot make the selected formatter emit success payloads.</summary>
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    public void ActualProgram_PrivateConsoleRemainsQuietUnderCategoryOverride(LogLevel level)
    {
        using var factory = new ObservationFactory("127.0.0.1", consoleOverride: true);
        using var client = factory.CreateClient();
        factory.Records.Clear();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Web.PrivacyControl");
        logger.Log(level, new EventId(80), Sensitive, null, static (state, _) => state);
        Assert.Empty(factory.Records.Formatted);
        Assert.Equal(level, Assert.Single(factory.Records.Snapshot).Level);
        using var output = new StringWriter();
        var entry = new Microsoft.Extensions.Logging.Abstractions.LogEntry<string>(
            level, "Web.PrivacyControl", new EventId(80), Sensitive, null,
            static (_, _) => throw new InvalidOperationException("Formatter callback must stay unused."));
        factory.Records.Formatter.Write(in entry, null, output);
        Assert.Equal(string.Empty, output.ToString());
    }

    /// <summary>The real readiness endpoint reports bounded failures and resets after a healthy response.</summary>
    [Fact]
    public async Task ActualProgram_FormattedHealthFailureIsBoundedAndResetsAfterRecovery()
    {
        using var factory = new ObservationFactory("127.0.0.1");
        using var client = factory.CreateClient();
        factory.HealthFails = true;
        factory.Records.Clear();
        async Task Probe(HttpStatusCode expected)
        {
            using var response = await client.GetAsync("/web/readiness");
            Assert.Equal(expected, response.StatusCode);
            Assert.DoesNotContain(Sensitive, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        await Probe(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(1, HealthFailureCount(factory.Records.Formatted));
        await Probe(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(1, HealthFailureCount(factory.Records.Formatted));
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await Probe(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(2, HealthFailureCount(factory.Records.Formatted));
        factory.HealthFails = false;
        await Probe(HttpStatusCode.OK);
        Assert.Equal(2, HealthFailureCount(factory.Records.Formatted));
        factory.HealthFails = true;
        await Probe(HttpStatusCode.ServiceUnavailable);
        Assert.Equal(3, HealthFailureCount(factory.Records.Formatted));
        Assert.All(factory.Records.Formatted, output => Assert.DoesNotContain(Sensitive, output, StringComparison.Ordinal));
    }

    /// <summary>The actual entry seam preserves tooling interception and safely contains ordinary startup failures.</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("synchronous")]
    [InlineData("asynchronous")]
    [InlineData("sink-failure")]
    [InlineData("host-aborted")]
    public async Task ProgramEntry_StartupBoundaryPreservesExitAndToolingContracts(string kind)
    {
        var previousExitCode = Environment.ExitCode;
        using var output = kind == "sink-failure" ? new ThrowingWriter() : new StringWriter();
        var aborted = new Microsoft.Extensions.Hosting.HostAbortedException();
        var calls = 0;
        try
        {
            Environment.ExitCode = 37;
            Task Start()
            {
                calls++;
                return kind switch
                {
                    "success" => Task.CompletedTask,
                    "asynchronous" => Task.FromException(new InvalidOperationException(Sensitive)),
                    "host-aborted" => throw aborted,
                    _ => throw new InvalidOperationException(Sensitive),
                };
            }
            if (kind == "host-aborted")
            {
                var actual = await Assert.ThrowsAsync<Microsoft.Extensions.Hosting.HostAbortedException>(
                    () => Program.RunHostAsync(Start, output));
                Assert.Same(aborted, actual);
            }
            else
                Assert.Null(await Xunit.Record.ExceptionAsync(() => Program.RunHostAsync(Start, output)));
            Assert.Equal(1, calls);
            Assert.Equal(kind is "success" or "host-aborted" ? 37 : 1, Environment.ExitCode);
            if (kind is "success" or "host-aborted" or "sink-failure")
                Assert.Equal(string.Empty, output.ToString());
            else
            {
                using var json = JsonDocument.Parse(output.ToString());
                Assert.Equal("CRITICAL", json.RootElement.GetProperty("severity").GetString());
                Assert.Equal(5102, json.RootElement.GetProperty("eventId").GetInt32());
                Assert.Equal("StartupFailure", json.RootElement.GetProperty("EventName").GetString());
                Assert.Equal("HostInitialization", json.RootElement.GetProperty("Operation").GetString());
                Assert.Equal(typeof(InvalidOperationException).FullName, json.RootElement.GetProperty("exceptionType").GetString());
                Assert.DoesNotContain(Sensitive, output.ToString(), StringComparison.Ordinal);
            }
        }
        finally { Environment.ExitCode = previousExitCode; }
    }

    /// <summary>Testing startup preserves the original failure and exit code instead of signaling successful host discovery.</summary>
    [Theory]
    [InlineData("synchronous")]
    [InlineData("asynchronous")]
    [InlineData("tokenless-cancellation")]
    [InlineData("foreign-cancellation")]
    public async Task ProgramEntry_TestingStartupPreservesOriginalFailureAndExitCode(string kind)
    {
        var previousExitCode = Environment.ExitCode;
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception failure = kind switch
        {
            "tokenless-cancellation" => new OperationCanceledException(Sensitive),
            "foreign-cancellation" => new OperationCanceledException(Sensitive, cancellation.Token),
            _ => new InvalidOperationException(Sensitive),
        };
        var calls = 0;
        Task Start()
        {
            calls++;
            if (kind == "synchronous") throw failure;
            return Task.FromException(failure);
        }
        try
        {
            Environment.ExitCode = 53;

            var actual = await Xunit.Record.ExceptionAsync(() =>
                Program.RunHostAsync(Start, output, preserveTestingException: () => true));

            Assert.Same(failure, actual);
            Assert.Equal(1, calls);
            Assert.Equal(53, Environment.ExitCode);
            Assert.Equal(string.Empty, output.ToString());
            if (failure is OperationCanceledException canceled)
                Assert.Equal(canceled.CancellationToken, Assert.IsType<OperationCanceledException>(actual).CancellationToken);
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }

    /// <summary>The real Testing Program lets WAF observe its original options failure without changing the process exit code.</summary>
    [Fact]
    public void ActualProgram_TestingValidationFailureRemainsAnOptionsFailure()
    {
        var previousExitCode = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 53;
            using var factory = new ObservationFactory("127.0.0.1", missingRecaptchaProject: true);

            var failure = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            {
                using var client = factory.CreateClient();
            });

            Assert.Contains(failure.Failures, value => value.Contains("Recaptcha:ProjectId", StringComparison.Ordinal));
            Assert.Equal(53, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }

    private static JsonElement AssertFormattedEvent(string[] records, string name, string severity)
    {
        var events = records.Select(output =>
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }).Where(entry => entry.TryGetProperty("EventName", out var value) && value.GetString() == name).ToArray();
        var failure = Assert.Single(events);
        Assert.Equal(severity, failure.GetProperty("severity").GetString());
        Assert.All(records, output => Assert.DoesNotContain(Sensitive, output, StringComparison.Ordinal));
        return failure;
    }

    private static int HealthFailureCount(string[] records) => records.Count(output =>
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.TryGetProperty("EventName", out var name) && name.GetString() == "HealthProbeFailure";
    });

    private sealed class OpaqueCustomerPayload
    {
        public override string ToString() => throw new InvalidOperationException("Customer payload must not be rendered.");
    }

    private sealed class ThrowingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("Synthetic sink failure.");
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
        bool forgedIncidentHeader = false, bool consoleOverride = false,
        bool missingRecaptchaProject = false) : TestingWebApplicationFactory
    {
        public RecordingProvider Records { get; } = new();
        public ObservationClock Clock { get; } = new();
        public FailurePageFilter PageFilter { get; } = new(ordinaryFailure, handledFailure, forgedIncidentHeader);
        public bool ShouldOverwriteHealth => overwriteHealth;
        public int SyntheticOverwrites;
        public int HealthOverwrites;
        public bool HealthFails;

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
            if (missingRecaptchaProject)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Recaptcha:ProjectId"] = null,
                    }));
            if (consoleOverride)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Logging:Console:LogLevel:Web.PrivacyControl"] = "Trace",
                        ["Observability:TracingEnabled"] = "true",
                        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
                    }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<ILoggerProvider>(provider =>
                {
                    var name = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<
                        Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>>().Value.FormatterName;
                    Records.Formatter = Assert.Single(provider.GetServices<Microsoft.Extensions.Logging.Console.ConsoleFormatter>(),
                        formatter => formatter.Name == name);
                    return Records;
                });
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
            return Task.FromResult(owner.HealthFails ? HealthCheckResult.Unhealthy(Sensitive) : HealthCheckResult.Healthy());
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

    [Microsoft.Extensions.Logging.ProviderAlias("Console")]
    private sealed class RecordingProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<Record> records = new();
        private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        private readonly ConcurrentQueue<string> formatted = new();
        public Microsoft.Extensions.Logging.Console.ConsoleFormatter Formatter { get; set; } = null!;
        public string[] Formatted => formatted.ToArray();
        public Action? OnFrameworkError { get; set; }
        public Record[] Snapshot => records.ToArray();
        public Record[] Failures => Snapshot.Where(entry => entry.Level >= LogLevel.Warning).ToArray();
        public void Clear() { records.Clear(); formatted.Clear(); }
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
                using var output = new StringWriter();
                var entry = new Microsoft.Extensions.Logging.Abstractions.LogEntry<TState>(
                    logLevel, category, eventId, state, exception, formatter);
                owner.Formatter.Write(in entry, owner.scopes, output);
                if (output.GetStringBuilder().Length > 0) owner.formatted.Enqueue(output.ToString());
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
