using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Failure-only categorical browser evidence; never retains transport or customer payloads.</summary>
internal sealed class BrowserFailureEvidence : IDisposable, IAsyncDisposable
{
    internal const string DataKey = "BrowserFailureEvidence";
    private readonly IPage page;
    private readonly object gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<Entry> events = [];
    private readonly List<IWebSocket> sockets = [];
    private readonly List<Subscription> subscriptions = [];
    private readonly List<Task> observations = [];
    private readonly Action<string>? cleanupSink;
    private Task? pageClose;
    private bool acquisitionFailed;
    private string stage = "Navigation";
    private bool disposed;
    private bool truncated;

    internal BrowserFailureEvidence(IPage page, Action<string>? cleanupSink = null)
    {
        this.page = page;
        this.cleanupSink = cleanupSink;
        lock (gate)
        {
            Acquire([
                (() => page.Console += OnConsole, () => page.Console -= OnConsole),
                (() => page.PageError += OnPageError, () => page.PageError -= OnPageError),
                (() => page.Request += OnRequest, () => page.Request -= OnRequest),
                (() => page.RequestFailed += OnRequestFailed, () => page.RequestFailed -= OnRequestFailed),
                (() => page.Response += OnResponse, () => page.Response -= OnResponse),
                (() => page.WebSocket += OnSocket, () => page.WebSocket -= OnSocket),
            ]);
        }
    }

    internal void Stage(string fixedStage)
    {
        if (fixedStage is not ("Navigation" or "Reload" or "PartsRail" or "Review" or "CustomerInput" or "UploadAdmission"))
            throw new ArgumentOutOfRangeException(nameof(fixedStage));
        lock (gate) { if (!disposed) stage = fixedStage; }
    }

    private void OnConsole(object? sender, IConsoleMessage message)
    {
        if (message.Type == "error") Record("console", Category(message.Text));
    }
    private void OnPageError(object? sender, string error) => Record("page-error", Category(error));
    private void OnRequest(object? sender, IRequest request)
    {
        if (IsBlazor(request.Url)) Record("request", "blazor");
    }
    private void OnRequestFailed(object? sender, IRequest request)
    {
        if (IsBlazor(request.Url)) Record("request-failed", "blazor");
    }
    private void OnResponse(object? sender, IResponse response)
    {
        if (IsBlazor(response.Url)) Record("response", "blazor", response.Status is >= 100 and <= 599 ? response.Status : null);
    }
    private void OnSocket(object? sender, IWebSocket socket)
    {
        if (!IsBlazor(socket.Url)) return;
        lock (gate)
        {
            if (disposed) return;
            if (sockets.Count >= 32) { truncated = true; return; }
            if (sockets.Contains(socket)) return;
            sockets.Add(socket);
            if (!Acquire([
                (() => socket.Close += OnSocketClose, () => socket.Close -= OnSocketClose),
                (() => socket.SocketError += OnSocketError, () => socket.SocketError -= OnSocketError),
            ])) return;
        }
        Record("socket-open", "blazor");
    }
    private void OnSocketClose(object? sender, IWebSocket socket) => Record("socket-close", "blazor");
    private void OnSocketError(object? sender, string error) => Record("socket-error", "blazor");

    internal static bool IsBlazor(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "ws" or "wss"
        && uri.AbsolutePath is "/_blazor" or "/_blazor/negotiate";
    internal static string Category(string text) => text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
        ? "timeout" : text.Contains("disconnect", StringComparison.OrdinalIgnoreCase) ? "disconnect" : "other";

    private void Record(string kind, string category, int? status = null)
    {
        lock (gate)
        {
            if (disposed) return;
            if (events.Count >= 32) { truncated = true; return; }
            events.Add(new(stage, Math.Min(clock.ElapsedMilliseconds, 86_400_000), kind, category, status));
        }
    }

    internal async Task AttachAsync(Exception original, Func<Task<string>> observe, Action<string> emit)
    {
        object observation = new { State = "unavailable" };
        try
        {
            Task<string> pending;
            lock (gate)
            {
                RemoveSettledObservations();
                if (disposed || observations.Count >= 32) throw new InvalidOperationException();
                pending = observe() ?? throw new InvalidOperationException();
                observations.Add(pending);
                ObserveFault(pending);
            }
            observation = ParseObservation(await pending.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        catch { /* Secondary observation must not replace the original failure. */ }
        try
        {
            string json;
            lock (gate) json = JsonSerializer.Serialize(new { Events = events.ToArray(), Truncated = truncated, Observation = observation });
            original.Data[DataKey] = json;
            emit(json);
        }
        catch { /* An evidence sink is never an authority to alter the primary exception. */ }
    }

    internal static object ParseObservation(string json)
    {
        if (json.Length > 1024) throw new FormatException();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 3 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException();
        var result = new Dictionary<string, object>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            object value;
            if (property.Name == "workflow")
            {
                var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                value = text is "empty" or "uploading" or "uploaded" or "error" or "multipart" or "configured" or "review" or "customerdetails" or "submitted"
                    ? text : throw new FormatException();
            }
            else if (property.Name is "parts" or "uploadRows")
                value = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count) && count is >= 0 and <= 32
                    ? count : throw new FormatException();
            else if (property.Name is "progress" or "isRepricing" or "configurationEnabled" or "errorVisible")
                value = property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.Value.GetBoolean() : throw new FormatException();
            else throw new FormatException();
            if (!result.TryAdd(property.Name, value)) throw new FormatException();
        }
        if (!result.ContainsKey("workflow")) throw new FormatException();
        return result;
    }

    // Undo is recorded before acquisition: an accessor may attach and then throw.
    private bool Acquire((Action Add, Action Remove)[] accessors)
    {
        var acquired = new List<Subscription>();
        foreach (var accessor in accessors)
        {
            var subscription = new Subscription(accessor.Remove);
            subscriptions.Add(subscription);
            acquired.Add(subscription);
            try { accessor.Add(); }
            catch
            {
                acquisitionFailed = true;
                RemoveSubscriptions(acquired);
                ReportCleanup();
                return false;
            }
        }
        return true;
    }

    private void RemoveSubscriptions(IEnumerable<Subscription> selected)
    {
        foreach (var subscription in selected.ToArray())
        {
            try
            {
                subscription.Remove();
                subscriptions.Remove(subscription);
            }
            catch { /* Retain this exact undo reference for the next cleanup attempt. */ }
        }
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void RemoveSettledObservations()
    {
        foreach (var task in observations.Where(task => task.IsCompleted).ToArray())
        {
            if (task.IsFaulted) _ = task.Exception;
            observations.Remove(task);
        }
    }

    private void ReportCleanup()
    {
        try
        {
            RemoveSettledObservations();
            var closeState = pageClose is null ? "not-attempted" : !pageClose.IsCompleted ? "pending"
                : pageClose.IsCompletedSuccessfully ? "closed" : "failed";
            cleanupSink?.Invoke(JsonSerializer.Serialize(new
            {
                Kind = "browser-evidence-cleanup",
                State = subscriptions.Count != 0 || observations.Count != 0 || closeState is "pending" or "failed"
                    ? "unsettled" : closeState == "closed" ? "complete" : "detached",
                RemainingSubscriptions = subscriptions.Count,
                PendingObservations = observations.Count,
                PageClose = closeState,
                AcquisitionFailed = acquisitionFailed,
            }));
        }
        catch { /* No cleanup sink may replace the primary failure. */ }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            RemoveSubscriptions(subscriptions);
            if (subscriptions.Count == 0) sockets.Clear();
            ReportCleanup();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            // This is only the caller-owned test page, never another page/context/browser.
            // Its caller's page/context await-using remains the final owner after these bounded attempts.
            Task close;
            lock (gate)
            {
                if (pageClose is null || pageClose.IsFaulted || pageClose.IsCanceled)
                {
                    Task created;
                    try { created = page.CloseAsync(); }
                    catch { created = Task.FromException(new InvalidOperationException()); }
                    pageClose = created;
                    ObserveFault(created);
                }
                close = pageClose!;
            }
            try { await close.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* The retained exact close task still reports its actual completion state. */ }
            Task settled;
            lock (gate)
            {
                RemoveSettledObservations();
                settled = Task.WhenAll(observations.ToArray());
                ObserveFault(settled);
            }
            try { await settled.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch { /* Incomplete observations remain retained; an unsettled report is not settlement proof. */ }
        }
        catch { /* Diagnostic cleanup must never replace the primary exception. */ }
        finally
        {
            lock (gate)
            {
                RemoveSubscriptions(subscriptions);
                ReportCleanup();
                if (subscriptions.Count == 0) sockets.Clear();
            }
        }
    }

    private sealed class Subscription(Action remove)
    {
        internal Action Remove { get; } = remove;
    }
    private sealed record Entry(string Stage, long Milliseconds, string Kind, string Category, int? Status);
}
