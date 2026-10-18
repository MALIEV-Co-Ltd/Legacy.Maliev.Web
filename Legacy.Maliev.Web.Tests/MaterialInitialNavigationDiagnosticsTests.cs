using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actor-free controls for the first material-page navigation observation only.</summary>
public sealed class MaterialInitialNavigationDiagnosticsTests
{
    [Fact]
    public async Task SuccessfulNavigationReturnsTheSameResultWithoutEmittingAndDetachesAllHandlers()
    {
        var (page, events) = Page();
        using var observer = new MaterialInitialNavigationDiagnostics(page, new("http://127.0.0.1:40001"));
        var expected = new object();
        var calls = 0;
        var output = new List<string>();
        var actual = await observer.RunAsync(() => { calls++; return Task.FromResult(expected); }, output.Add);
        Assert.Same(expected, actual);
        Assert.Equal(1, calls);
        Assert.Empty(output);
        Assert.Empty(events.Handlers);
        Assert.Equal(6, events.Adds);
        Assert.Equal(6, events.Removes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailureRetainsExactPrimaryAndIndependentObservedMilestones(bool dom, bool loaded)
    {
        var (page, events) = Page();
        using var observer = new MaterialInitialNavigationDiagnostics(page, new("http://127.0.0.1:40001"));
        var primary = new TimeoutException("private-navigation-text");
        var output = new List<string>();
        var request = Request("http://127.0.0.1:40001/instantquotation/3d-printing?token=private", "document");
        var actual = await Assert.ThrowsAsync<TimeoutException>(() => observer.RunAsync<object>(() =>
        {
            events.Raise("Request", request);
            if (dom) events.Raise("DOMContentLoaded", page);
            if (loaded) events.Raise("Load", page);
            return Task.FromException<object>(primary);
        }, output.Add));
        Assert.Same(primary, actual);
        using var receipt = Parse(Assert.Single(output));
        Assert.Equal(dom, receipt.RootElement.GetProperty("Snapshot").GetProperty("DomContentLoadedObserved").GetBoolean());
        Assert.Equal(loaded, receipt.RootElement.GetProperty("Snapshot").GetProperty("LoadObserved").GetBoolean());
        var row = Assert.Single(receipt.RootElement.GetProperty("Snapshot").GetProperty("Network").GetProperty("Requests").EnumerateArray());
        Assert.Equal("pending", row.GetProperty("Outcome").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("Status").ValueKind);
        Assert.False(receipt.RootElement.GetProperty("Snapshot").GetProperty("ObserverFaulted").GetBoolean());
        Assert.Empty(events.Handlers);
        Assert.DoesNotContain("private", output[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureSnapshotIsBoundedFrozenAndNeverReadsHeadersBodiesOrUnknownUrls()
    {
        var (page, events) = Page();
        using var observer = new MaterialInitialNavigationDiagnostics(page, new("http://127.0.0.1:40001"));
        var primary = new TimeoutException("private");
        var output = new List<string>();
        var first = Request("http://127.0.0.1:40001/_blazor?id=private", "fetch");
        await Assert.ThrowsAsync<TimeoutException>(() => observer.RunAsync<object>(() =>
        {
            events.Raise("Request", first);
            for (int index = 0; index < 39; index++)
                events.Raise("Request", Request("https://external.invalid/private", "private-resource"));
            events.Raise("Response", Response(first, 200));
            return Task.FromException<object>(primary);
        }, output.Add));
        var frozen = Assert.Single(output);
        events.Raise("RequestFinished", first);
        events.Raise("Load", page);
        Assert.Equal(frozen, Assert.Single(output));
        using var receipt = Parse(frozen);
        var snapshot = receipt.RootElement.GetProperty("Snapshot");
        Assert.False(snapshot.GetProperty("LoadObserved").GetBoolean());
        Assert.Equal(0, snapshot.GetProperty("PendingSubscriptions").GetInt32());
        var network = snapshot.GetProperty("Network");
        Assert.Equal(40, network.GetProperty("Observed").GetInt32());
        Assert.Equal(8, network.GetProperty("Omitted").GetInt32());
        var rows = network.GetProperty("Requests");
        Assert.Equal(32, rows.GetArrayLength());
        Assert.Equal(200, rows[0].GetProperty("Status").GetInt32());
        Assert.Equal("pending", rows[0].GetProperty("Outcome").GetString());
        Assert.DoesNotContain("private", frozen, StringComparison.Ordinal);
        Assert.DoesNotContain("external.invalid", frozen, StringComparison.Ordinal);
        Assert.True(frozen.Length < 16_384);
    }

    [Fact]
    public async Task SinkFailureAndHostileExceptionDataCannotReplaceOriginal()
    {
        var (page, events) = Page();
        using var observer = new MaterialInitialNavigationDiagnostics(page, new("http://127.0.0.1:40001"));
        var primary = new HostileDataException();
        var actual = await Assert.ThrowsAsync<HostileDataException>(() => observer.RunAsync<object>(
            () => Task.FromException<object>(primary), _ => throw new IOException("private-output")));
        Assert.Same(primary, actual);
        Assert.Empty(events.Handlers);
    }

    [Theory]
    [InlineData("Request")]
    [InlineData("Response")]
    [InlineData("RequestFinished")]
    [InlineData("RequestFailed")]
    [InlineData("DOMContentLoaded")]
    [InlineData("Load")]
    public async Task PartialSubscriptionFailureRollsBackEveryAttemptAndDoesNotChangeNavigation(string eventName)
    {
        var (page, events) = Page();
        events.ThrowAfterAdd = eventName;
        using var observer = new MaterialInitialNavigationDiagnostics(page, new("http://127.0.0.1:40001"));
        var expected = new object();
        var actual = await observer.RunAsync(() => Task.FromResult(expected), _ => throw new InvalidOperationException());
        Assert.Same(expected, actual);
        Assert.Empty(events.Handlers);
        Assert.Equal(events.Adds, events.Removes);
    }

    [Fact]
    public async Task DetachFailureRetainsUndoForOwnerDisposalWithoutReplacingPrimary()
    {
        var (page, events) = Page();
        events.ThrowRemoveOnce = "Response";
        using var observer = new MaterialInitialNavigationDiagnostics(page, new("http://127.0.0.1:40001"));
        var primary = new IOException("private-primary");
        var output = new List<string>();
        var actual = await Assert.ThrowsAsync<IOException>(() => observer.RunAsync<object>(
            () => Task.FromException<object>(primary), output.Add));
        Assert.Same(primary, actual);
        using var receipt = Parse(Assert.Single(output));
        var snapshot = receipt.RootElement.GetProperty("Snapshot");
        Assert.True(snapshot.GetProperty("ObserverFaulted").GetBoolean());
        Assert.Equal(1, snapshot.GetProperty("PendingSubscriptions").GetInt32());
        Assert.Empty(events.Handlers); // Run's final disposal retries the exact retained undo.
        Assert.Equal(0, observer.PendingSubscriptions);
    }

    [Fact]
    public async Task FaultingRequestPropertyCannotEscapeThePassiveCallback()
    {
        var (page, events) = Page();
        using var observer = new MaterialInitialNavigationDiagnostics(page, new("http://127.0.0.1:40001"));
        var request = DispatchProxy.Create<IRequest, EventProxy>();
        var actual = await observer.RunAsync(() =>
        {
            events.Raise("Request", request); // No property is configured: every getter throws.
            return Task.FromResult(7);
        }, _ => throw new InvalidOperationException());
        Assert.Equal(7, actual);
        Assert.Empty(events.Handlers);
    }

    private static JsonDocument Parse(string output) =>
        JsonDocument.Parse(output["material-initial-navigation ".Length..]);

    private static (IPage Page, EventProxy Events) Page()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        return (page, (EventProxy)(object)page);
    }

    private static IRequest Request(string url, string type)
    {
        var request = DispatchProxy.Create<IRequest, EventProxy>();
        var values = ((EventProxy)(object)request).Properties;
        values["Url"] = url;
        values["Method"] = "GET";
        values["ResourceType"] = type;
        values["Failure"] = "private-failure-text";
        return request;
    }

    private static IResponse Response(IRequest request, int status)
    {
        var response = DispatchProxy.Create<IResponse, EventProxy>();
        var values = ((EventProxy)(object)response).Properties;
        values["Request"] = request;
        values["Status"] = status;
        return response;
    }

    private sealed class HostileDataException : Exception
    {
        public override IDictionary Data => throw new InvalidOperationException("private-data");
    }

    /// <summary>Emulates interface accessors only; creates no browser or application host.</summary>
    public class EventProxy : DispatchProxy
    {
        internal readonly Dictionary<string, Delegate> Handlers = [];
        internal readonly Dictionary<string, object?> Properties = [];
        internal int Adds;
        internal int Removes;
        internal string? ThrowAfterAdd;
        internal string? ThrowRemoveOnce;

        /// <summary>Dispatches the explicitly configured event or property access.</summary>
        /// <param name="targetMethod">The interface accessor.</param>
        /// <param name="args">The accessor arguments.</param>
        /// <returns>The configured property value, or null for an event accessor.</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name.StartsWith("add_", StringComparison.Ordinal))
            {
                Adds++;
                var key = name[4..];
                Handlers[key] = Handlers.TryGetValue(key, out var old)
                    ? Delegate.Combine(old, (Delegate)args![0]!) : (Delegate)args![0]!;
                if (ThrowAfterAdd == key) throw new InvalidOperationException("private-add");
                return null;
            }
            if (name.StartsWith("remove_", StringComparison.Ordinal))
            {
                Removes++;
                var key = name[7..];
                if (ThrowRemoveOnce == key)
                {
                    ThrowRemoveOnce = null;
                    throw new InvalidOperationException("private-remove");
                }
                if (Handlers.TryGetValue(key, out var old))
                {
                    var remaining = Delegate.Remove(old, (Delegate)args![0]!);
                    if (remaining is null) Handlers.Remove(key); else Handlers[key] = remaining;
                }
                return null;
            }
            if (name.StartsWith("get_", StringComparison.Ordinal) && Properties.TryGetValue(name[4..], out var value))
                return value;
            throw new NotSupportedException("Unconfigured accessor");
        }

        internal void Raise<T>(string name, T value)
        {
            if (Handlers.TryGetValue(name, out var handler)) handler.DynamicInvoke(this, value);
        }
    }
}
