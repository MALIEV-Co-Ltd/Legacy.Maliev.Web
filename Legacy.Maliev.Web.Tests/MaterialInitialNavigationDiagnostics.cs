using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Passive first-navigation observations over the existing bounded request ledger.</summary>
internal sealed class MaterialInitialNavigationDiagnostics : IDisposable
{
    private readonly object gate = new();
    private readonly NavigationRequestLedger ledger;
    private readonly List<Action> undo = [];
    private bool stopped;
    private bool domContentLoadedObserved;
    private bool loadObserved;
    private bool observerFaulted;

    internal MaterialInitialNavigationDiagnostics(IPage page, Uri origin)
    {
        ledger = new(origin);
        foreach (var accessor in new (Action Add, Action Remove)[]
        {
            (() => page.Request += Started, () => page.Request -= Started),
            (() => page.Response += Responded, () => page.Response -= Responded),
            (() => page.RequestFinished += Finished, () => page.RequestFinished -= Finished),
            (() => page.RequestFailed += Failed, () => page.RequestFailed -= Failed),
            (() => page.DOMContentLoaded += DomLoaded, () => page.DOMContentLoaded -= DomLoaded),
            (() => page.Load += Loaded, () => page.Load -= Loaded),
        })
        {
            // Record undo before add: an event accessor can attach and then throw.
            lock (gate) undo.Add(accessor.Remove);
            try { accessor.Add(); }
            catch
            {
                lock (gate) observerFaulted = true;
                Dispose();
                break;
            }
        }
    }

    internal int PendingSubscriptions { get { lock (gate) return undo.Count; } }

    private void Observe(Action observation)
    {
        try { lock (gate) { if (!stopped) observation(); } }
        catch { lock (gate) observerFaulted = true; }
    }

    private void Started(object? sender, IRequest request) =>
        Observe(() => ledger.Start(request, request.Url, request.Method, request.ResourceType));
    private void Responded(object? sender, IResponse response) =>
        Observe(() => ledger.Respond(response.Request, response.Status));
    private void Finished(object? sender, IRequest request) =>
        Observe(() => ledger.End(request, "finished"));
    private void Failed(object? sender, IRequest request) =>
        Observe(() => ledger.End(request, "failed", request.Failure));
    private void DomLoaded(object? sender, IPage page) => Observe(() => domContentLoadedObserved = true);
    private void Loaded(object? sender, IPage page) => Observe(() => loadObserved = true);

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation, Action<string> emit)
    {
        try { return await operation(); }
        catch
        {
            try
            {
                Dispose();
                object snapshot;
                lock (gate)
                {
                    snapshot = new
                    {
                        DomContentLoadedObserved = domContentLoadedObserved,
                        LoadObserved = loadObserved,
                        ObserverFaulted = observerFaulted,
                        PendingSubscriptions = undo.Count,
                        Network = ledger.Snapshot(),
                    };
                }
                emit("material-initial-navigation " + JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    Phase = "initial",
                    WaitState = "networkidle",
                    ClockDomain = "managed-callback-arrival",
                    MilestoneSemantics = "unobserved-is-not-absence",
                    Snapshot = snapshot,
                }));
            }
            catch { /* Secondary observation/output never replaces the exact navigation exception. */ }
            throw;
        }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        Action[] remaining;
        lock (gate)
        {
            stopped = true;
            remaining = undo.ToArray();
        }
        foreach (var remove in remaining)
        {
            try
            {
                remove();
                lock (gate) undo.Remove(remove);
            }
            catch
            {
                // Retain exact failed undo for the owner's next Dispose attempt.
                // The original page/context still owns attached delegates until its existing teardown.
                lock (gate) observerFaulted = true;
            }
        }
    }
}
