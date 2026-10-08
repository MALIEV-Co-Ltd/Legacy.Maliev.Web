using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

// Failure-only, initial-navigation evidence. Never retain headers, queries, bodies or console text.
internal sealed class InstantQuotationNavigationFailureDiagnostics : IDisposable
{
    private readonly IPage page;
    private readonly NavigationRequestLedger ledger;

    internal InstantQuotationNavigationFailureDiagnostics(IPage page, Uri origin)
    {
        this.page = page;
        ledger = new(origin);
        page.Request += Started;
        page.Response += Responded;
        page.RequestFinished += Finished;
        page.RequestFailed += Failed;
    }

    private void Started(object? sender, IRequest request) =>
        ledger.Start(request, request.Url, request.Method, request.ResourceType);
    private void Responded(object? sender, IResponse response) => ledger.Respond(response.Request, response.Status);
    private void Finished(object? sender, IRequest request) => ledger.End(request, "finished");
    private void Failed(object? sender, IRequest request) => ledger.End(request, "failed", request.Failure);

    internal static async Task<T> PreserveFailureAsync<T>(Func<Task<T>> operation, Func<Task> observeFailure)
    {
        try { return await operation(); }
        catch
        {
            try { await observeFailure(); }
            catch { /* Diagnostic failures must never replace the original navigation exception. */ }
            throw;
        }
    }

    internal async Task WriteFailureAsync(ITestOutputHelper output, object boundary)
    {
        JsonElement? renderer = null;
        var rendererState = "unavailable";
        var networkAtFailure = ledger.Snapshot();
        if (!page.IsClosed)
        {
            try
            {
                var pending = page.EvaluateAsync<JsonElement>("""
                () => ({readyState:['loading','interactive','complete'].includes(document.readyState)?document.readyState:'other',
                  quotationPath:location.pathname.toLowerCase()==='/instantquotation/3d-printing',
                  titlePresent:document.title.length>0, blazorPresent:!!window.Blazor,
                  fileInputPresent:!!document.querySelector('#instant-quote-files'),
                  consentPresent:!!document.querySelector('#cookieConsent'),
                  partCount:Math.min(32,document.querySelectorAll('[data-workflow-part]').length),
                  blazorErrorVisible:(()=>{const e=document.querySelector('#blazor-error-ui');
                    return !!e&&getComputedStyle(e).display!=='none';})(),
                  workerStarted:Math.min(32,window.__wallAcceptance?.started||0),
                  workerErrorCount:Math.min(32,window.__wallAcceptance?.errors?.length||0)})
                """);
                // Observe a late driver fault after the bounded wait; the owning page/context/browser still dispose.
                _ = pending.ContinueWith(task => { _ = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                renderer = await pending.WaitAsync(TimeSpan.FromSeconds(2));
                rendererState = "captured";
            }
            catch { /* Keep fixed metadata only; never retain driver/renderer exception text. */ }
        }
        output.WriteLine("WEB463_INITIAL_NAVIGATION_FAILURE " + JsonSerializer.Serialize(new
        {
            boundary,
            rendererState,
            renderer,
            network = networkAtFailure,
        }));
    }

    public void Dispose()
    {
        page.Request -= Started;
        page.Response -= Responded;
        page.RequestFinished -= Finished;
        page.RequestFailed -= Failed;
    }
}

internal sealed class NavigationRequestLedger(Uri origin)
{
    private const int MaximumRequests = 32;
    private readonly object gate = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly Dictionary<object, NavigationRequestObservation> requests = new(ReferenceEqualityComparer.Instance);
    private int observed;
    private int omitted;

    internal void Start(object identity, string url, string method, string resourceType)
    {
        lock (gate)
        {
            observed++;
            if (requests.Count == MaximumRequests) { omitted++; return; }
            requests.TryAdd(identity, new(observed, SafePath(origin, url),
                method is "GET" or "POST" or "HEAD" or "OPTIONS" ? method : "other",
                resourceType is "document" or "stylesheet" or "script" or "image" or "font" or "xhr" or "fetch"
                    or "websocket" or "eventsource" or "manifest" or "media" ? resourceType : "other",
                elapsed.ElapsedMilliseconds, null, null, "pending", null));
        }
    }

    internal void Respond(object identity, int status)
    {
        lock (gate)
            if (requests.TryGetValue(identity, out var item))
                requests[identity] = item with { Status = status is >= 100 and <= 599 ? status : null };
    }

    internal void End(object identity, string outcome, string? failure = null)
    {
        lock (gate)
            if (requests.TryGetValue(identity, out var item))
                requests[identity] = item with
                {
                    EndedMs = elapsed.ElapsedMilliseconds,
                    Outcome = outcome == "finished" ? "finished" : "failed",
                    FailureCode = outcome == "finished" ? null : SafeFailureCode(failure),
                };
    }

    internal NavigationNetworkSnapshot Snapshot()
    {
        lock (gate)
            return new(observed, omitted, requests.Values.OrderBy(item => item.Number).ToArray());
    }

    internal static string SafeFailureCode(string? failure) => failure switch
    {
        "net::ERR_ABORTED" or "net::ERR_FAILED" or "net::ERR_TIMED_OUT" or "net::ERR_CONNECTION_REFUSED"
            or "net::ERR_CONNECTION_CLOSED" or "net::ERR_NAME_NOT_RESOLVED" or "net::ERR_EMPTY_RESPONSE"
            or "net::ERR_BLOCKED_BY_CLIENT" or "net::ERR_INTERNET_DISCONNECTED" => failure,
        null => "unspecified",
        _ => "other",
    };

    internal static string SafePath(Uri origin, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "invalid";
        if (uri.Scheme != origin.Scheme || uri.Host != origin.Host || uri.Port != origin.Port || uri.UserInfo.Length != 0)
            return "cross-origin";
        // Exact public bootstrap assets only. Unknown same-origin paths may contain capabilities/content.
        return uri.AbsolutePath switch
        {
            "/instantquotation/3d-printing" or "/_blazor" or "/_blazor/negotiate" or "/_framework/blazor.web.js"
                or "/instant-quotation/fdm-profiles.v1.json" or "/dist/site.min.css" or "/dist/vendor.min.js"
                or "/dist/app.min.js" or "/dist/route-instant-quotation.css" or "/dist/route-instant-quotation.js"
                or "/dist/instant-quotation-viewer.mjs" or "/dist/instant-quotation-workflow.mjs" => uri.AbsolutePath,
            _ => "same-origin-other",
        };
    }
}

internal sealed record NavigationNetworkSnapshot(int Observed, int Omitted, NavigationRequestObservation[] Requests);
internal sealed record NavigationRequestObservation(int Number, string Path, string Method, string ResourceType,
    long StartedMs, long? EndedMs, int? Status, string Outcome, string? FailureCode);
