using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Tests;

// Fixture-only counts; no request, exception, header, body or identity data is retained.
internal sealed class SummaryServerNavigationEvidence : IStartupFilter
{
    private readonly object gate = new();
    private int entered;
    private int headersStarted;
    private int returned;
    private int faulted;
    private int aborted;
    private int pending;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(ObserveAsync);
        next(app);
    };

    internal async Task ObserveAsync(HttpContext context, RequestDelegate next)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || !string.Equals(context.Request.Path.Value, "/instantquotation/3d-printing", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        lock (gate)
        {
            entered = Increment(entered);
            pending = Increment(pending);
        }
        context.Response.OnStarting(() =>
        {
            lock (gate) headersStarted = Increment(headersStarted);
            return Task.CompletedTask;
        });
        try
        {
            await next(context);
            lock (gate) returned = Increment(returned);
        }
        catch
        {
            lock (gate) faulted = Increment(faulted);
            throw;
        }
        finally
        {
            lock (gate)
            {
                pending = Math.Max(0, pending - 1);
                if (context.RequestAborted.IsCancellationRequested) aborted = Increment(aborted);
            }
        }
    }

    internal SummaryServerNavigationSnapshot Snapshot()
    {
        lock (gate) return new(entered, headersStarted, returned, faulted, aborted, pending);
    }

    internal static SummaryRunnerSnapshot RunnerSnapshot() => new(
        Math.Clamp(GC.GetTotalMemory(false), 0, 1L << 40),
        Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 0, 1L << 40),
        Math.Clamp(ThreadPool.ThreadCount, 0, 65536),
        Math.Clamp(ThreadPool.PendingWorkItemCount, 0, 1L << 40));

    internal static void PreserveFailure(Action write)
    {
        try { write(); }
        catch { /* Observation must never replace a navigation or readiness failure. */ }
    }

    private static int Increment(int value) => Math.Min(65536, value + 1);
}

internal sealed record SummaryServerNavigationSnapshot(int Entered, int HeadersStarted, int Returned,
    int Faulted, int Aborted, int Pending);

internal sealed record SummaryRunnerSnapshot(long ManagedBytes, long GcAvailableBytes,
    int ThreadPoolThreads, long PendingWorkItems);
