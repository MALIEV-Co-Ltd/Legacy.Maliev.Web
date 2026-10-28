using System.Diagnostics;
using System.Globalization;
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
    private readonly Uri origin;
    private readonly TestResponseFailureEvidence? serverProbe;
    private readonly string phase;
    private readonly string waitState;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset clockOriginUtc = DateTimeOffset.UtcNow;
    private readonly List<Milestone> milestones = [];
    private bool milestonesTruncated;
    private bool inputFileReadyObserved;

    internal MaterialInitialNavigationDiagnostics(IPage page, Uri origin,
        TestResponseFailureEvidence? serverProbe = null, string phase = "initial", string waitState = "networkidle")
    {
        if (phase is not ("initial" or "reload")) throw new ArgumentOutOfRangeException(nameof(phase));
        if (waitState is not ("networkidle" or "domcontentloaded")) throw new ArgumentOutOfRangeException(nameof(waitState));
        this.origin = origin;
        this.serverProbe = serverProbe;
        this.phase = phase;
        this.waitState = waitState;
        ledger = new(origin);
        var accessors = new List<(Action Add, Action Remove)>
        {
            (() => page.Request += Started, () => page.Request -= Started),
            (() => page.Response += Responded, () => page.Response -= Responded),
            (() => page.RequestFinished += Finished, () => page.RequestFinished -= Finished),
            (() => page.RequestFailed += Failed, () => page.RequestFailed -= Failed),
            (() => page.DOMContentLoaded += DomLoaded, () => page.DOMContentLoaded -= DomLoaded),
            (() => page.Load += Loaded, () => page.Load -= Loaded),
        };
        if (serverProbe is not null)
            accessors.Add((() => page.Console += ConsoleMessage, () => page.Console -= ConsoleMessage));
        foreach (var accessor in accessors)
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
        Observe(() =>
        {
            ledger.Start(request, request.Url, request.Method, request.ResourceType);
            RecordNetworkMilestone(request, "started");
        });
    private void Responded(object? sender, IResponse response) =>
        Observe(() =>
        {
            ledger.Respond(response.Request, response.Status);
            RecordNetworkMilestone(response.Request, "response", response.Status);
        });
    private void Finished(object? sender, IRequest request) =>
        Observe(() => ledger.End(request, "finished"));
    private void Failed(object? sender, IRequest request) =>
        Observe(() => ledger.End(request, "failed", request.Failure));
    private void DomLoaded(object? sender, IPage page) => Observe(() =>
    {
        domContentLoadedObserved = true;
        RecordMilestone("document", "dom-content-loaded");
    });
    private void Loaded(object? sender, IPage page) => Observe(() =>
    {
        loadObserved = true;
        RecordMilestone("document", "loaded");
    });

    internal void ObserveInputFileReady() => Observe(() =>
    {
        inputFileReadyObserved = true;
        RecordMilestone("input-file", "ready");
    });

    private void RecordNetworkMilestone(IRequest request, string kind, int? status = null)
    {
        if (serverProbe is null) return;
        var category = NavigationRequestLedger.SafePath(origin, request.Url) switch
        {
            "/instantquotation/3d-printing" => "document",
            "/_framework/blazor.web.js" => "framework",
            "/_blazor" or "/_blazor/negotiate" => "interactive-transport",
            _ => null,
        };
        if (category is not null) RecordMilestone(category, kind,
            status: status is >= 100 and <= 599 ? status : null);
    }

    private void ConsoleMessage(object? sender, IConsoleMessage message) => Observe(() =>
    {
        if (message.Type != "error") return;
        var text = message.Text;
        DateTimeOffset? browserEmittedUtc = null;
        if (text.Length >= 26 && text[0] == '[' && text[25] == ']'
            && DateTimeOffset.TryParseExact(text.Substring(1, 24), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            browserEmittedUtc = parsed;
        RecordMilestone(BrowserFailureEvidence.Category(text), "console-error", browserEmittedUtc);
    });

    private void RecordMilestone(string category, string kind,
        DateTimeOffset? browserEmittedUtc = null, int? status = null)
    {
        if (serverProbe is null) return;
        if (milestones.Count == 16) { milestonesTruncated = true; return; }
        milestones.Add(new(category, kind,
            Math.Min((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, 86_400_000),
            DateTimeOffset.UtcNow, browserEmittedUtc, status));
    }

    private void ObserveServer(Action<TestResponseFailureEvidence> observation)
    {
        if (serverProbe is null) return;
        try { observation(serverProbe); }
        catch { lock (gate) observerFaulted = true; }
    }

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation, Action<string> emit)
    {
        ObserveServer(probe => probe.Record(TestResponsePhase.ClientStarted));
        try
        {
            var result = await operation();
            ObserveServer(probe => probe.Record(TestResponsePhase.ClientReturned));
            return result;
        }
        catch
        {
            ObserveServer(probe => probe.Record(TestResponsePhase.ClientFailed));
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
                        InputFileReadyObserved = inputFileReadyObserved,
                        Milestones = milestones.ToArray(),
                        MilestonesTruncated = milestonesTruncated,
                        Server = serverProbe is null ? (JsonElement?)null
                            : JsonSerializer.Deserialize<JsonElement>(serverProbe.Snapshot()),
                    };
                }
                emit((serverProbe is null ? "material-initial-navigation " : "quotation-navigation ")
                    + JsonSerializer.Serialize(new
                    {
                        SchemaVersion = 1,
                        Phase = phase,
                        WaitState = waitState,
                        ClockDomain = "managed-callback-arrival",
                        ClockOriginUtc = clockOriginUtc,
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
        ObserveServer(probe => probe.Close());
    }

    private sealed record Milestone(string Category, string Event, long ElapsedMilliseconds,
        DateTimeOffset CallbackUtc, DateTimeOffset? BrowserEmittedUtc, int? Status);
}
