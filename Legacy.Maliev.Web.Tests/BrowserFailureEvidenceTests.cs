using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class BrowserFailureEvidenceTests
{
    [Theory]
    [InlineData("https://example.test/_blazor?id=private", true)]
    [InlineData("ws://example.test/_blazor?id=private", true)]
    [InlineData("https://example.test/_blazor/negotiate?secret=private", true)]
    [InlineData("https://example.test/_blazor/private-file", false)]
    [InlineData("https://example.test/customer?path=/_blazor", false)]
    public void OnlyKnownBlazorPathsAreRecognized(string url, bool expected) =>
        Assert.Equal(expected, BrowserFailureEvidence.IsBlazor(url));

    [Theory]
    [InlineData("private customer Connection disconnected", "disconnect")]
    [InlineData("private timeout detail", "timeout")]
    [InlineData("Connection disconnected: Server timeout elapsed", "timeout")]
    [InlineData("private filename.stl", "other")]
    public void ErrorTextBecomesFixedCategory(string text, string category) =>
        Assert.Equal(category, BrowserFailureEvidence.Category(text));

    [Theory]
    [InlineData("{\"workflow\":\"configured\",\"parts\":33}")]
    [InlineData("{\"workflow\":\"private\"}")]
    [InlineData("{\"workflow\":\"configured\",\"filename\":\"private\"}")]
    [InlineData("{\"workflow\":\"configured\",\"parts\":1,\"parts\":2}")]
    [InlineData("{\"workflow\":\"configured\",\"progress\":\"private\"}")]
    public void ObservationRejectsPrivateOrUnboundedValues(string json) =>
        Assert.Throws<FormatException>(() => BrowserFailureEvidence.ParseObservation(json));

    [Fact]
    public async Task EventsAreBoundedCategoricalAndStageAttributed()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        using var evidence = new BrowserFailureEvidence(page);
        evidence.Stage("Reload");
        for (var i = 0; i < 40; i++) proxy.Raise("PageError", "private filename /ticket Connection disconnected");
        Assert.Throws<ArgumentOutOfRangeException>(() => evidence.Stage("private"));
        var original = new TimeoutException("primary");
        var output = new List<string>();
        await evidence.AttachAsync(original, () => Task.FromResult("{\"workflow\":\"configured\",\"parts\":2,\"uploadRows\":0,\"progress\":false}"), output.Add);
        var json = Assert.Single(output);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(32, document.RootElement.GetProperty("Events").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("Truncated").GetBoolean());
        Assert.All(document.RootElement.GetProperty("Events").EnumerateArray(), item =>
        {
            Assert.Equal("Reload", item.GetProperty("Stage").GetString());
            Assert.Equal("disconnect", item.GetProperty("Category").GetString());
        });
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.Equal(json, original.Data[BrowserFailureEvidence.DataKey]);
        Assert.Equal("primary", original.Message);
    }

    [Fact]
    public async Task FailingObserverAndSinkDoNotReplaceOriginal()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        using var evidence = new BrowserFailureEvidence(page);
        var original = new TimeoutException("primary");
        await evidence.AttachAsync(original, () => Task.FromException<string>(new InvalidOperationException("private")),
            _ => throw new InvalidOperationException("private sink"));
        Assert.Contains("unavailable", Assert.IsType<string>(original.Data[BrowserFailureEvidence.DataKey]), StringComparison.Ordinal);
        Assert.Null(original.InnerException);
        Assert.Equal("primary", original.Message);
    }

    [Fact]
    public async Task LateObserverFaultIsObservedAndObservationStopsWithinBudget()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        using var evidence = new BrowserFailureEvidence(page);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new TimeoutException("primary");
        try
        {
            await evidence.AttachAsync(original, () => pending.Task, _ => { }).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains("unavailable", Assert.IsType<string>(original.Data[BrowserFailureEvidence.DataKey]), StringComparison.Ordinal);
        }
        finally
        {
            pending.TrySetException(new InvalidOperationException("late private failure"));
            try { await pending.Task; }
            catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public void DisposeDetachesExactlyOnceIncludingOwnedSocketSubscriptions()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        var socket = DispatchProxy.Create<IWebSocket, EventProxy>();
        var socketProxy = (EventProxy)(object)socket;
        socketProxy.Url = "ws://example.test/_blazor?id=private";
        var evidence = new BrowserFailureEvidence(page);
        proxy.Raise("WebSocket", socket);
        Assert.Equal(6, proxy.Adds);
        Assert.Equal(2, socketProxy.Adds);
        evidence.Dispose();
        evidence.Dispose();
        Assert.Equal(6, proxy.Removes);
        Assert.Equal(2, socketProxy.Removes);
        Assert.Empty(proxy.Handlers);
        Assert.Empty(socketProxy.Handlers);
    }

    [Fact]
    public void SocketSubscriptionsAreBoundedAndIgnoreUnrelatedPaths()
    {
        var page = DispatchProxy.Create<IPage, EventProxy>();
        var proxy = (EventProxy)(object)page;
        using var evidence = new BrowserFailureEvidence(page);
        var observed = new List<EventProxy>();
        for (var index = 0; index < 40; index++)
        {
            var socket = DispatchProxy.Create<IWebSocket, EventProxy>();
            var socketProxy = (EventProxy)(object)socket;
            observed.Add(socketProxy);
            proxy.Raise("WebSocket", socket);
        }
        var unrelated = DispatchProxy.Create<IWebSocket, EventProxy>();
        ((EventProxy)(object)unrelated).Url = "wss://example.test/private-file?secret=ticket";
        proxy.Raise("WebSocket", unrelated);
        Assert.Equal(64, observed.Sum(item => item.Adds));
        Assert.Equal(0, ((EventProxy)(object)unrelated).Adds);
        evidence.Dispose();
        Assert.Equal(64, observed.Sum(item => item.Removes));
        Assert.All(observed, item => Assert.Empty(item.Handlers));
    }

    // No browser or SDK process is created: only interface event accessors are emulated.
    public class EventProxy : DispatchProxy
    {
        internal readonly Dictionary<string, Delegate> Handlers = [];
        internal int Adds;
        internal int Removes;
        internal string Url = "https://example.test/_blazor";
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name.StartsWith("add_", StringComparison.Ordinal))
            {
                Adds++;
                var key = name[4..];
                Handlers[key] = Handlers.TryGetValue(key, out var old) ? Delegate.Combine(old, (Delegate)args![0]!) : (Delegate)args![0]!;
                return null;
            }
            if (name.StartsWith("remove_", StringComparison.Ordinal))
            {
                Removes++;
                var key = name[7..];
                if (Handlers.TryGetValue(key, out var old))
                {
                    var remaining = Delegate.Remove(old, (Delegate)args![0]!);
                    if (remaining is null) Handlers.Remove(key); else Handlers[key] = remaining;
                }
                return null;
            }
            if (name == "get_Url") return Url;
            throw new NotSupportedException(name);
        }
        internal void Raise<T>(string name, T value)
        {
            if (Handlers.TryGetValue(name, out var handler)) handler.DynamicInvoke(this, value);
        }
    }
}
