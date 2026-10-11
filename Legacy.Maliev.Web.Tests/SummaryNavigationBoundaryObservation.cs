using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Bounded test-host observations; never reads log state, URLs, headers or response bodies.</summary>
internal sealed class SummaryNavigationBoundaryRecorder
{
    private readonly object gate = new();
    private readonly AsyncLocal<SummaryNavigationBoundaryLease?> request = new();
    private SummaryNavigationBoundaryLease? active;
    internal SummaryNavigationBoundaryLease? Current { get { lock (gate) return active; } }
    internal SummaryNavigationBoundaryLease? CurrentRequest => request.Value;

    internal SummaryNavigationBoundaryLease Begin(string phase)
    {
        if (phase is not ("initial" or "reload")) throw new ArgumentOutOfRangeException(nameof(phase));
        lock (gate)
        {
            if (active is not null) throw new InvalidOperationException("Navigation observation already active.");
            return active = new(this, phase);
        }
    }

    internal IDisposable EnterRequest(SummaryNavigationBoundaryLease lease)
    {
        var previous = request.Value;
        request.Value = lease;
        return new Scope(() => request.Value = previous);
    }

    internal void End(SummaryNavigationBoundaryLease lease)
    {
        lock (gate)
        {
            lease.Close();
            if (ReferenceEquals(active, lease)) active = null;
        }
    }

    private sealed class Scope(Action close) : IDisposable
    {
        private Action? closeAction = close;
        public void Dispose() => Interlocked.Exchange(ref closeAction, null)?.Invoke();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum SummaryNavigationStage
{
    BrowserDocumentStarted, KestrelIngress, EndpointExecuting, ResponseStarting,
    EndpointExecuted, RequestFaulted, RequestCompleted, BrowserDocumentResponse,
    BrowserDocumentFinished, BrowserDocumentFailed, InteractiveInputReady
}

internal sealed record SummaryNavigationEvent(SummaryNavigationStage Stage, long ElapsedMilliseconds, int? Status);
internal sealed record SummaryNavigationSnapshot(Guid Correlation, string Phase, bool Closed,
    int Omitted, SummaryNavigationEvent[] Events);

internal sealed class SummaryNavigationBoundaryLease(SummaryNavigationBoundaryRecorder owner, string phase) : IDisposable
{
    private readonly object gate = new();
    private readonly Guid correlation = Guid.NewGuid();
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly List<SummaryNavigationEvent> events = [];
    private int omitted;
    private bool closed;

    internal void Record(SummaryNavigationStage stage, int? status = null)
    {
        lock (gate)
        {
            if (closed) return;
            if (events.Count == 64) { omitted++; return; }
            events.Add(new(stage, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                status is >= 100 and <= 599 ? status : null));
        }
    }

    internal SummaryNavigationSnapshot Snapshot()
    {
        lock (gate) return new(correlation, phase, closed, omitted, events.ToArray());
    }

    internal void Close() { lock (gate) closed = true; }
    public void Dispose() => owner.End(this);
}

/// <summary>Instruments only the existing fixture document route, without changing requests or responses.</summary>
internal sealed class SummaryNavigationStartupFilter(SummaryNavigationBoundaryRecorder recorder) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, invokeNext) =>
        {
            var lease = recorder.Current;
            if (lease is null || !HttpMethods.IsGet(context.Request.Method) ||
                !context.Request.Path.Equals("/instantquotation/3d-printing", StringComparison.OrdinalIgnoreCase))
            {
                await invokeNext(context);
                return;
            }

            using var scope = recorder.EnterRequest(lease);
            lease.Record(SummaryNavigationStage.KestrelIngress);
            context.Response.OnStarting(() =>
            {
                lease.Record(SummaryNavigationStage.ResponseStarting, context.Response.StatusCode);
                return Task.CompletedTask;
            });
            try { await invokeNext(context); }
            catch { lease.Record(SummaryNavigationStage.RequestFaulted); throw; }
            finally { lease.Record(SummaryNavigationStage.RequestCompleted); }
        });
        next(app);
    };
}

/// <summary>Observes ASP.NET Core endpoint lifecycle event IDs; formatter/state/exception are never inspected.</summary>
internal sealed class SummaryNavigationLoggerProvider(SummaryNavigationBoundaryRecorder recorder) : ILoggerProvider
{
    internal const string EndpointCategory = "Microsoft.AspNetCore.Routing.EndpointMiddleware";
    public ILogger CreateLogger(string categoryName) => new Logger(recorder, categoryName == EndpointCategory);
    public void Dispose() { }

    private sealed class Logger(SummaryNavigationBoundaryRecorder recorder, bool endpoint) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => endpoint && level == LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            if (eventId.Id == 0 && eventId.Name == "ExecutingEndpoint")
                recorder.CurrentRequest?.Record(SummaryNavigationStage.EndpointExecuting);
            else if (eventId.Id == 1 && eventId.Name == "ExecutedEndpoint")
                recorder.CurrentRequest?.Record(SummaryNavigationStage.EndpointExecuted);
        }
    }
}

internal static class SummaryNavigationObservationWriter
{
    /// <summary>Observer failure rejects an otherwise passing navigation; an existing failure is never masked.</summary>
    internal static void Write(Func<string> capture, Action<string> write, Exception? original, Action<string> fallback)
    {
        var category = "capture";
        try
        {
            var report = capture();
            category = "write";
            write(report);
        }
        catch when (original is not null)
        {
            try { original.Data["SummaryNavigationObserverFailure"] = category; }
            catch { /* Exception metadata must not replace the original failure. */ }
            try { fallback("summary-navigation-observer-error " + JsonSerializer.Serialize(new { category, originalRetained = true })); }
            catch
            {
                try { original.Data["SummaryNavigationObserverFallbackFailure"] = true; }
                catch { /* The caller still throws its original navigation failure. */ }
            }
        }
    }
}
