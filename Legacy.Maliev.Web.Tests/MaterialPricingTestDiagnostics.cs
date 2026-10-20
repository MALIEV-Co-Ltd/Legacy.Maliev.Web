using System.Diagnostics;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

internal sealed class MaterialPricingTestDiagnostics(IInstantQuotationAuthoritativePricingService inner,
    TaskScheduler? observationScheduler = null) : IInstantQuotationAuthoritativePricingService
{
    private readonly object sync = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly List<Task> quotes = [];
    private readonly List<Task> callbacks = [];
    private readonly List<Task> observations = [];
    private readonly List<CancellationTokenRegistration> subscriptions = [];
    private readonly List<object> events = [];
    private int omitted;

    public Task[] OwnedTasks { get { lock (sync) return quotes.ToArray(); } }
    public Task[] CallbackTasks { get { lock (sync) return callbacks.ToArray(); } }
    public Task[] ObservationTasks { get { lock (sync) return observations.ToArray(); } }
    public object Snapshot() { lock (sync) return new { Events = events.ToArray(), Omitted = omitted }; }
    public void DisposeSubscriptions()
    {
        CancellationTokenRegistration[] owned;
        lock (sync) owned = subscriptions.ToArray();
        foreach (var subscription in owned) subscription.Dispose();
    }
    public void Record(string phase, string state, bool canceled = false)
    {
        lock (sync)
        {
            if (events.Count < 128) events.Add(new { Phase = phase, State = state, Canceled = canceled, ElapsedMilliseconds = elapsed.ElapsedMilliseconds });
            else omitted++;
        }
    }

    public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
        string? ownerIdentity, bool includeComparisons, CancellationToken cancellationToken) =>
        Start(() => inner.QuoteAsync(session, ownerIdentity, includeComparisons, cancellationToken), "plain", cancellationToken);

    public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
        string? ownerIdentity, bool includeComparisons,
        Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
        CancellationToken cancellationToken) => Start(() => inner.QuoteAsync(session, ownerIdentity, includeComparisons,
            (frame, token) =>
            {
                bool selected = session.Parts.Any(part => part.PartId == frame.PartId
                    && string.Equals(part.Configuration.MaterialKey, frame.MaterialKey, StringComparison.OrdinalIgnoreCase));
                string phase = selected ? "selected-callback" : "comparison-callback";
                Record(phase, "entered-" + frame.Status, token.IsCancellationRequested);
                var registration = Subscribe(token, phase);
                ValueTask actual;
                try { actual = observer(frame, token); }
                catch { registration.Dispose(); Record(phase, "faulted", token.IsCancellationRequested); throw; }
                if (actual.IsCompleted)
                {
                    Record(phase, actual.IsCompletedSuccessfully ? "returned" : actual.IsCanceled ? "canceled" : "faulted", token.IsCancellationRequested);
                    registration.Dispose();
                    return actual;
                }
                Task callback = actual.AsTask();
                lock (sync) callbacks.Add(callback);
                Observe(callback, phase, token, registration);
                return new ValueTask(callback);
            }, cancellationToken), "display", cancellationToken);

    private Task<InstantQuotationOrderQuote?> Start(Func<Task<InstantQuotationOrderQuote?>> start,
        string phase, CancellationToken caller)
    {
        Record(phase, "started", caller.IsCancellationRequested);
        Task<InstantQuotationOrderQuote?> actual = start();
        lock (sync) quotes.Add(actual);
        Observe(actual, phase, caller, Subscribe(caller, phase));
        return actual; // Return the exact producer task, not a diagnostic forwarding task.
    }

    private CancellationTokenRegistration Subscribe(CancellationToken token, string phase)
    {
        var registration = token.Register(() => Record(phase, "canceled", true));
        lock (sync) subscriptions.Add(registration);
        return registration;
    }
    private void Observe(Task actual, string phase, CancellationToken token, CancellationTokenRegistration registration)
    {
        Task observation = actual.ContinueWith(completed =>
        {
            try
            {
                _ = completed.Exception;
                Record(phase, completed.Status.ToString(), token.IsCancellationRequested);
            }
            finally { registration.Dispose(); }
        }, CancellationToken.None, TaskContinuationOptions.None, observationScheduler ?? TaskScheduler.Default);
        lock (sync) observations.Add(observation);
    }
}
