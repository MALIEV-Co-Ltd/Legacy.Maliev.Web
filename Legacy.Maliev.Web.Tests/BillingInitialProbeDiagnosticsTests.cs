using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Native actor-free controls; no MemberAuthority fixture, server, socket or database.</summary>
public sealed class BillingInitialProbeDiagnosticsTests
{
    [Theory]
    [InlineData("request-path")]
    [InlineData("response-status")]
    public async Task FaultingDiagnosticFeaturesPreserveTheExactDownstreamException(string fault)
    {
        var observer = new BillingInitialProbeDiagnostics("en", 1280, new string('a', 32));
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var sentinel = new InvalidOperationException("private-downstream-message-must-not-be-retained");
        var calls = 0;
        observer.Configure(app => app.Run(_ => { calls++; return Task.FromException(sentinel); }))(builder);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/Account/Login";
        context.Request.QueryString = new QueryString("?culture=en");
        if (fault == "request-path") context.Features.Set<IHttpRequestFeature>(new FaultingRequest());
        else context.Features.Set<IHttpResponseFeature>(new FaultingResponse());
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.Build()(context));
        Assert.Same(sentinel, actual);
        Assert.Equal(1, calls);
        observer.Stop();
        Assert.False(observer.State.Active);
    }

    [Fact]
    public void ProviderNeverFormatsOrReadsStateAndHasAFiniteStoppedInventory()
    {
        var observer = new BillingInitialProbeDiagnostics("th", 375, new string('a', 32));
        var logger = observer.CreateLogger("Microsoft.AspNetCore.DataProtection.KeyManagement.KeyRingProvider");
        var state = new PoisonState();
        for (var index = 0; index < 100; index++)
            logger.Log(LogLevel.Debug, new EventId(1, "private-event-name"), state,
                new InvalidOperationException("private-exception"), (_, _) => throw new InvalidOperationException("formatter must never execute"));
        Assert.Equal(16, observer.State.ProviderEvents);
        Assert.Equal(16, observer.State.Dropped);
        observer.Stop();
        var stopped = observer.State;
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        logger.Log(LogLevel.Debug, new EventId(2), state, null, (_, _) => throw new InvalidOperationException("formatter must never execute"));
        observer.Observe(BillingInitialProbeDiagnostics.Stage.GetInvoked);
        Assert.Equal(stopped, observer.State);
    }

    [Fact]
    public async Task MismatchedRequestAndUnrelatedProviderAreNotObserved()
    {
        var observer = new BillingInitialProbeDiagnostics("en", 1280, new string('a', 32));
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        var calls = 0;
        observer.Configure(app => app.Run(_ => { calls++; return Task.CompletedTask; }))(builder);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/Account/Login";
        context.Request.QueryString = new QueryString("?culture=en");
        await builder.Build()(context);
        var logger = observer.CreateLogger("Private.Unrelated.Provider");
        logger.Log(LogLevel.Critical, new EventId(1), new PoisonState(), null, (_, _) => throw new InvalidOperationException("must never format"));
        Assert.Equal(1, calls);
        Assert.Equal(0, observer.State.Events);
        Assert.Equal(0, observer.State.ProviderEvents);
        observer.Stop();
    }

    [Fact]
    public void StageInventoryIsBoundedAndProviderDisposalIsInertAfterStop()
    {
        var observer = new BillingInitialProbeDiagnostics("en", 1280, new string('a', 32));
        for (var index = 0; index < 100; index++) observer.Observe(BillingInitialProbeDiagnostics.Stage.CertificatePinAccepted);
        Assert.Equal(16, observer.State.Events);
        Assert.Equal(16, observer.State.Dropped);
        observer.Stop();
        var stopped = observer.State;
        observer.Dispose(); // Already stopped: no receipt file is written by this unit control.
        Assert.Equal(stopped, observer.State);
    }

    private sealed class PoisonState
    {
        public override string ToString() => throw new InvalidOperationException("state must not be inspected");
    }

    private sealed class FaultingRequest : IHttpRequestFeature
    {
        public string Protocol { get; set; } = "HTTP/1.1";
        public string Scheme { get; set; } = "https";
        public string Method { get; set; } = "GET";
        public string PathBase { get; set; } = "";
        public string Path { get => throw new InvalidOperationException("private-feature-message"); set { } }
        public string QueryString { get; set; } = "?culture=en";
        public string RawTarget { get; set; } = "/Account/Login?culture=en";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
    }

    private sealed class FaultingResponse : IHttpResponseFeature
    {
        public int StatusCode { get => throw new InvalidOperationException("private-feature-message"); set { } }
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
