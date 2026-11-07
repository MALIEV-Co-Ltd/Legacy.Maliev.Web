using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Passive, bounded observations for this fixture's first quotation document only.</summary>
internal sealed class SummaryInitialRequestDiagnostics : IStartupFilter
{
    private const int MaximumEvents = 32;
    private readonly object gate = new();
    private readonly object requestKey = new();
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly List<Observation> observations = [];
    private bool active = true;
    private int dropped;

    internal enum Stage
    {
        HostStartInvoked, HostStartReturned, NavigationInvoked, NavigationReturned, NavigationFailed,
        PipelineEntered, HeadersStarted, ResponseCompleted, PipelineReturned, PipelineFailed,
        SessionCreateEntered, SessionCreateReturned, SessionCreateFailed,
        SessionReadEntered, SessionReadReturned, SessionReadFailed,
        CountryEntered, CountryReturned, CountryFailed, ObservationStopped
    }

    private sealed record Observation(string Stage, long ElapsedTicks, int? Status, bool Canceled, string Failure);

    internal void Observe(Stage stage, int? status = null, bool canceled = false, Exception? failure = null)
    {
        try
        {
            lock (gate)
            {
                if (!active || !Enum.IsDefined(stage)) return;
                if (observations.Count == MaximumEvents)
                {
                    dropped = Math.Min(MaximumEvents, dropped + 1);
                    return;
                }

                observations.Add(new(stage.ToString(), Stopwatch.GetElapsedTime(started).Ticks,
                    status is >= 100 and <= 599 ? status : null, canceled, failure switch
                    {
                        null => "None",
                        OperationCanceledException => "Canceled",
                        HttpRequestException => "Http",
                        IOException => "Io",
                        _ => "Other"
                    }));
            }
        }
        catch (Exception) { /* Observation cannot replace the original operation's result. */ }
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            if (!Attach(context))
            {
                await continuation(context);
                return;
            }

            Observe(Stage.PipelineEntered);
            try
            {
                context.Response.OnStarting(() => { ObserveResponse(context, Stage.HeadersStarted); return Task.CompletedTask; });
                context.Response.OnCompleted(() => { ObserveResponse(context, Stage.ResponseCompleted); return Task.CompletedTask; });
            }
            catch (Exception) { /* Callback registration is diagnostic only. */ }

            try { await continuation(context); }
            catch (Exception failure) { Observe(Stage.PipelineFailed, failure: failure); throw; }
            finally { ObserveResponse(context, Stage.PipelineReturned); }
        });
        next(app);
    };

    private bool Attach(HttpContext context)
    {
        try
        {
            lock (gate)
            {
                if (!active || !HttpMethods.IsGet(context.Request.Method)
                    || !context.Request.Path.Equals(new PathString("/instantquotation/3d-printing"), StringComparison.OrdinalIgnoreCase))
                    return false;
                context.Items[requestKey] = this;
                return true;
            }
        }
        catch (Exception) { return false; }
    }

    private bool Matches(IHttpContextAccessor accessor)
    {
        try
        {
            lock (gate)
            {
                return active && accessor.HttpContext is { } context
                    && context.Items.TryGetValue(requestKey, out var owner) && ReferenceEquals(owner, this);
            }
        }
        catch (Exception) { return false; }
    }

    private void ObserveResponse(HttpContext context, Stage stage)
    {
        try { Observe(stage, context.Response.StatusCode, context.RequestAborted.IsCancellationRequested); }
        catch (Exception) { /* Faulting response features must not alter the request. */ }
    }

    internal async Task<T> ForwardAsync<T>(IHttpContextAccessor accessor,
        Stage entered, Stage returned, Stage failed, Func<Task<T>> operation)
    {
        var observed = Matches(accessor);
        if (observed) Observe(entered);
        try
        {
            var result = await operation();
            if (observed) Observe(returned);
            return result;
        }
        catch (Exception failure)
        {
            if (observed) Observe(failed, failure: failure);
            throw;
        }
    }

    internal object StopAndSnapshot()
    {
        lock (gate)
        {
            Observe(Stage.ObservationStopped);
            active = false;
            return new
            {
                schemaVersion = 1,
                maximumEvents = MaximumEvents,
                dropped,
                observations = observations.Select(item => new
                {
                    stage = item.Stage,
                    elapsedTicks = item.ElapsedTicks,
                    status = item.Status,
                    canceled = item.Canceled,
                    failure = item.Failure
                }).ToArray()
            };
        }
    }

    internal static void TryWrite(Action write)
    {
        try { write(); }
        catch (Exception) { /* Secondary receipt failure cannot replace the actual navigation failure. */ }
    }
}
