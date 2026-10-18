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
