using Microsoft.AspNetCore.Builder;
using Legacy.Maliev.Web;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Legacy.Maliev.Web.Tests;

// Reuse the existing collection that excludes process-wide Console.Out races.
[Collection("Shared diagnostics output")]
public sealed class WebPrivateLoggingSourceTests
{
    private const string Category = "Legacy.Maliev.Web.Tests.WebPrivateLoggingSourceTests";
    private const string Secret = "synthetic-private-logging-sentinel";

    [Fact]
    public async Task ActualProgramSelectedFormatter_DoesNotEvaluatePrivateRenderedMessage()
    {
        await using var factory = new LoggingFactory();
        var services = factory.Services;
        var selectedName = services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName;
        var formatter = Assert.Single(services.GetServices<ConsoleFormatter>(), value => value.Name == selectedName);
        var evaluated = false;
        var entry = new LogEntry<string>(LogLevel.Error, Category, new EventId(7341), Secret,
            new InvalidOperationException(Secret), (_, _) => { evaluated = true; return Secret; });
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        formatter.Write(in entry, null, output);

        Assert.False(evaluated);
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("ERROR", document.RootElement.GetProperty("severity").GetString());
        Assert.False(document.RootElement.TryGetProperty("Message", out _));
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    public async Task ActualProgramSelectedFormatter_SuccessLevelsNeverRenderOrWrite(LogLevel level)
    {
        await using var factory = new LoggingFactory();
        var services = factory.Services;
        var selectedName = services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName;
        var formatter = Assert.Single(services.GetServices<ConsoleFormatter>(), value => value.Name == selectedName);
        var evaluated = false;
        var entry = new LogEntry<string>(level, Category, new EventId(7342), Secret, null,
            (_, _) => { evaluated = true; return Secret; });
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        formatter.Write(in entry, null, output);

        Assert.False(evaluated);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Theory]
    [InlineData(LogLevel.Warning, "WARNING")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "CRITICAL")]
    public async Task ActualProgramConsoleProvider_DropsPrivateMessageStateAndScopes(LogLevel level, string severity)
    {
        var original = Console.Out;
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        using var output = TextWriter.Synchronized(buffer);
        try
        {
            Console.SetOut(output);
            await using (var factory = new LoggingFactory())
            {
                var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
                using (logger.BeginScope(new Dictionary<string, object?>
                {
                    ["CustomerEmail"] = Secret,
                    ["TraceId"] = "0123456789abcdef0123456789abcdef"
                }))
                {
                    logger.Log(level, new EventId(7343), new Dictionary<string, object?>
                    {
                        ["EventName"] = "BusinessOperationFailure",
                        ["Operation"] = "quotation",
                        ["StatusCode"] = 503,
                        ["Code"] = "quote_invalid",
                        ["CustomerEmail"] = Secret,
                        ["Path"] = "/" + Secret,
                        ["{OriginalFormat}"] = Secret
                    }, new InvalidOperationException(Secret), static (_, _) => Secret);
                }
            }
            // Factory disposal owns provider shutdown/drain. No sleep or console-queue delivery claim.
        }
        finally
        {
            Console.SetOut(original);
        }

        var records = buffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            var record = Assert.Single(records, value =>
                value.RootElement.TryGetProperty("Category", out var category) && category.GetString() == Category);
            Assert.Equal(severity, record.RootElement.GetProperty("severity").GetString());
            Assert.DoesNotContain(Secret, record.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.False(record.RootElement.TryGetProperty("Message", out _));
            var state = record.RootElement.GetProperty("State");
            Assert.Equal("BusinessOperationFailure", state.GetProperty("EventName").GetString());
            Assert.Equal("quotation", state.GetProperty("Operation").GetString());
            Assert.Equal(503, state.GetProperty("StatusCode").GetInt32());
            Assert.Equal("quote_invalid", state.GetProperty("Code").GetString());
            Assert.False(state.TryGetProperty("CustomerEmail", out _));
            Assert.False(state.TryGetProperty("Path", out _));
            Assert.False(state.TryGetProperty("{OriginalFormat}", out _));
        }
        finally
        {
            foreach (var record in records) record.Dispose();
        }
    }

    [Fact]
    public async Task ActualProgram_SelectsPrivateConsoleLogsWithoutRemovingMetricsOrTracing()
    {
        await using var factory = new LoggingFactory();
        var services = factory.Services;

        Assert.DoesNotContain(services.GetServices<ILoggerProvider>(),
            provider => provider is OpenTelemetryLoggerProvider);
        Assert.Single(services.GetServices<ILoggerProvider>(), provider => provider is ConsoleLoggerProvider);
        Assert.NotNull(services.GetRequiredService<MeterProvider>());
        Assert.NotNull(services.GetRequiredService<TracerProvider>());
        Assert.Equal("", services.GetRequiredService<IConfiguration>()["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    }

    [Fact]
    public async Task ActualProgramAllProviderWarningFloor_SurvivesCategoryOverrideAndConfigurationReload()
    {
        await using var factory = new LoggingFactory();
        var services = factory.Services;
        var configuration = Assert.IsAssignableFrom<IConfigurationRoot>(services.GetRequiredService<IConfiguration>());
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);

        configuration["Logging:LogLevel:" + Category] = "Trace";
        configuration.Reload();
        logger.LogInformation("Synthetic successful operation");
        logger.LogWarning("Synthetic warning control");
        Assert.Single(factory.OtherProvider.Levels, level => level == LogLevel.Warning);
        Assert.DoesNotContain(factory.OtherProvider.Levels, level => level < LogLevel.Warning);

        configuration["Logging:LogLevel:Default"] = "Trace";
        configuration["Logging:LogLevel:" + Category] = "Information";
        configuration.Reload();
        logger.LogInformation("Synthetic successful operation after reload");
        logger.LogError("Synthetic error control");
        Assert.Single(factory.OtherProvider.Levels, level => level == LogLevel.Error);
        Assert.DoesNotContain(factory.OtherProvider.Levels, level => level < LogLevel.Warning);
    }


    [Fact]
    public void PrivateFormatter_NeverEnumeratesUnknownStateOrScopesAndRetainsActivityTrace()
    {
        using var activity = new System.Diagnostics.Activity("private-logging-control")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C).Start();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var entry = new LogEntry<OpaqueState>(LogLevel.Error, Category, new EventId(7344),
            new OpaqueState(), new InvalidOperationException(Secret),
            static (_, _) => throw new InvalidOperationException("Formatter must not execute"));

        new WebPrivateConsoleFormatter().Write(in entry, new OpaqueScopes(), output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Empty(record.RootElement.GetProperty("State").EnumerateObject());
        Assert.Contains(record.RootElement.GetProperty("Scopes").EnumerateArray(), scope =>
            scope.GetProperty("TraceId").GetString() == activity.TraceId.ToHexString()
            && scope.GetProperty("SpanId").GetString() == activity.SpanId.ToHexString());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("upload_invalid")]
    [InlineData("quote_invalid")]
    [InlineData("quote_expired")]
    [InlineData("upload_expired")]
    [InlineData("quote_mismatch")]
    public void PrivateFormatter_PreservesExactlyTheFiveSourceBusinessCodes(string code)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var fields = new[] { new KeyValuePair<string, object?>("Code", code) };
        var entry = new LogEntry<KeyValuePair<string, object?>[]>(LogLevel.Warning, Category,
            default, fields, null, static (_, _) => throw new InvalidOperationException());

        new WebPrivateConsoleFormatter().Write(in entry, null, output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Equal(code, record.RootElement.GetProperty("State").GetProperty("Code").GetString());
    }

    [Theory]
    [InlineData("customer-file.pdf")]
    [InlineData("QUOTE_INVALID")]
    [InlineData("quote_invalid\nforged")]
    public void PrivateFormatter_DropsUnknownCodesWithoutRenderingValues(string code)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var fields = new Dictionary<string, object?>
        {
            ["Code"] = code,
            ["Operation"] = new OpaqueValue(),
            ["CustomerEmail"] = Secret,
            ["Payload"] = new OpaqueValue(),
        };
        var entry = new LogEntry<Dictionary<string, object?>>(LogLevel.Error, Category, default,
            fields, new InvalidOperationException(Secret), static (_, _) => throw new InvalidOperationException());

        new WebPrivateConsoleFormatter().Write(in entry, null, output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Empty(record.RootElement.GetProperty("State").EnumerateObject());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateFormatter_BoundsFieldCountAndRejectsOversizedIdentifiersAndInvalidCategory()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var fields = Enumerable.Range(0, 64)
            .Select(index => new KeyValuePair<string, object?>("Unknown" + index, new OpaqueValue()))
            .Append(new KeyValuePair<string, object?>("EventName", "BeyondFieldBudget"))
            .ToArray();
        var entry = new LogEntry<KeyValuePair<string, object?>[]>(LogLevel.Error,
            "/" + Secret + "?token=" + new string('x', 50000), default, fields, null,
            static (_, _) => throw new InvalidOperationException());

        new WebPrivateConsoleFormatter().Write(in entry, null, output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Equal("Application", record.RootElement.GetProperty("Category").GetString());
        Assert.Empty(record.RootElement.GetProperty("State").EnumerateObject());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(output.ToString()) <= 8192);
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActualIncident_ConsoleKeepsNestedIncidentUtcAndTraceWithoutRequestValues()
    {
        var original = Console.Out;
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        using var output = TextWriter.Synchronized(buffer);
        string incidentId;
        using var activity = new System.Diagnostics.Activity("private-incident-control")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C).Start();
        try
        {
            Console.SetOut(output);
            await using (var factory = new LoggingFactory())
            {
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                context.Request.Method = "POST";
                context.Request.Path = "/" + Secret;
                context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?token=" + Secret);
                var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
                var incident = Legacy.Maliev.Web.Middleware.ErrorIncidentHandler.LogUnhandledFailure(
                    context, logger, new InvalidOperationException(Secret),
                    new DateTimeOffset(2026, 10, 18, 7, 0, 0, TimeSpan.FromHours(7)));
                incidentId = incident.IncidentId;
            }
        }
        finally
        {
            Console.SetOut(original);
        }

        var records = buffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            var record = Assert.Single(records, value =>
                value.RootElement.GetProperty("Category").GetString() == Category).RootElement;
            var state = record.GetProperty("State");
            Assert.Equal(incidentId, state.GetProperty("IncidentId").GetString());
            Assert.Equal("2026-10-18T00:00:00.0000000+00:00", state.GetProperty("OccurredAtUtc").GetString());
            Assert.Equal("UnhandledRequestFailure", state.GetProperty("EventName").GetString());
            Assert.False(state.TryGetProperty("Path", out _));
            Assert.Contains(record.GetProperty("Scopes").EnumerateArray(), scope =>
                scope.GetProperty("TraceId").GetString() == activity.TraceId.ToHexString());
            Assert.DoesNotContain(Secret, buffer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            foreach (var record in records) record.Dispose();
        }
    }

    [Fact]
    public void WarningFloor_ShortCircuitsOldDelegateAndPreservesStricterNoneAndError()
    {
        var called = 0;
        var options = new LoggerFilterOptions { MinLevel = LogLevel.Error };
        options.Rules.Add(new LoggerFilterRule(null, Category, LogLevel.Trace,
            (_, _, _) => { called++; return true; }));
        options.Rules.Add(new LoggerFilterRule("stricter", null, LogLevel.None, null));

        PrivateWebLogging.ClampFilters(options);

        Assert.Equal(LogLevel.Error, options.MinLevel);
        Assert.Equal(LogLevel.None, options.Rules[1].LogLevel);
        Assert.False(options.Rules[0].Filter!(null, Category, LogLevel.Debug));
        Assert.Equal(0, called);
        Assert.True(options.Rules[0].Filter!(null, Category, LogLevel.Error));
        Assert.Equal(1, called);
    }

    [Fact]
    public async Task ActualProgram_FormatterSelectionStaysPrivateAfterConfigurationReload()
    {
        await using var factory = new LoggingFactory();
        var services = factory.Services;
        var configuration = Assert.IsAssignableFrom<IConfigurationRoot>(services.GetRequiredService<IConfiguration>());
        configuration["Logging:Console:FormatterName"] = "json";
        configuration.Reload();

        Assert.Equal(WebPrivateConsoleFormatter.FormatterName,
            services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName);
        Assert.DoesNotContain(services.GetServices<ILoggerProvider>(),
            provider => provider is OpenTelemetryLoggerProvider);
    }

    [Fact]
    public async Task BootstrapFailure_IsPrivateCriticalAndReturnsOneWithoutMutatingProcessExitCode()
    {
        var exit = Environment.ExitCode;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var result = await PrivateWebLogging.RunHostAsync(
            () => Task.FromException(new InvalidOperationException(Secret)), bootstrapOutput: output);

        Assert.Equal(1, result);
        Assert.Equal(exit, Environment.ExitCode);
        using var record = JsonDocument.Parse(output.ToString());
        Assert.Equal("CRITICAL", record.RootElement.GetProperty("severity").GetString());
        Assert.Equal("HostStartupFailure", record.RootElement.GetProperty("State").GetProperty("EventName").GetString());
        Assert.Equal("startup", record.RootElement.GetProperty("State").GetProperty("Operation").GetString());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapTestingCompatibility_RethrowsOriginalValidationFailure()
    {
        var error = new InvalidOperationException(Secret);
        var exit = Environment.ExitCode;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PrivateWebLogging.RunHostAsync(() => Task.FromException(error),
                preserveTestingException: () => true, bootstrapOutput: output));

        Assert.Same(error, observed);
        Assert.Equal(exit, Environment.ExitCode);
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapToolingAbort_IsRethrownWithoutFalseCriticalOrExitMutation()
    {
        var error = new Microsoft.Extensions.Hosting.HostAbortedException();
        var exit = Environment.ExitCode;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var observed = await Assert.ThrowsAsync<Microsoft.Extensions.Hosting.HostAbortedException>(() =>
            PrivateWebLogging.RunHostAsync(() => Task.FromException(error), bootstrapOutput: output));

        Assert.Same(error, observed);
        Assert.Equal(exit, Environment.ExitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task BootstrapNormalCompletion_IsQuiet()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(0, await PrivateWebLogging.RunHostAsync(() => Task.CompletedTask, bootstrapOutput: output));
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void SinkFailure_UsesOnlyFixedPrivateFallback()
    {
        var original = Console.Error;
        using var fallback = new StringWriter(CultureInfo.InvariantCulture);
        var entry = new LogEntry<string>(LogLevel.Error, Category, default, Secret,
            new InvalidOperationException(Secret), static (_, _) => throw new InvalidOperationException());
        try
        {
            Console.SetError(fallback);
            new WebPrivateConsoleFormatter().Write(in entry, new OpaqueScopes(), new FailingWriter());
        }
        finally
        {
            Console.SetError(original);
        }

        using var record = JsonDocument.Parse(fallback.ToString());
        Assert.Equal("PrivateLogSinkFailure", record.RootElement.GetProperty("State").GetProperty("EventName").GetString());
        Assert.DoesNotContain(Secret, fallback.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapSinkFailure_StillReturnsOneAndUsesOnlyPrivateFallback()
    {
        var original = Console.Error;
        using var fallback = new StringWriter(CultureInfo.InvariantCulture);
        int result;
        try
        {
            Console.SetError(fallback);
            result = await PrivateWebLogging.RunHostAsync(
                () => Task.FromException(new InvalidOperationException(Secret)), bootstrapOutput: new FailingWriter());
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Equal(1, result);
        Assert.Contains("PrivateLogSinkFailure", fallback.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, fallback.ToString(), StringComparison.Ordinal);
    }

    private sealed class OpaqueValue
    {
        public override string ToString() => throw new InvalidOperationException("No private value rendering");
    }

    private sealed class OpaqueState : IEnumerable<KeyValuePair<string, object?>>
    {
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            throw new InvalidOperationException("No arbitrary state enumeration");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() => throw new InvalidOperationException("No arbitrary state rendering");
    }

    private sealed class OpaqueScopes : IExternalScopeProvider
    {
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) =>
            throw new InvalidOperationException("No arbitrary scope traversal");
        public IDisposable Push(object? state) => throw new InvalidOperationException("No arbitrary scope push");
    }

    private sealed class FailingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException(Secret);
    }


    [Fact]
    public async Task ActualSyntheticConsole_ErrorKeepsOwnedNonceAndRejectsHeaderOnlyForgery()
    {
        const string nonce = "0123456789abcdef0123456789abcdef";
        var original = Console.Out;
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        using var output = TextWriter.Synchronized(buffer);
        Exception typedError;
        try
        {
            Console.SetOut(output);
            await using (var factory = new SyntheticLoggingFactory())
            {
                using var client = factory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    "/internal/diagnostics/observability?token=" + Secret);
                request.Headers.Add("X-Maliev-Diagnostic-Id", nonce);
                using var response = await client.SendAsync(request);
                Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal(nonce, Assert.Single(response.Headers.GetValues("X-Maliev-Diagnostic-Id")));
                Assert.False(response.Headers.Contains("X-Incident-Id"));
                typedError = Assert.IsType<global::Maliev.Aspire.ServiceDefaults.Diagnostics.ProductionObservabilityDiagnosticException>(
                    factory.Capture.Error);
            }
        }
        finally
        {
            Console.SetOut(original);
        }

        var records = buffer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            var probe = Assert.Single(records, record =>
                record.RootElement.GetProperty("State").TryGetProperty("EventName", out var name)
                && name.GetString() == "ObservabilityPipelineProbe").RootElement;
            Assert.Equal("WARNING", probe.GetProperty("severity").GetString());
            Assert.True(probe.GetProperty("State").GetProperty("Synthetic").GetBoolean());
            Assert.Equal(nonce, probe.GetProperty("State").GetProperty("DiagnosticId").GetString());
            var error = Assert.Single(records, record =>
                record.RootElement.GetProperty("Category").GetString() ==
                "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware").RootElement;
            Assert.Equal("ERROR", error.GetProperty("severity").GetString());
            Assert.True(error.GetProperty("Synthetic").GetBoolean());
            Assert.Equal(nonce, error.GetProperty("DiagnosticId").GetString());
            var critical = Assert.Single(records, record =>
                record.RootElement.GetProperty("State").TryGetProperty("EventName", out var name)
                && name.GetString() == "UnhandledRequestFailure").RootElement;
            Assert.Equal("CRITICAL", critical.GetProperty("severity").GetString());
            Assert.Equal(nonce, critical.GetProperty("State").GetProperty("DiagnosticId").GetString());
            Assert.DoesNotContain(Secret, buffer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            foreach (var record in records) record.Dispose();
        }

        foreach (var kind in new[] { "valid", "missing-nonce", "missing-instance", "duplicate-nonce",
                     "duplicate-instance", "invalid-nonce", "invalid-instance", "business-error" })
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            if (kind != "missing-nonce")
                context.Response.Headers["X-Maliev-Diagnostic-Id"] = kind switch
                {
                    "duplicate-nonce" => new Microsoft.Extensions.Primitives.StringValues(new[] { nonce, nonce }),
                    "invalid-nonce" => "private-noncanonical-value",
                    _ => nonce,
                };
            if (kind != "missing-instance")
                context.Response.Headers["X-Maliev-Health-Instance"] = kind switch
                {
                    "duplicate-instance" => new Microsoft.Extensions.Primitives.StringValues(new[] { nonce, nonce }),
                    "invalid-instance" => "private-noncanonical-value",
                    _ => nonce,
                };
            var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor();
            var previous = accessor.HttpContext;
            using var projected = new StringWriter(CultureInfo.InvariantCulture);
            try
            {
                accessor.HttpContext = context;
                var failure = kind == "business-error" ? new InvalidOperationException(Secret) : typedError;
                var entry = new LogEntry<string>(LogLevel.Error, Category, default, Secret, failure,
                    static (_, _) => throw new InvalidOperationException());
                new WebPrivateConsoleFormatter(accessor).Write(in entry, new OpaqueScopes(), projected);
            }
            finally
            {
                accessor.HttpContext = previous;
            }
            using var document = JsonDocument.Parse(projected.ToString());
            Assert.Equal(kind == "valid", document.RootElement.TryGetProperty("Synthetic", out _));
            Assert.Equal(kind == "valid", document.RootElement.TryGetProperty("DiagnosticId", out _));
            Assert.DoesNotContain(Secret, projected.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("private-noncanonical-value", projected.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapCancellationFailure_IsCriticalForTokenlessAndForeignCanceledTokens(bool foreignToken)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var exit = Environment.ExitCode;
        Task Failure() => foreignToken ? Task.FromCanceled(cancellation.Token)
            : Task.FromException(new OperationCanceledException(Secret));

        Assert.Equal(1, await PrivateWebLogging.RunHostAsync(Failure, bootstrapOutput: output));

        Assert.Equal(exit, Environment.ExitCode);
        using var record = JsonDocument.Parse(output.ToString());
        Assert.Equal("CRITICAL", record.RootElement.GetProperty("severity").GetString());
        Assert.Equal("HostStartupFailure", record.RootElement.GetProperty("State").GetProperty("EventName").GetString());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapTestingCompatibility_RethrowsCancellationWithoutExitMutation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = new OperationCanceledException(Secret, cancellation.Token);
        var exit = Environment.ExitCode;
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var observed = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            PrivateWebLogging.RunHostAsync(() => Task.FromException(failure),
                preserveTestingException: () => true, bootstrapOutput: output));

        Assert.Same(failure, observed);
        Assert.Equal(exit, Environment.ExitCode);
        Assert.Contains("HostStartupFailure", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IncidentId", "bad\r\nincident")]
    [InlineData("IncidentId", "customer@example.invalid")]
    [InlineData("OccurredAtUtc", "private-non-date")]
    [InlineData("TraceId", "private-non-trace")]
    [InlineData("SpanId", "too-short")]
    [InlineData("EventName", "customer@example.invalid")]
    [InlineData("StatusCode", 99)]
    [InlineData("StatusCode", 600)]
    public void PrivateFormatter_RejectsNonGrammarStateWithoutStringification(string key, object value)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var fields = new[] { new KeyValuePair<string, object?>(key, value) };
        var entry = new LogEntry<KeyValuePair<string, object?>[]>(LogLevel.Error, Category, default,
            fields, null, static (_, _) => throw new InvalidOperationException());

        new WebPrivateConsoleFormatter().Write(in entry, new OpaqueScopes(), output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Empty(record.RootElement.GetProperty("State").EnumerateObject());
    }

    [Fact]
    public void PrivateFormatter_RetainsOnlySafeExceptionTypeAndCodeLocation()
    {
        Exception? failure = null;
        try
        {
            ThrowPrivateFailure();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var entry = new LogEntry<string>(LogLevel.Error, Category, default, Secret, failure,
            static (_, _) => throw new InvalidOperationException());

        new WebPrivateConsoleFormatter().Write(in entry, null, output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Equal("System.InvalidOperationException", record.RootElement.GetProperty("ExceptionType").GetString());
        Assert.Equal("System.ArgumentException", record.RootElement.GetProperty("InnerExceptionType").GetString());
        Assert.EndsWith(".ThrowPrivateFailure", record.RootElement.GetProperty("SourceLocation").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
        Assert.False(record.RootElement.TryGetProperty("Message", out _));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowPrivateFailure() =>
        throw new InvalidOperationException(Secret, new ArgumentException(Secret));

    private sealed class SyntheticLoggingFactory : TestingWebApplicationFactory
    {
        public TypedFailureCapture Capture { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(Capture);
                services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new LoopbackPeer());
            });
        }
    }

    private sealed class LoopbackPeer : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
                await following(context);
            });
            next(app);
        };
    }

    private sealed class TypedFailureCapture : ILoggerProvider
    {
        public Exception? Error { get; private set; }
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { Error = null; }
        private sealed class Capture(TypedFailureCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (exception is global::Maliev.Aspire.ServiceDefaults.Diagnostics.ProductionObservabilityDiagnosticException)
                    owner.Error = exception;
            }
        }
    }


    [Fact]
    public async Task PrivateFormatter_RetainsCompilerGeneratedAsyncLocationWithoutPrivateRendering()
    {
        Exception? failure = null;
        try
        {
            await ThrowPrivateAsyncFailure();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }
        var target = Assert.IsAssignableFrom<System.Reflection.MethodBase>(failure?.TargetSite);
        var declaring = Assert.IsType<string>(target.DeclaringType?.FullName);
        Assert.Contains("<", declaring, StringComparison.Ordinal);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var entry = new LogEntry<string>(LogLevel.Error, Category, default, Secret, failure,
            static (_, _) => throw new InvalidOperationException());

        new WebPrivateConsoleFormatter().Write(in entry, new OpaqueScopes(), output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Equal(declaring + "." + target.Name, record.RootElement.GetProperty("SourceLocation").GetString());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateFormatter_RetainsCompilerGeneratedClosureLocationWithoutPrivateRendering()
    {
        var privateValue = string.Concat(Secret, "-closure");
        Action thrower = [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] () =>
            throw new InvalidOperationException(privateValue);
        var failure = Assert.Throws<InvalidOperationException>(thrower);
        var target = Assert.IsAssignableFrom<System.Reflection.MethodBase>(failure.TargetSite);
        var declaring = Assert.IsType<string>(target.DeclaringType?.FullName);
        Assert.Contains("<", target.Name, StringComparison.Ordinal);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var entry = new LogEntry<string>(LogLevel.Error, Category, default, privateValue, failure,
            static (_, _) => throw new InvalidOperationException());

        new WebPrivateConsoleFormatter().Write(in entry, new OpaqueScopes(), output);

        using var record = JsonDocument.Parse(output.ToString());
        Assert.Equal(declaring + "." + target.Name, record.RootElement.GetProperty("SourceLocation").GetString());
        Assert.DoesNotContain(Secret, output.ToString(), StringComparison.Ordinal);
        Assert.False(record.RootElement.TryGetProperty("Message", out _));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task ThrowPrivateAsyncFailure()
    {
        await Task.Yield();
        throw new InvalidOperationException(Secret);
    }

    private sealed class LoggingFactory : TestingWebApplicationFactory
    {
        public LevelCapture OtherProvider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            builder.UseSetting("Observability:TracingEnabled", "true");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Logging:LogLevel:" + Category] = "Warning"
                }));
            // Added after the application's registrations only to observe the common ILogger filter.
            // This test sink stores levels, never renders state or exports a log.
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(OtherProvider));
        }
    }

    private sealed class LevelCapture : ILoggerProvider
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }

        private sealed class Logger(LevelCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (category == Category) owner.Levels.Enqueue(logLevel);
            }
        }
    }
}
