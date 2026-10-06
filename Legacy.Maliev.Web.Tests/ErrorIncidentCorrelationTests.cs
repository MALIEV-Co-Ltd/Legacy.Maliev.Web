using Legacy.Maliev.Web.Middleware;
using Legacy.Maliev.Web.Components.Pages;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

public sealed class ErrorIncidentCorrelationTests
{
    [Fact]
    public void GetOrCreate_CreatesOpaqueHeaderSafeUtcIncidentAndReusesIt()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "unsafe\r\nX-Forged: yes";
        var observed = new DateTimeOffset(2026, 8, 2, 10, 30, 0, TimeSpan.FromHours(7));

        var first = ErrorIncidentHandler.GetOrCreate(context, observed);
        var second = ErrorIncidentHandler.GetOrCreate(context, observed.AddHours(1));

        Assert.Same(first, second);
        Assert.Matches("^[a-f0-9]{32}$", first.IncidentId);
        Assert.Equal(TimeSpan.Zero, first.OccurredAtUtc.Offset);
        Assert.Equal(observed.ToUniversalTime(), first.OccurredAtUtc);
        Assert.Equal(first.IncidentId, context.Response.Headers[ErrorIncidentHandler.HeaderName]);
        Assert.DoesNotContain('\r', first.IncidentId);
        Assert.DoesNotContain('\n', first.IncidentId);

        // Match exception-handler response clearing before the shared Error renderer.
        context.Response.Headers.Clear();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException("private-original-exception"),
        });
        var display = Assert.IsType<ErrorDisplayModel>(ErrorDisplayModelResolver.Resolve(context, null));
        Assert.Equal(first.IncidentId, display.IncidentId);
        Assert.Equal(first.OccurredAtUtc, display.OccurredAtUtc);
        Assert.Equal(first.IncidentId, context.Response.Headers[ErrorIncidentHandler.HeaderName]);
        Assert.True(ErrorIncidentHandler.TryGet(context, out var restored));
        Assert.Same(first, restored);

        context.Response.Headers.Clear();
        context.Features.Set<IExceptionHandlerFeature>(null);
        var preview = Assert.IsType<ErrorDisplayModel>(ErrorDisplayModelResolver.Resolve(context, 500));
        Assert.True(preview.IsPreview);
        Assert.Null(preview.IncidentId);
        Assert.False(context.Response.Headers.ContainsKey(ErrorIncidentHandler.HeaderName));

        var unowned = new DefaultHttpContext();
        unowned.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException("private-unobserved-exception"),
        });
        var unownedDisplay = Assert.IsType<ErrorDisplayModel>(ErrorDisplayModelResolver.Resolve(unowned, null));
        Assert.Null(unownedDisplay.IncidentId);
        Assert.False(unowned.Response.Headers.ContainsKey(ErrorIncidentHandler.HeaderName));
    }

    [Fact]
    public void LogUnhandledFailure_EmitsSearchableStructuredFieldsWithoutRequestSecrets()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/quotation";
        context.Request.QueryString = new QueryString("?token=sensitive-query-token");
        context.Request.Headers.Authorization = "Bearer sensitive-access-token";
        context.Request.Headers.Cookie = "session=sensitive-cookie";
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("sensitive-exception-message");

        var incident = ErrorIncidentHandler.LogUnhandledFailure(
            context,
            logger,
            exception,
            new DateTimeOffset(2026, 8, 2, 3, 30, 0, TimeSpan.Zero));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal("UnhandledRequestFailure", entry.Properties["EventName"]);
        Assert.Equal(incident.IncidentId, entry.Properties["IncidentId"]);
        Assert.Equal("2026-08-02T03:30:00.0000000+00:00", entry.Properties["OccurredAtUtc"]);
        Assert.Equal(500, entry.Properties["StatusCode"]);
        Assert.Equal("POST", entry.Properties["Method"]);
        Assert.Equal("/quotation", entry.Properties["Path"]);
        Assert.Equal(typeof(Program).Assembly.GetName().Name, entry.Properties["Service"]);
        Assert.Equal(nameof(InvalidOperationException), entry.Properties["ExceptionType"]);
        Assert.Equal(8, entry.Properties.Count);

        var rendered = entry.RenderedMessage;
        Assert.DoesNotContain("sensitive-query-token", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-access-token", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-cookie", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-exception-message", rendered, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        string RenderedMessage,
        IReadOnlyDictionary<string, object?> Properties);
}
