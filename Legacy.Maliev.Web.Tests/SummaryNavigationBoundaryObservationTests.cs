using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real observer controls for bounded capture, request correlation, privacy and original failure retention.</summary>
public sealed class SummaryNavigationBoundaryObservationTests
{
    [Fact]
    public void ClosedObservationRejectsLateEventsAndAllowsNewPhase()
    {
        var recorder = new SummaryNavigationBoundaryRecorder();
        var first = recorder.Begin("initial");
        first.Record(SummaryNavigationStage.BrowserDocumentStarted);
        Assert.Throws<InvalidOperationException>(() => recorder.Begin("reload"));
        first.Dispose(); first.Dispose();
        first.Record(SummaryNavigationStage.KestrelIngress);
        Assert.Null(recorder.Current);
        Assert.True(first.Snapshot().Closed);
        Assert.Single(first.Snapshot().Events);
        using var second = recorder.Begin("reload");
        Assert.NotEqual(first.Snapshot().Correlation, second.Snapshot().Correlation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://private.invalid/?token=secret")]
    [InlineData("cookie=secret")]
    public void OnlyFixedPhaseCategoriesAreAccepted(string phase) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SummaryNavigationBoundaryRecorder().Begin(phase));

    [Fact]
    public void EventBufferIsBoundedAndSnapshotsDoNotExposeMutableStorage()
    {
        using var lease = new SummaryNavigationBoundaryRecorder().Begin("initial");
        for (var index = 0; index < 100; index++) lease.Record(SummaryNavigationStage.BrowserDocumentStarted);
        var snapshot = lease.Snapshot();
        Assert.Equal(64, snapshot.Events.Length);
        Assert.Equal(36, snapshot.Omitted);
        snapshot.Events[0] = new(SummaryNavigationStage.RequestFaulted, 0, 500);
        Assert.Equal(SummaryNavigationStage.BrowserDocumentStarted, lease.Snapshot().Events[0].Stage);
    }

    [Theory]
    [InlineData(99, null)]
    [InlineData(200, 200)]
    [InlineData(599, 599)]
    [InlineData(600, null)]
    public void OnlyHttpStatusRangeIsRetained(int supplied, int? expected)
    {
        using var lease = new SummaryNavigationBoundaryRecorder().Begin("initial");
        lease.Record(SummaryNavigationStage.BrowserDocumentResponse, supplied);
        Assert.Equal(expected, Assert.Single(lease.Snapshot().Events).Status);
    }

    [Fact]
    public void EndpointLoggerUsesOnlyCorrelatedLifecycleIdsWithoutFormattingPrivateState()
    {
        var recorder = new SummaryNavigationBoundaryRecorder();
        using var lease = recorder.Begin("initial");
        using var provider = new SummaryNavigationLoggerProvider(recorder);
        var logger = provider.CreateLogger(SummaryNavigationLoggerProvider.EndpointCategory);
        logger.Log(LogLevel.Information, new(0, "ExecutingEndpoint"), "cookie=secret", null,
            (_, _) => throw new InvalidOperationException("Formatter must not run."));
        Assert.Empty(lease.Snapshot().Events);
        using (recorder.EnterRequest(lease))
        {
            logger.Log(LogLevel.Information, new(0, "ExecutingEndpoint"), "cookie=secret", null,
                (_, _) => throw new InvalidOperationException("Formatter must not run."));
            logger.Log(LogLevel.Information, new(1, "ExecutedEndpoint"), "token=secret", new Exception("private"),
                (_, _) => throw new InvalidOperationException("Formatter must not run."));
            logger.Log(LogLevel.Information, new(0, "UnexpectedEvent"), "secret", null, (_, _) => "secret");
            provider.CreateLogger("Private.Customer").LogInformation("secret");
        }
        Assert.Null(recorder.CurrentRequest);
        Assert.Collection(lease.Snapshot().Events,
            item => Assert.Equal(SummaryNavigationStage.EndpointExecuting, item.Stage),
            item => Assert.Equal(SummaryNavigationStage.EndpointExecuted, item.Stage));
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(lease.Snapshot()), StringComparison.Ordinal);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(lease.Snapshot()), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("POST", "/instantquotation/3d-printing")]
    [InlineData("GET", "/dist/private.js")]
    public async Task MiddlewareDoesNotObserveOtherRequests(string method, string path)
    {
        var recorder = new SummaryNavigationBoundaryRecorder();
        using var lease = recorder.Begin("initial");
        using var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        new SummaryNavigationStartupFilter(recorder).Configure(builder => builder.Run(_ => Task.CompletedTask))(app);
        var context = new DefaultHttpContext(); context.Request.Method = method; context.Request.Path = path;
        await app.Build()(context);
        Assert.Empty(lease.Snapshot().Events);
        Assert.Null(recorder.CurrentRequest);
    }

    [Fact]
    public async Task MiddlewareCapturesIngressAndResponseStartThenReleasesRequestCorrelation()
    {
        var recorder = new SummaryNavigationBoundaryRecorder();
        using var lease = recorder.Begin("initial");
        using var services = new ServiceCollection().BuildServiceProvider();
        var response = new ControlledResponseFeature();
        var app = new ApplicationBuilder(services);
        new SummaryNavigationStartupFilter(recorder).Configure(builder => builder.Run(async _ =>
        {
            Assert.Same(lease, recorder.CurrentRequest);
            response.StatusCode = 200;
            await response.StartAsync();
        }))(app);
        var context = new DefaultHttpContext(); context.Features.Set<IHttpResponseFeature>(response);
        context.Request.Method = "GET"; context.Request.Path = "/instantquotation/3d-printing";
        await app.Build()(context);
        Assert.Null(recorder.CurrentRequest);
        Assert.Collection(lease.Snapshot().Events,
            item => Assert.Equal(SummaryNavigationStage.KestrelIngress, item.Stage),
            item => { Assert.Equal(SummaryNavigationStage.ResponseStarting, item.Stage); Assert.Equal(200, item.Status); },
            item => Assert.Equal(SummaryNavigationStage.RequestCompleted, item.Stage));
    }

    [Fact]
    public async Task MiddlewarePreservesEndpointFailureAndReleasesRequestCorrelation()
    {
        var recorder = new SummaryNavigationBoundaryRecorder();
        using var lease = recorder.Begin("initial");
        using var services = new ServiceCollection().BuildServiceProvider();
        var original = new InvalidOperationException("controlled endpoint failure");
        var app = new ApplicationBuilder(services);
        new SummaryNavigationStartupFilter(recorder).Configure(builder => builder.Run(_ => throw original))(app);
        var context = new DefaultHttpContext(); context.Request.Method = "GET";
        context.Request.Path = "/instantquotation/3d-printing";
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => app.Build()(context)));
        Assert.Null(recorder.CurrentRequest);
        Assert.Collection(lease.Snapshot().Events,
            item => Assert.Equal(SummaryNavigationStage.KestrelIngress, item.Stage),
            item => Assert.Equal(SummaryNavigationStage.RequestFaulted, item.Stage),
            item => Assert.Equal(SummaryNavigationStage.RequestCompleted, item.Stage));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ObserverErrorNeverMasksExistingFailure(bool captureFails)
    {
        var original = new TimeoutException("controlled navigation deadline");
        var fallback = new List<string>();
        var retained = Assert.Throws<TimeoutException>((Action)(() =>
        {
            try { throw original; }
            catch (Exception failure)
            {
                SummaryNavigationObservationWriter.Write(
                    () => captureFails ? throw new InvalidOperationException("secret") : "bounded report",
                    _ => throw new InvalidOperationException("secret"), failure, fallback.Add);
                throw;
            }
        }));
        Assert.Same(original, retained);
        Assert.Equal(captureFails ? "capture" : "write", original.Data["SummaryNavigationObserverFailure"]);
        Assert.DoesNotContain("secret", Assert.Single(fallback), StringComparison.Ordinal);
    }

    [Fact]
    public void ObserverFailureRejectsOtherwisePassingNavigation() =>
        Assert.Throws<InvalidOperationException>(() => SummaryNavigationObservationWriter.Write(
            () => "bounded report", _ => throw new InvalidOperationException(), null, _ => { }));

    [Fact]
    public void FailedFallbackStillPreservesOriginalException()
    {
        var original = new TimeoutException();
        SummaryNavigationObservationWriter.Write(() => throw new InvalidOperationException(), _ => { }, original,
            _ => throw new InvalidOperationException());
        Assert.True((bool)original.Data["SummaryNavigationObserverFallbackFailure"]!);
    }

    private sealed class ControlledResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> starting = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }
        public void OnStarting(Func<object, Task> callback, object state) => starting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { }
        internal async Task StartAsync()
        {
            foreach (var entry in starting.AsEnumerable().Reverse()) await entry.Callback(entry.State);
            HasStarted = true;
        }
    }
}
