using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real diagnostic decorator forwarding; no pricing/manufacturing acceptance claim.</summary>
public sealed class MaterialCompletionObservationForwardingTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task DecoratedPricing_ForwardsSameFrameAndTokenOnce_AndAwaitsRealObserver(
        bool asynchronous, bool selected)
    {
        using var fixture = new Fixture(selected);
        using var caller = new CancellationTokenSource();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        InstantQuotationMaterialPricingProgress? observedFrame = null;
        CancellationToken observedToken = default;
        ValueTask Observer(InstantQuotationMaterialPricingProgress frame, CancellationToken token)
        {
            calls++;
            observedFrame = frame;
            observedToken = token;
            return asynchronous ? new ValueTask(release.Task) : ValueTask.CompletedTask;
        }

        var invocation = fixture.Service.QuoteAsync(fixture.Session, "synthetic-owner", true,
            Observer, caller.Token);
        try
        {
            Assert.Equal(1, calls);
            Assert.Same(fixture.Frame, observedFrame);
            Assert.Equal(caller.Token, observedToken);
            fixture.AssertForwardedInvocation(caller.Token);
            if (asynchronous)
            {
                Assert.False(invocation.IsCompleted);
                release.SetResult(true);
            }
            Assert.Same(fixture.Quote, await invocation);
            Assert.Equal(1, calls);
            Assert.Equal(1, fixture.Inner.ObserverInvocations);
            Assert.Contains(selected ? "selectedCallbackReturned=1" : "comparisonCallbackReturned=1",
                fixture.Observation.Describe());
            Assert.Contains("returnedQuote=1", fixture.Observation.Describe());
        }
        finally { await ReleaseAndObserveAsync(release, invocation); }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task DecoratedPricing_ObserverFaultPreservesOriginalException_WithoutRetryOrQuote(
        bool asynchronous, bool selected)
    {
        using var fixture = new Fixture(selected);
        using var caller = new CancellationTokenSource();
        var original = new IOException("Synthetic observer fault");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        ValueTask Observer(InstantQuotationMaterialPricingProgress frame, CancellationToken token)
        {
            Assert.Same(fixture.Frame, frame);
            Assert.Equal(caller.Token, token);
            calls++;
            if (!asynchronous) throw original;
            return AwaitThenThrowAsync();
        }
        async ValueTask AwaitThenThrowAsync() { await release.Task; throw original; }

        var invocation = fixture.Service.QuoteAsync(fixture.Session, "synthetic-owner", true,
            Observer, caller.Token);
        try
        {
            fixture.AssertForwardedInvocation(caller.Token);
            if (asynchronous)
            {
                Assert.False(invocation.IsCompleted);
                release.SetResult(true);
            }
            Assert.Same(original, await Assert.ThrowsAsync<IOException>(() => invocation));
            Assert.Equal(1, calls);
            Assert.Equal(1, fixture.Inner.ObserverInvocations);
            Assert.Contains(selected ? "selectedCallbackFaulted=1" : "comparisonCallbackFaulted=1",
                fixture.Observation.Describe());
            Assert.Contains("faulted=1", fixture.Observation.Describe());
            Assert.Contains("returnedQuote=0", fixture.Observation.Describe());
        }
        finally { await ReleaseAndObserveAsync(release, invocation); }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task DecoratedPricing_ObserverCancellationPreservesOriginalExceptionAndToken(
        bool asynchronous, bool selected)
    {
        using var fixture = new Fixture(selected);
        using var caller = new CancellationTokenSource();
        var original = new OperationCanceledException("Synthetic observer cancellation", caller.Token);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        ValueTask Observer(InstantQuotationMaterialPricingProgress frame, CancellationToken token)
        {
            Assert.Same(fixture.Frame, frame);
            Assert.Equal(caller.Token, token);
            calls++;
            if (!asynchronous) { caller.Cancel(); throw original; }
            return AwaitThenCancelAsync();
        }
        async ValueTask AwaitThenCancelAsync() { await release.Task; throw original; }

        var invocation = fixture.Service.QuoteAsync(fixture.Session, "synthetic-owner", true,
            Observer, caller.Token);
        try
        {
            fixture.AssertForwardedInvocation(caller.Token);
            if (asynchronous)
            {
                Assert.False(invocation.IsCompleted);
                caller.Cancel();
                release.SetResult(true);
            }
            var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
            Assert.Same(original, observed);
            Assert.Equal(caller.Token, observed.CancellationToken);
            Assert.Equal(1, calls);
            Assert.Equal(1, fixture.Inner.ObserverInvocations);
            Assert.Contains(selected ? "selectedCallbackCanceled=1" : "comparisonCallbackCanceled=1",
                fixture.Observation.Describe());
            Assert.Contains("canceled=1", fixture.Observation.Describe());
            Assert.Contains("returnedQuote=0", fixture.Observation.Describe());
        }
        finally { await ReleaseAndObserveAsync(release, invocation); }
    }

    private static async Task ReleaseAndObserveAsync(TaskCompletionSource<bool> release, Task invocation)
    {
        release.TrySetResult(true);
        try { await invocation; }
        catch { /* Observe expected/secondary settlement without masking the first assertion failure. */ }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        public MaterialCompletionObservation Observation { get; } = new();
        public InstantQuotationSessionState Session { get; }
        public InstantQuotationMaterialPricingProgress Frame { get; }
        // Synthetic boundary result only: neither this quote nor its unused geometry authorizes an order.
        public InstantQuotationOrderQuote Quote { get; } = new([], 1, 1, 1, 0, 0, 1, 0, 1, 1, 1);
        public ForwardingPricing Inner { get; }
        public IInstantQuotationAuthoritativePricingService Service { get; }

        public Fixture(bool selected)
        {
            var partId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
            // Same minimal public DTO fixture shape as SelectedPrintTimeTimeoutDiagnosticsTests.
            var part = new InstantQuotationPart(partId, "synthetic.stl",
                new InstantQuotationUploadReference("synthetic"), null!,
                new InstantQuotationPartConfiguration("ABS", "White", 1));
            Session = new("synthetic-session", "synthetic-submission", new([part]),
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1));
            Frame = new(partId, selected ? "ABS" : "PLA", InstantQuotationMaterialPricingStatus.Completed, 1);
            Inner = new(Frame, Quote);
            var services = new ServiceCollection();
            services.AddSingleton<IInstantQuotationAuthoritativePricingService>(Inner);
            services.AddSingleton<IInstantQuotationQuoteTicketService>(new UnusedBoundaries());
            services.AddSingleton<IInstantQuotationSessionStore>(new UnusedBoundaries());
            Observation.Decorate(services);
            provider = services.BuildServiceProvider();
            Service = provider.GetRequiredService<IInstantQuotationAuthoritativePricingService>();
            Assert.NotSame(Inner, Service); // Exercises actual decorator, not a mirrored forwarding implementation.
            Observation.BeginEdit();
        }

        public void AssertForwardedInvocation(CancellationToken token)
        {
            Assert.Equal(1, Inner.Calls);
            Assert.Same(Session, Inner.Session);
            Assert.Equal("synthetic-owner", Inner.Owner);
            Assert.True(Inner.IncludeComparisons);
            Assert.Equal(token, Inner.Token);
        }
        public void Dispose() => provider.Dispose();
    }

    private sealed class ForwardingPricing(InstantQuotationMaterialPricingProgress frame,
        InstantQuotationOrderQuote quote) : IInstantQuotationAuthoritativePricingService
    {
        public int Calls { get; private set; }
        public int ObserverInvocations { get; private set; }
        public InstantQuotationSessionState? Session { get; private set; }
        public string? Owner { get; private set; }
        public bool IncludeComparisons { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? ownerIdentity, bool includeComparisons, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected non-observer overload");

        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? ownerIdentity, bool includeComparisons,
            Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken cancellationToken)
        {
            Calls++;
            Session = session;
            Owner = ownerIdentity;
            IncludeComparisons = includeComparisons;
            Token = cancellationToken;
            ObserverInvocations++;
            await observer(frame, cancellationToken);
            return quote;
        }
    }

    // Required registrations for actual Decorate(); all remain unused and fail if forwarding reaches them.
    private sealed class UnusedBoundaries : IInstantQuotationQuoteTicketService, IInstantQuotationSessionStore
    {
        public InstantQuotationQuoteAuthorization Issue(InstantQuotationSessionState session,
            InstantQuotationOrderQuote quote, DateTimeOffset now) => throw new InvalidOperationException("Unused ticket boundary");
        public bool Validate(InstantQuotationSessionState session, InstantQuotationOrderQuote quote,
            InstantQuotationQuoteAuthorization authorization, DateTimeOffset now) => throw new InvalidOperationException("Unused ticket boundary");
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity,
            InstantQuotationOrderState requestState, CancellationToken cancellationToken) => throw new InvalidOperationException("Unused store boundary");
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Unused store boundary");
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Unused store boundary");
        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Unused store boundary");
    }
}
