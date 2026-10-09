using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Passive fixed first-login observations; no request data, worker, retry or readiness substitution.</summary>
internal sealed class BillingInitialProbeDiagnostics(string culture, int width, string attemptRun) : IStartupFilter, ILoggerProvider
{
    private const int MaximumEvents = 16;
    private const int MaximumReceiptBytes = 32768;
    private readonly object gate = new();
    private readonly string invocation = Guid.NewGuid().ToString("N");
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly List<Observation> observations = [];
    private readonly List<ProviderObservation> providerObservations = [];
    private volatile bool active = true;
    private int dropped;
    private int observationFailures;

    internal enum Stage
    {
        ServerStartInvoked, ServerStartReturned, GetInvoked, CertificatePinAccepted,
        CertificatePinRejected, RequestPipelineEntered, ResponseStarting,
        ResponseCompleted, RequestPipelineReturned, RequestPipelineFailed,
        GetReturned, InitialProbeFailed, ObservationStopped,
    }

    private enum Failure { None, Canceled, HttpRequest, Io, Other }
    private sealed record Metrics(long CpuTicks, long WorkingSetBytes, long PrivateBytes,
        int ThreadPoolThreads, long PendingWorkItems, long CompletedWorkItems, int AvailableWorkers,
        int AvailableIo, long ManagedBytes, long HeapBytes, long FragmentedBytes, long MemoryLoadBytes,
        long HighMemoryLoadThresholdBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);
    private sealed record Observation(int Sequence, string Stage, long ElapsedTicks, Metrics? Metrics,
        int? Status, bool RequestCanceled, string Failure);
    private sealed record ProviderObservation(string Family, int EventId, long ElapsedTicks);

    internal static bool ObservedCategory(string? category) => Family(category) is not null;
    private static string? Family(string? category) => category switch
    {
        { } value when value.StartsWith("Microsoft.AspNetCore.DataProtection.", StringComparison.Ordinal) => "DataProtection",
        { } value when value.StartsWith("Microsoft.AspNetCore.Antiforgery.", StringComparison.Ordinal) => "Antiforgery",
        _ => null,
    };
    public ILogger CreateLogger(string categoryName) => new FixedLogger(this, Family(categoryName));
    public void Dispose() => StopAndPersist();

    private sealed class FixedLogger(BillingInitialProbeDiagnostics owner, string? family) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel)
        {
            try { lock (owner.gate) return owner.active && family is not null; }
            catch (Exception) { return false; }
        }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Never inspect state, invoke formatter, or retain exception/message/category text.
            try
            {
                lock (owner.gate)
                {
                    if (!owner.active || family is null || eventId.Id is < 0 or > 65535) return;
                    if (owner.providerObservations.Count == MaximumEvents) { owner.dropped = Math.Min(owner.dropped + 1, MaximumEvents); return; }
                    owner.providerObservations.Add(new(family, eventId.Id, Stopwatch.GetElapsedTime(owner.started).Ticks));
                }
            }
            catch (Exception) { /* A passive provider cannot change the real request result. */ }
        }
    }

    // Samples only this testhost, never enumerates processes or reads their arguments/environment.
    private static Metrics ReadMetrics()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        ThreadPool.GetAvailableThreads(out var workers, out var io);
        var memory = GC.GetGCMemoryInfo();
        return new(process.TotalProcessorTime.Ticks, process.WorkingSet64, process.PrivateMemorySize64,
            ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount, ThreadPool.CompletedWorkItemCount, workers, io,
            GC.GetTotalMemory(forceFullCollection: false), memory.HeapSizeBytes, memory.FragmentedBytes,
            memory.MemoryLoadBytes, memory.HighMemoryLoadThresholdBytes,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    }

    internal void Observe(Stage stage, int? status = null, bool requestCanceled = false, Exception? failure = null)
    {
        try
        {
            lock (gate)
            {
                if (!active) return;
                if (observations.Count == MaximumEvents) { dropped = Math.Min(dropped + 1, MaximumEvents); return; }
                Metrics? metrics = null;
                try
                {
                    if (stage is Stage.ServerStartInvoked or Stage.ServerStartReturned or Stage.GetInvoked or Stage.GetReturned or Stage.InitialProbeFailed)
                        metrics = ReadMetrics();
                }
                catch (Exception) { observationFailures = Math.Min(observationFailures + 1, MaximumEvents); }
                var kind = failure switch
                {
                    null => Failure.None,
                    OperationCanceledException => Failure.Canceled,
                    HttpRequestException => Failure.HttpRequest,
                    IOException => Failure.Io,
                    _ => Failure.Other,
                };
                observations.Add(new(observations.Count, stage.ToString(), Stopwatch.GetElapsedTime(started).Ticks,
                    metrics, status is >= 100 and <= 599 ? status : null, requestCanceled, kind.ToString()));
            }
        }
        catch (Exception) { /* Secondary observation failures cannot replace the actual startup/request exception. */ }
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            // The only observed request is the original anonymous readiness GET.
            if (!active || !Matches(context))
            {
                await continuation(context);
                return;
            }
            Observe(Stage.RequestPipelineEntered);
            try
            {
                context.Response.OnStarting(() =>
                {
                    ObserveResponse(context, Stage.ResponseStarting);
                    return Task.CompletedTask;
                });
                context.Response.OnCompleted(() =>
                {
                    ObserveResponse(context, Stage.ResponseCompleted);
                    return Task.CompletedTask;
                });
            }
            catch (Exception) { /* Callback-registration diagnostics cannot replace the unchanged downstream request. */ }
            try { await continuation(context); }
            catch (Exception failure) { Observe(Stage.RequestPipelineFailed, failure: failure); throw; }
            finally { ObserveResponse(context, Stage.RequestPipelineReturned); }
        });
        next(app);
    };

    private static bool Matches(HttpContext context)
    {
        try { return HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/Account/Login" && context.Request.QueryString.Value == "?culture=en"; }
        catch (Exception) { return false; }
    }

    private void ObserveResponse(HttpContext context, Stage stage)
    {
        try { Observe(stage, context.Response.StatusCode, context.RequestAborted.IsCancellationRequested); }
        catch (Exception) { /* Faulting context features must not replace a downstream exception or response. */ }
    }

    internal (int Events, int ProviderEvents, int Dropped, bool Active) State
    {
        get { lock (gate) return (observations.Count, providerObservations.Count, dropped, active); }
    }

    internal void Stop()
    {
        try
        {
            lock (gate)
            {
                if (!active) return;
                Observe(Stage.ObservationStopped);
                active = false;
            }
        }
        catch (Exception) { active = false; }
    }

    internal void StopAndPersist()
    {
        try
        {
            Observation[] snapshot;
            ProviderObservation[] providerSnapshot;
            lock (gate)
            {
                if (!active) return;
                Observe(Stage.ObservationStopped);
                active = false; // All later callbacks, including credential-bearing browser work, are inert.
                snapshot = observations.ToArray();
                providerSnapshot = providerObservations.ToArray();
            }
            static string? Coordinate(string name, string pattern)
            {
                var value = Environment.GetEnvironmentVariable(name);
                return value is { Length: <= 64 } && Regex.IsMatch(value, pattern) ? value : null;
            }
            var receipt = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                surface = "member-billing-initial-readiness",
                invocation,
                attemptRun,
                culture,
                width,
                candidateHead = Coordinate("MALIEV_BILLING_CANDIDATE_HEAD", "\\A[a-f0-9]{40}\\z"),
                runId = Coordinate("GITHUB_RUN_ID", "\\A[1-9][0-9]*\\z"),
                runAttempt = Coordinate("GITHUB_RUN_ATTEMPT", "\\A[1-9][0-9]*\\z"),
                comparisonMode = Environment.GetEnvironmentVariable("BILLING_COMPARISON_MODE") is "focused" ? "focused"
                    : Environment.GetEnvironmentVariable("BILLING_COMPARISON_MODE") is "collected" ? "collected" : null,
                observerStopped = true,
                httpTimeoutSeconds = 15,
                maximumEvents = MaximumEvents,
                maximumTotalEvents = MaximumEvents * 2,
                dropped,
                observationFailures,
                observations = snapshot,
                providerObservations = providerSnapshot,
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (receipt.Length > MaximumReceiptBytes) return;
            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "billing-persistence", "initial-readiness");
            Directory.CreateDirectory(directory);
            // Fresh invocation path never collides with or merges the en/th business receipts.
            using var output = new FileStream(Path.Combine(directory, culture + "-" + invocation + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(receipt);
        }
        catch (Exception)
        {
            active = false;
            // Missing diagnostics are not a pass. Original startup/GET/assertion and graph cleanup still determine the test result.
        }
    }
}
