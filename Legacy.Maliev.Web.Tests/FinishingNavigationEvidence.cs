using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Passive, bounded navigation observations containing only fixed public categories.</summary>
internal sealed class FinishingNavigationEvidence : IDisposable
{
    private static readonly HashSet<string> PublicPaths = new(StringComparer.Ordinal)
    {
        "/services/finishing-and-color", "/dist/site.min.css", "/dist/vendor.min.js", "/dist/app.min.js",
    };
    private readonly object gate = new();
    private readonly Uri origin;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<object, PendingRequest> pending = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> types = new(StringComparer.Ordinal);
    private readonly List<Action> detach = [];
    private int started;
    private int finished;
    private int failed;
    private int cleanupFailures;
    private bool truncated;
    private bool acquisitionFailed;
    private bool observationFailed;
    private bool stopped;
    private bool domContentLoaded;
    private bool loaded;
    private object? mainRequest;
    private int? mainResponseStatus;
    private long? mainResponseElapsedMs;
    private bool mainDocumentFinished;

    internal FinishingNavigationEvidence(IPage page, Uri origin)
    {
        this.origin = origin;
        EventHandler<IRequest> request = (_, value) => Observe(() => RecordRequest(value, value.ResourceType,
            value.Url, value.IsNavigationRequest && ReferenceEquals(value.Frame, page.MainFrame)));
        EventHandler<IRequest> requestFinished = (_, value) => Observe(() => Complete(value, false));
        EventHandler<IRequest> requestFailed = (_, value) => Observe(() => Complete(value, true));
        EventHandler<IResponse> response = (_, value) => Observe(() =>
        {
            lock (gate)
            {
                if (!stopped && ReferenceEquals(mainRequest, value.Request))
                {
                    mainResponseStatus = value.Status is >= 100 and <= 599 ? value.Status : null;
                    mainResponseElapsedMs = Math.Min(clock.ElapsedMilliseconds, 600000);
                }
            }
        });
        EventHandler<IPage> dom = (_, _) => Observe(() => { lock (gate) { if (!stopped) domContentLoaded = true; } });
        EventHandler<IPage> load = (_, _) => Observe(() => { lock (gate) { if (!stopped) loaded = true; } });
        try
        {
            // Register undo before invoking each accessor, including accessors that attach then throw.
            detach.Add(() => page.Request -= request);
            page.Request += request;
            detach.Add(() => page.RequestFinished -= requestFinished);
            page.RequestFinished += requestFinished;
            detach.Add(() => page.RequestFailed -= requestFailed);
            page.RequestFailed += requestFailed;
            detach.Add(() => page.Response -= response);
            page.Response += response;
            detach.Add(() => page.DOMContentLoaded -= dom);
            page.DOMContentLoaded += dom;
            detach.Add(() => page.Load -= load);
            page.Load += load;
        }
        catch
        {
            acquisitionFailed = true;
            Dispose();
        }
    }

    internal static string Classify(Uri origin, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "<other>";
        if (!string.Equals(uri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != origin.Port)
            return "<third-party>";
        return PublicPaths.Contains(uri.AbsolutePath) ? uri.AbsolutePath : "<other>";
    }

    internal void RecordRequest(object identity, string resourceType, string url, bool mainDocument)
    {
        lock (gate)
        {
            if (stopped) return;
            started = Math.Min(started + 1, 9999);
            var type = resourceType is "document" or "stylesheet" or "script" or "image" or "font"
                or "xhr" or "fetch" ? resourceType : "other";
            types[type] = Math.Min(types.GetValueOrDefault(type) + 1, 9999);
            if (mainDocument && !ReferenceEquals(mainRequest, identity))
            {
                mainRequest = identity;
                mainResponseStatus = null;
                mainResponseElapsedMs = null;
                mainDocumentFinished = false;
            }
            if (pending.Count >= 32) { truncated = true; return; }
            pending[identity] = new(type, Classify(origin, url));
        }
    }

    private void Complete(object identity, bool failure)
    {
        lock (gate)
        {
            if (stopped) return;
            if (failure) failed = Math.Min(failed + 1, 9999);
            else finished = Math.Min(finished + 1, 9999);
            if (ReferenceEquals(mainRequest, identity)) mainDocumentFinished = !failure;
            pending.Remove(identity);
        }
    }

    private void Observe(Action observation)
    {
        try { observation(); }
        catch { lock (gate) observationFailed = true; }
    }

    internal void Report(Action<string> sink)
    {
        try
        {
            string json;
            lock (gate)
            {
                json = JsonSerializer.Serialize(new
                {
                    Schema = "finishing-navigation.v1",
                    ElapsedMs = Math.Min(clock.ElapsedMilliseconds, 600000),
                    MainResponseStatus = mainResponseStatus,
                    MainResponseElapsedMs = mainResponseElapsedMs,
                    MainDocumentFinished = mainDocumentFinished,
                    DomContentLoaded = domContentLoaded,
                    Loaded = loaded,
                    Started = started,
                    Finished = finished,
                    Failed = failed,
                    RequestTypes = types,
                    Pending = pending.Values.ToArray(),
                    Truncated = truncated,
                    AcquisitionFailed = acquisitionFailed,
                    ObservationFailed = observationFailed,
                    CleanupFailures = cleanupFailures,
                    RemainingSubscriptions = detach.Count,
                });
            }
            if (json.Length <= 8192) sink(json);
        }
        catch { /* A diagnostic sink cannot replace a navigation exception. */ }
    }

    public void Dispose()
    {
        lock (gate) stopped = true;
        // A bounded second pass settles transient accessor failures without replacing the primary exception.
        for (var pass = 0; pass < 2 && detach.Count != 0; pass++)
        {
            for (var index = detach.Count - 1; index >= 0; index--)
            {
                try { detach[index](); detach.RemoveAt(index); }
                catch { lock (gate) cleanupFailures = Math.Min(cleanupFailures + 1, 9999); }
            }
        }
        clock.Stop();
    }

    private sealed record PendingRequest(string Type, string Path);
}
