using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class TestResponseFailureEvidenceTests
{
    [Fact]
    public void Collector_IsBoundedMonotonicAndStopsAtClientSettlement()
    {
        var probe = new TestResponseFailureEvidence(TestResponseRequestLabel.BaseQuotation);
        for (int i = 0; i < 40; i++) probe.Record(TestResponsePhase.RequestAborted);
        probe.Close();
        probe.Record(TestResponsePhase.NextReturned, 200, 42);
        using var json = JsonDocument.Parse(probe.Snapshot());
        var records = json.RootElement.GetProperty("Records").EnumerateArray().ToArray();
        Assert.Equal(16, records.Length);
        Assert.True(json.RootElement.GetProperty("Overflow").GetBoolean());
        Assert.True(json.RootElement.GetProperty("Closed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("ObservedBodyBytes").ValueKind);
        Assert.Equal("unavailable", json.RootElement.GetProperty("BodyByteObservation").GetString());
        var ticks = records.Select(item => item.GetProperty("ElapsedTicks").GetInt64()).ToArray();
        Assert.Equal(ticks.Order(), ticks);
        Assert.All(records, item => Assert.Equal("RequestAborted", item.GetProperty("Phase").GetString()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestResponseFailureEvidence((TestResponseRequestLabel)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => probe.Record((TestResponsePhase)999));
    }

    [Fact]
    public void Registrations_AreBoundedAndRemovedWithoutRetainingLateCallbacks()
    {
        var registry = new TestResponseProbeRegistry();
        var leases = new List<TestResponseProbeRegistry.Lease>();
        try
        {
            for (int i = 0; i < 16; i++) leases.Add(registry.Begin(TestResponseRequestLabel.BaseQuotation));
            Assert.Equal(16, registry.ActiveCount);
            Assert.Throws<InvalidOperationException>(() => registry.Begin(TestResponseRequestLabel.BaseQuotation));
            var first = leases[0];
            Assert.True(registry.TryFind(first.Id, out var found));
            Assert.Same(first.Probe, found);
            first.Dispose();
            Assert.False(registry.TryFind(first.Id, out _));
            Assert.False(registry.TryFind(Guid.NewGuid(), out _));
            Assert.Equal(15, registry.ActiveCount);
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }
        Assert.Equal(0, registry.ActiveCount);
    }

    [Fact]
    public async Task ExistingPipelineCallbacks_RecordLifecycleAndRemoveOnlyTheProbeHeader()
    {
        var registry = new TestResponseProbeRegistry();
        using var lease = registry.Begin(TestResponseRequestLabel.UnknownQuotationPath);
        using var aborted = new CancellationTokenSource();
        var callbacks = new ResponseCallbacks();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(callbacks);
        context.RequestAborted = aborted.Token;
        context.Request.Path = "/private-customer";
        context.Request.Headers[TestResponseProbeRegistry.Header] = lease.Id.ToString("N");
        context.Request.Headers["X-Private"] = "private credential";
        context.Response.StatusCode = 404;
        context.Response.ContentLength = 42;
        var filter = new TestResponseProbeStartupFilter(registry);
        await filter.InvokeAsync(context, async current =>
        {
            Assert.False(current.Request.Headers.ContainsKey(TestResponseProbeRegistry.Header));
            Assert.Equal("private credential", current.Request.Headers["X-Private"].ToString());
            Assert.Equal("/private-customer", current.Request.Path.Value);
            await callbacks.StartAsync();
            aborted.Cancel();
        });
        await callbacks.CompleteAsync();
        using var json = JsonDocument.Parse(lease.Probe.Snapshot());
        var records = json.RootElement.GetProperty("Records").EnumerateArray().ToArray();
        Assert.Equal<string?>(new[] { "ServerAdmitted", "HeadersStarting", "RequestAborted", "NextReturned", "ResponseCompleted" }.AsEnumerable(),
            records.Select(item => item.GetProperty("Phase").GetString()));
        Assert.Equal(404, records[1].GetProperty("Status").GetInt32());
        Assert.Equal(42, records[1].GetProperty("KnownContentLength").GetInt64());
        Assert.DoesNotContain("private", lease.Probe.Snapshot(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(lease.Id.ToString("N"), lease.Probe.Snapshot(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnregisteredRequest_IsPassiveAndPipelineFailureIsNotReplaced()
    {
        var registry = new TestResponseProbeRegistry();
        var filter = new TestResponseProbeStartupFilter(registry);
        var context = new DefaultHttpContext();
        var original = new InvalidOperationException("private original");
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            filter.InvokeAsync(context, _ => Task.FromException(original))));
        Assert.Equal(0, registry.ActiveCount);

        using var lease = registry.Begin(TestResponseRequestLabel.UnknownQuotationPath);
        context.Request.Headers[TestResponseProbeRegistry.Header] = lease.Id.ToString("N");
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            filter.InvokeAsync(context, _ => Task.FromException(original))));
        Assert.Contains("NextFaulted", lease.Probe.Snapshot(), StringComparison.Ordinal);
        Assert.DoesNotContain("private", lease.Probe.Snapshot(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulSend_PreservesResponseContentTimeoutAndEmitsNothing()
    {
        var registry = new TestResponseProbeRegistry();
        using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("private synthetic body") };
        var content = response.Content;
        using var client = new HttpClient(new ControlledHandler(_ => Task.FromResult(response)));
        var timeout = client.Timeout;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/private?token=secret");
        var output = new List<string>();
        var actual = await TestResponseFailureEvidence.SendAsync(services, client, request,
            TestResponseRequestLabel.BaseQuotation, output.Add);
        Assert.Same(response, actual);
        Assert.Same(content, actual.Content);
        Assert.Equal(timeout, client.Timeout);
        Assert.Empty(output);
        Assert.Equal(0, registry.ActiveCount);
    }

    [Fact]
    public async Task FailedSend_EmitsOneSecretFreeSnapshotAndRethrowsTheSameException()
    {
        var registry = new TestResponseProbeRegistry();
        using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
        var original = new HttpRequestException("private credential/body/header");
        using var client = new HttpClient(new ControlledHandler(_ => Task.FromException<HttpResponseMessage>(original)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/private?token=secret");
        request.Headers.Add("X-Private", "private credential");
        var output = new List<string>();
        Assert.Same(original, await Assert.ThrowsAsync<HttpRequestException>(() =>
            TestResponseFailureEvidence.SendAsync(services, client, request,
                TestResponseRequestLabel.BaseQuotation, output.Add)));
        var snapshot = Assert.Single(output);
        Assert.Equal(snapshot, original.Data[TestResponseFailureEvidence.DataKey]);
        foreach (var secret in new[] { "private", "credential", "secret", "localhost", "X-Private", original.Message })
            Assert.DoesNotContain(secret, snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ClientFailed", snapshot, StringComparison.Ordinal);
        Assert.Equal(0, registry.ActiveCount);
    }

    [Fact]
    public async Task FailedOutput_DoesNotMaskTheRequestException()
    {
        var registry = new TestResponseProbeRegistry();
        using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
        var original = new HttpRequestException("primary");
        using var client = new HttpClient(new ControlledHandler(_ => Task.FromException<HttpResponseMessage>(original)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/");
        Assert.Same(original, await Assert.ThrowsAsync<HttpRequestException>(() =>
            TestResponseFailureEvidence.SendAsync(services, client, request,
                TestResponseRequestLabel.CompatibilityNotice, _ => throw new InvalidOperationException("sink"))));
        Assert.Equal(0, registry.ActiveCount);
    }

    [Fact]
    public async Task NavigationLease_CorrelatesOneOwnedDocumentWithoutExposingItsIdentity()
    {
        var registry = new TestResponseProbeRegistry();
        using var lease = Assert.IsType<TestResponseProbeRegistry.Lease>(
            registry.TryBeginNavigation(new Uri("http://127.0.0.1:40001"), TestResponseRequestLabel.BaseQuotation));
        Assert.Null(registry.TryBeginNavigation(new Uri("http://127.0.0.1:40001"), TestResponseRequestLabel.BaseQuotation));
        var context = NavigationContext();
        context.Request.QueryString = new("?token=private-query");
        context.Request.Headers.Authorization = "Bearer private-credential";
        context.Request.Headers.Cookie = "private-cookie";
        var filter = new TestResponseProbeStartupFilter(registry);
        await filter.InvokeAsync(context, current =>
        {
            Assert.False(current.Request.Headers.ContainsKey(TestResponseProbeRegistry.Header));
            Assert.Equal("Bearer private-credential", current.Request.Headers.Authorization.ToString());
            Assert.Equal("private-cookie", current.Request.Headers.Cookie.ToString());
            Assert.Equal("?token=private-query", current.Request.QueryString.Value);
            return Task.CompletedTask;
        });
        await filter.InvokeAsync(NavigationContext(), current =>
        {
            Assert.False(current.Request.Headers.ContainsKey(TestResponseProbeRegistry.Header));
            return Task.CompletedTask;
        });
        using var snapshot = JsonDocument.Parse(lease.Probe.Snapshot());
        Assert.Single(snapshot.RootElement.GetProperty("Records").EnumerateArray(),
            item => item.GetProperty("Phase").GetString() == "ServerAdmitted");
        Assert.Contains("NextReturned", lease.Probe.Snapshot(), StringComparison.Ordinal);
        foreach (var secret in new[] { lease.Id.ToString("N"), "private", "127.0.0.1", "40001", "Authorization", "Cookie" })
            Assert.DoesNotContain(secret, lease.Probe.Snapshot(), StringComparison.OrdinalIgnoreCase);
        lease.Dispose();
        Assert.Equal(0, registry.ActiveCount);
        Assert.False(registry.TryFind(lease.Id, out _));
        var closed = lease.Probe.Snapshot();
        await filter.InvokeAsync(NavigationContext(), _ => Task.CompletedTask);
        Assert.Equal(closed, lease.Probe.Snapshot());
    }

    [Theory]
    [InlineData("POST", "document", "navigate", "text/html", "/instantquotation/3d-printing", "127.0.0.1:40001")]
    [InlineData("GET", "iframe", "navigate", "text/html", "/instantquotation/3d-printing", "127.0.0.1:40001")]
    [InlineData("GET", "script", "no-cors", "*/*", "/_framework/blazor.web.js", "127.0.0.1:40001")]
    [InlineData("GET", "document", "cors", "text/html", "/instantquotation/3d-printing", "127.0.0.1:40001")]
    [InlineData("GET", "document", "navigate", "application/json", "/instantquotation/3d-printing", "127.0.0.1:40001")]
    [InlineData("GET", "document", "navigate", "text/html", "/private-customer", "127.0.0.1:40001")]
    [InlineData("GET", "document", "navigate", "text/html", "/instantquotation/3d-printing", "foreign.invalid")]
    public async Task NavigationLease_DoesNotClaimForeignOrNonDocumentRequests(
        string method, string destination, string mode, string accept, string path, string host)
    {
        var registry = new TestResponseProbeRegistry();
        using var lease = Assert.IsType<TestResponseProbeRegistry.Lease>(
            registry.TryBeginNavigation(new Uri("http://127.0.0.1:40001"), TestResponseRequestLabel.BaseQuotation));
        var foreign = NavigationContext();
        foreign.Request.Method = method;
        foreign.Request.Headers["Sec-Fetch-Dest"] = destination;
        foreign.Request.Headers["Sec-Fetch-Mode"] = mode;
        foreign.Request.Headers.Accept = accept;
        foreign.Request.Path = path;
        foreign.Request.Host = new(host);
        var filter = new TestResponseProbeStartupFilter(registry);
        await filter.InvokeAsync(foreign, current =>
        {
            Assert.False(current.Request.Headers.ContainsKey(TestResponseProbeRegistry.Header));
            return Task.CompletedTask;
        });
        Assert.DoesNotContain("ServerAdmitted", lease.Probe.Snapshot(), StringComparison.Ordinal);
        await filter.InvokeAsync(NavigationContext(), _ => Task.CompletedTask);
        Assert.Contains("ServerAdmitted", lease.Probe.Snapshot(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NavigationLease_ExpiresWithoutTimersAndDisposalReleasesTheFactorySlot()
    {
        long now = 0;
        var registry = new TestResponseProbeRegistry(() => now);
        Assert.Null(registry.TryBeginNavigation(new Uri("https://foreign.invalid"), TestResponseRequestLabel.BaseQuotation));
        using var lease = Assert.IsType<TestResponseProbeRegistry.Lease>(
            registry.TryBeginNavigation(new Uri("http://127.0.0.1:40001"), TestResponseRequestLabel.BaseQuotation));
        now = 31 * System.Diagnostics.Stopwatch.Frequency;
        await new TestResponseProbeStartupFilter(registry).InvokeAsync(NavigationContext(), _ => Task.CompletedTask);
        Assert.DoesNotContain("ServerAdmitted", lease.Probe.Snapshot(), StringComparison.Ordinal);
        lease.Dispose();
        using var next = Assert.IsType<TestResponseProbeRegistry.Lease>(
            registry.TryBeginNavigation(new Uri("http://127.0.0.1:40001"), TestResponseRequestLabel.BaseQuotation));
        await new TestResponseProbeStartupFilter(registry).InvokeAsync(NavigationContext(), _ => Task.CompletedTask);
        Assert.Contains("ServerAdmitted", next.Probe.Snapshot(), StringComparison.Ordinal);
        next.Dispose();
        Assert.Equal(0, registry.ActiveCount);
    }

    [Fact]
    public async Task NavigationMatcherFailure_IsPassiveAndCannotReplaceThePipelineFailure()
    {
        var calls = 0;
        var registry = new TestResponseProbeRegistry(() =>
            ++calls == 1 ? 0 : throw new InvalidOperationException("private-observer"));
        using var lease = Assert.IsType<TestResponseProbeRegistry.Lease>(
            registry.TryBeginNavigation(new Uri("http://127.0.0.1:40001"), TestResponseRequestLabel.BaseQuotation));
        var original = new IOException("private-pipeline");
        Assert.Same(original, await Assert.ThrowsAsync<IOException>(() =>
            new TestResponseProbeStartupFilter(registry).InvokeAsync(
                NavigationContext(), _ => Task.FromException(original))));
        lease.Dispose();
        Assert.Equal(0, registry.ActiveCount);
        Assert.DoesNotContain("private", lease.Probe.Snapshot(), StringComparison.OrdinalIgnoreCase);
    }

    private static DefaultHttpContext NavigationContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/instantquotation/3d-printing";
        context.Request.Host = new("127.0.0.1:40001");
        context.Request.Headers["Sec-Fetch-Dest"] = "document";
        context.Request.Headers["Sec-Fetch-Mode"] = "navigate";
        context.Request.Headers.Accept = "text/html,application/xhtml+xml";
        return context;
    }

    private sealed class ControlledHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class ResponseCallbacks : HttpResponseFeature
    {
        private Func<Task>? starting;
        private Func<Task>? completed;
        public override void OnStarting(Func<object, Task> callback, object state) => starting = () => callback(state);
        public override void OnCompleted(Func<object, Task> callback, object state) => completed = () => callback(state);
        internal Task StartAsync() => starting?.Invoke() ?? Task.CompletedTask;
        internal Task CompleteAsync() => completed?.Invoke() ?? Task.CompletedTask;
    }
}
