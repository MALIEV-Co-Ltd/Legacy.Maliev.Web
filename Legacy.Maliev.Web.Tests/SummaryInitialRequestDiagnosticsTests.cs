using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actor-free controls for passive first-document observations.</summary>
public sealed class SummaryInitialRequestDiagnosticsTests
{
    [Theory]
    [InlineData("POST", "/instantquotation/3d-printing")]
    [InlineData("GET", "/contact")]
    public async Task NonTargetRequestsRemainUntouched(string method, string path)
    {
        var probe = new SummaryInitialRequestDiagnostics();
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var calls = 0;
        probe.Configure(app => app.Run(context =>
        {
            calls++;
            context.Response.StatusCode = 202;
            return Task.CompletedTask;
        }))(builder);
        var context = Context();
        context.Request.Method = method;
        context.Request.Path = path;

        await builder.Build()(context);

        Assert.Equal(1, calls);
        Assert.Equal(202, context.Response.StatusCode);
        Assert.Equal(new[] { "ObservationStopped" }, Stages(probe));
    }

    [Fact]
    public async Task HeaderStartAndCompletionAreIndependentOfPipelineReturn()
    {
        var probe = new SummaryInitialRequestDiagnostics();
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var response = new RecordingResponse();
        probe.Configure(app => app.Run(context =>
        {
            context.Response.StatusCode = 201;
            return Task.CompletedTask;
        }))(builder);
        var context = Context();
        context.Features.Set<IHttpResponseFeature>(response);

        await builder.Build()(context);
        await response.StartAsync();
        await response.CompleteAsync();

        var stages = Stages(probe);
        Assert.Equal(new[] { "PipelineEntered", "PipelineReturned", "HeadersStarted", "ResponseCompleted", "ObservationStopped" }, stages);
        Assert.Equal(201, context.Response.StatusCode);
    }

    [Fact]
    public async Task PipelineExceptionIdentityAndPrivateRequestDataRemainUnchangedAndUnrecorded()
    {
        var probe = new SummaryInitialRequestDiagnostics();
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var sentinel = new InvalidOperationException("private-exception-message");
        probe.Configure(app => app.Run(_ => Task.FromException(sentinel)))(builder);
        var context = Context();
        context.Request.QueryString = new QueryString("?culture=en&private=do-not-record");
        context.Request.Headers.Cookie = "private-cookie=do-not-record";

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.Build()(context));

        Assert.Same(sentinel, actual);
        var json = JsonSerializer.Serialize(probe.StopAndSnapshot());
        Assert.Contains("PipelineFailed", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("do-not-record", json, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("request-path")]
    [InlineData("response-status")]
    public async Task FaultingDiagnosticFeaturesCannotReplaceTheDownstreamException(string fault)
    {
        var probe = new SummaryInitialRequestDiagnostics();
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var sentinel = new InvalidOperationException("downstream");
        var calls = 0;
        probe.Configure(app => app.Run(_ => { calls++; return Task.FromException(sentinel); }))(builder);
        var context = Context();
        if (fault == "request-path") context.Features.Set<IHttpRequestFeature>(new FaultingRequest());
        else context.Features.Set<IHttpResponseFeature>(new FaultingResponse());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.Build()(context));

        Assert.Same(sentinel, actual);
        Assert.Equal(1, calls);
        probe.StopAndSnapshot();
    }

    [Fact]
    public async Task DelegatedCallPreservesTheResultAndExactCancellationToken()
    {
        var probe = new SummaryInitialRequestDiagnostics();
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var builder = new ApplicationBuilder(services);
        var context = Context();
        var accessor = new HttpContextAccessor { HttpContext = context };
        var sentinel = new object();
        var calls = 0;
        probe.Configure(app => app.Run(async _ =>
        {
            Task<object> Operation(CancellationToken token)
            {
                calls++;
                Assert.Equal(cancellation.Token, token);
                return Task.FromResult(sentinel);
            }

            var result = await probe.ForwardAsync(accessor,
                SummaryInitialRequestDiagnostics.Stage.SessionCreateEntered,
                SummaryInitialRequestDiagnostics.Stage.SessionCreateReturned,
                SummaryInitialRequestDiagnostics.Stage.SessionCreateFailed,
                () => Operation(cancellation.Token));
            Assert.Same(sentinel, result);
        }))(builder);

        await builder.Build()(context);

        Assert.Equal(1, calls);
        Assert.Contains("SessionCreateEntered", Stages(probe));
    }

    [Fact]
    public async Task DelegatedFailureRetainsTheSameExceptionWithoutRetainingItsText()
    {
        var probe = new SummaryInitialRequestDiagnostics();
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var context = Context();
        var accessor = new HttpContextAccessor { HttpContext = context };
        var sentinel = new IOException("private-dependency-failure");
        var calls = 0;
        probe.Configure(app => app.Run(async _ =>
        {
            var actual = await Assert.ThrowsAsync<IOException>(() => probe.ForwardAsync<object>(accessor,
                SummaryInitialRequestDiagnostics.Stage.CountryEntered,
                SummaryInitialRequestDiagnostics.Stage.CountryReturned,
                SummaryInitialRequestDiagnostics.Stage.CountryFailed,
                () => { calls++; return Task.FromException<object>(sentinel); }));
            Assert.Same(sentinel, actual);
        }))(builder);

        await builder.Build()(context);

        Assert.Equal(1, calls);
        var json = JsonSerializer.Serialize(probe.StopAndSnapshot());
        Assert.Contains("CountryFailed", json, StringComparison.Ordinal);
        Assert.Contains("\"failure\":\"Io\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InventoryIsBoundedAndStoppingMakesLaterEventsInert()
    {
        var probe = new SummaryInitialRequestDiagnostics();
        for (var index = 0; index < 100; index++)
            probe.Observe(SummaryInitialRequestDiagnostics.Stage.NavigationInvoked, status: 999);
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(probe.StopAndSnapshot()));
        Assert.Equal(32, snapshot.RootElement.GetProperty("observations").GetArrayLength());
        Assert.Equal(32, snapshot.RootElement.GetProperty("dropped").GetInt32());
        Assert.All(snapshot.RootElement.GetProperty("observations").EnumerateArray(),
            item => Assert.Equal(JsonValueKind.Null, item.GetProperty("status").ValueKind));
        var stopped = JsonSerializer.Serialize(probe.StopAndSnapshot());

        probe.Observe(SummaryInitialRequestDiagnostics.Stage.NavigationFailed,
            failure: new Exception("private-late-message"));
        probe.Observe((SummaryInitialRequestDiagnostics.Stage)12345);

        Assert.Equal(stopped, JsonSerializer.Serialize(probe.StopAndSnapshot()));
    }

    [Fact]
    public void ReceiptWriteFailureDoesNotEscapeAndStillCallsTheWriterOnce()
    {
        var calls = 0;
        SummaryInitialRequestDiagnostics.TryWrite(() =>
        {
            calls++;
            throw new IOException("private-output-failure");
        });
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RequestCancellationIsObservedWithoutChangingTheResponse()
    {
        var probe = new SummaryInitialRequestDiagnostics();
        using var services = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var builder = new ApplicationBuilder(services);
        probe.Configure(app => app.Run(_ => Task.CompletedTask))(builder);
        var context = Context();
        context.RequestAborted = cancellation.Token;

        await builder.Build()(context);

        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(probe.StopAndSnapshot()));
        var returned = Assert.Single(snapshot.RootElement.GetProperty("observations").EnumerateArray(),
            item => item.GetProperty("stage").GetString() == "PipelineReturned");
        Assert.True(returned.GetProperty("canceled").GetBoolean());
        Assert.Equal(200, context.Response.StatusCode);
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/instantquotation/3d-printing";
        return context;
    }

    private static string[] Stages(SummaryInitialRequestDiagnostics probe)
    {
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(probe.StopAndSnapshot()));
        return snapshot.RootElement.GetProperty("observations").EnumerateArray()
            .Select(item => item.GetProperty("stage").GetString()!).ToArray();
    }

    private sealed class RecordingResponse : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> starting = [];
        private readonly List<(Func<object, Task> Callback, object State)> completed = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;
        public void OnStarting(Func<object, Task> callback, object state) => starting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) => completed.Add((callback, state));
        internal async Task StartAsync() { foreach (var item in starting.AsEnumerable().Reverse()) await item.Callback(item.State); }
        internal async Task CompleteAsync() { foreach (var item in completed.AsEnumerable().Reverse()) await item.Callback(item.State); }
    }

    private sealed class FaultingRequest : IHttpRequestFeature
    {
        public string Protocol { get; set; } = "HTTP/1.1";
        public string Scheme { get; set; } = "http";
        public string Method { get; set; } = "GET";
        public string PathBase { get; set; } = "";
        public string Path { get => throw new InvalidOperationException("private-feature"); set { } }
        public string QueryString { get; set; } = "";
        public string RawTarget { get; set; } = "";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
    }

    private sealed class FaultingResponse : IHttpResponseFeature
    {
        public int StatusCode { get => throw new InvalidOperationException("private-feature"); set { } }
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
