using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Observes only the original first-login GET without changing its client or pipeline.</summary>
internal sealed class WebSurfaceLoginPhaseObservation : IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private const string Begin = "Microsoft.AspNetCore.Hosting.BeginRequest";
    private const string Matched = "Microsoft.AspNetCore.Routing.EndpointMatched";
    private const string End = "Microsoft.AspNetCore.Hosting.EndRequest";
    private const string Fault = "Microsoft.AspNetCore.Hosting.UnhandledException";
    private readonly object gate = new();
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly ITestOutputHelper? output;
    private readonly List<string> phases = [];
    private IDisposable? subscription;
    private CancellationTokenRegistration abortRegistration;
    private HttpContext? observedContext;
    private bool disposed;

    internal WebSurfaceLoginPhaseObservation(DiagnosticListener? listener, ITestOutputHelper? output)
    {
        this.output = output;
        if (listener is null) { Record("observer-unavailable"); return; }
        try { subscription = listener.Subscribe(this, name => name is Begin or Matched or End or Fault); }
        catch (Exception exception) { Record("observer-fault", exceptionType: exception.GetType().FullName); }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            var context = value.Value as HttpContext
                ?? value.Value?.GetType().GetProperty("httpContext")?.GetValue(value.Value) as HttpContext;
            if (context is null) return;
            lock (gate)
            {
                if (disposed) return;
                if (observedContext is null)
                {
                    if (value.Key != Begin || !HttpMethods.IsGet(context.Request.Method)
                        || !context.Request.Path.Equals("/account/login", StringComparison.OrdinalIgnoreCase)) return;
                    observedContext = context;
                    Record("pipeline-entered");
                    context.Response.OnStarting(() =>
                    {
                        Record("response-starting");
                        return Task.CompletedTask;
                    });
                    context.Response.OnCompleted(() =>
                    {
                        Record("response-completed");
                        return Task.CompletedTask;
                    });
                    abortRegistration = context.RequestAborted.Register(() => Record("request-aborted"));
                }
                if (!ReferenceEquals(observedContext, context)) return;
                if (value.Key == Matched)
                    Record("endpoint-selected", endpoint: context.GetEndpoint()?.DisplayName);
                else if (value.Key == End)
                    Record("pipeline-returned");
                else if (value.Key == Fault)
                {
                    var exception = value.Value?.GetType().GetProperty("exception")?.GetValue(value.Value) as Exception;
                    Record("pipeline-faulted", exceptionType: exception?.GetType().FullName);
                }
            }
        }
        catch (Exception exception) { Record("observer-fault", exceptionType: exception.GetType().FullName); }
    }

    public void OnError(Exception error) => Record("observer-fault", exceptionType: error.GetType().FullName);

    public void OnCompleted() { }

    internal void WriteFailure(Exception primary)
    {
        try
        {
            Record("client-faulted", exceptionType: primary.GetType().FullName);
            string[] snapshot;
            lock (gate) snapshot = phases.ToArray();
            foreach (var phase in snapshot) Write(phase);
        }
        catch (Exception) { /* Observation must not replace the original test failure. */ }
    }

    private void Record(string phase, string? endpoint = null, string? exceptionType = null)
    {
        try
        {
            lock (gate)
            {
                if (disposed) return;
                phases.Add(string.Format(CultureInfo.InvariantCulture,
                    "login-get-phase elapsed_ms={0:F3} phase={1} endpoint={2} exception_type={3}",
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, phase, endpoint ?? "none", exceptionType ?? "none"));
            }
        }
        catch (Exception) { /* Diagnostic callbacks must not change response completion. */ }
    }

    private void Write(string phase)
    {
        if (output is not null) output.WriteLine(phase);
        else Console.Error.WriteLine(phase);
    }

    public void Dispose()
    {
        IDisposable? detached;
        CancellationTokenRegistration aborted;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            detached = subscription;
            subscription = null;
            aborted = abortRegistration;
            abortRegistration = default;
            observedContext = null;
        }
        // Dispose outside the callback lock so an in-flight abort can finish.
        try { detached?.Dispose(); }
        catch (Exception exception) { WriteCleanupFailure(exception); }
        try { aborted.Dispose(); }
        catch (Exception exception) { WriteCleanupFailure(exception); }
    }

    private void WriteCleanupFailure(Exception exception)
    {
        try
        {
            Write(string.Format(CultureInfo.InvariantCulture,
                "login-get-phase elapsed_ms={0:F3} phase=observer-dispose-fault endpoint=none exception_type={1}",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception.GetType().FullName));
        }
        catch (Exception) { /* Cleanup reporting must not replace the original test failure. */ }
    }
}
