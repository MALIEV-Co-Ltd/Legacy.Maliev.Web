using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual coordinator and protected store; controlled pricing latency is not manufacturing proof.</summary>
public sealed class MaterialComparisonRepricingContinuityTests
{
    [Fact]
    public async Task PersistenceRefusal_InvalidatesPublishedPricesBeforePricingButPreservesStoredPriorRequest()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Boundary.RefuseNextPut = true;
        var calls = fixture.Pricing.Calls;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workflow.UpdateConfigurationAsync(
            fixture.PartId, "M68", "White", 2, default));
        Assert.Equal(calls, fixture.Pricing.Calls);
        var prior = (await fixture.Store.GetAsync(fixture.SessionId, null, default))!;
        Assert.Equal(1, Assert.Single(prior.Parts).Configuration.Quantity);
        Assert.NotNull(prior.QuoteAuthorization);
        Assert.Equal(2, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
        Assert.Null(Assert.Single(fixture.Workflow.Parts).Quote);
    }

    [Fact]
    public async Task CancellationAtFirstPersistence_InvalidatesPublishedPricesAndPropagatesActualCallerToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        fixture.Boundary.PauseNextPut = true;
        var calls = fixture.Pricing.Calls;
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, caller.Token);
        await fixture.Boundary.PutReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        Assert.Equal(caller.Token, canceled.CancellationToken);
        Assert.Equal(calls, fixture.Pricing.Calls);
        var prior = (await fixture.Store.GetAsync(fixture.SessionId, null, default))!;
        Assert.Equal(1, Assert.Single(prior.Parts).Configuration.Quantity);
        Assert.NotNull(prior.QuoteAuthorization);
        Assert.Equal(2, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
        Assert.Null(Assert.Single(fixture.Workflow.Parts).Quote);
    }

    [Theory]
    [InlineData("not-a-material", "White", 1)]
    [InlineData("M68", "not-a-color", 1)]
    [InlineData("M68", "White", 0)]
    public async Task InvalidConfiguration_RejectedBeforeMutationPreservesValidPublishedQuote(
        string material, string color, int quantity)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prior = fixture.Workflow.OrderQuote;
        var calls = fixture.Pricing.Calls;
        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Workflow.UpdateConfigurationAsync(
            fixture.PartId, material, color, quantity, default));
        Assert.Same(prior, fixture.Workflow.OrderQuote);
        Assert.Equal(calls, fixture.Pricing.Calls);
        var part = Assert.Single(fixture.Workflow.Parts);
        Assert.Equal("M68", part.Configuration.MaterialKey);
        Assert.Equal("White", part.Configuration.Color);
        Assert.Equal(1, part.Configuration.Quantity);
        Assert.NotNull(part.Quote);
    }

    [Fact]
    public async Task MaterialChange_DelayedAuthoritativeQuoteRemovesPreviousMaterialPricesImmediately()
    {
        await using var fixture = await Fixture.CreateAsync();
        var prior = Assert.Single(fixture.Workflow.Parts).Quote;
        Assert.NotNull(prior);
        Assert.NotEmpty(prior.MaterialPrices);
        var pending = fixture.Pricing.DelayNext();
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "K", "Gray", 1, default);
        try
        {
            await pending.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var visible = Assert.Single(fixture.Workflow.Parts);
            Assert.Equal("K", visible.Configuration.MaterialKey);
            var stored = await fixture.Store.GetAsync(fixture.SessionId, null, default);
            Assert.Equal("K", Assert.Single(Assert.IsType<InstantQuotationSessionState>(stored).Parts).Configuration.MaterialKey);
            Assert.Null(stored!.QuoteAuthorization);
            // Razor's material comparison consumes this exact PartViewModel.Quote, not a copied view.
            Assert.Null(visible.Quote);
        }
        finally
        {
            pending.Completion.TrySetResult(true);
            await update;
        }
    }

    [Fact]
    public async Task QuantityChange_CallerCancellationCannotLeavePreviousComparisonPricesVisible()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        var pending = fixture.Pricing.DelayNext();
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, caller.Token);
        await pending.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        Assert.Equal(caller.Token, canceled.CancellationToken);
        Assert.Equal(2, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
        Assert.Null(Assert.Single(fixture.Workflow.Parts).Quote);
    }

    [Fact]
    public async Task FailedRepricing_CannotLeavePreviousComparisonPricesVisible()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Pricing.DelayNext();
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await pending.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var failure = new IOException("Controlled pricing boundary failure");
        pending.Completion.SetException(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => update));
        Assert.Equal(2, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
        Assert.Null(Assert.Single(fixture.Workflow.Parts).Quote);
    }

    [Fact]
    public async Task UnavailableAuthoritativeResult_RemovesComparisonPricesRatherThanInventingZero()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Pricing.DelayNext();
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await pending.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pending.Completion.SetResult(false);
        await update;
        Assert.Null(fixture.Workflow.OrderQuote);
        Assert.Null(Assert.Single(fixture.Workflow.Parts).Quote);
        Assert.False(fixture.Workflow.HasCompleteAuthoritativeEstimate);
    }

    [Fact]
    public async Task SerializedNewerConfiguration_WinsAfterOlderDelayedCompletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = fixture.Pricing.DelayNext();
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await older.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = fixture.Pricing.DelayNext();
        var second = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 3, default);
        Assert.False(newer.Reached.Task.IsCompleted);
        older.Completion.SetResult(true);
        await first;
        await newer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        newer.Completion.SetResult(true);
        await second;
        Assert.Equal(3, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
        Assert.Equal(3, Assert.Single(Assert.IsType<InstantQuotationOrderQuote>(fixture.Workflow.OrderQuote).Parts).Quantity);
        Assert.Equal(3, Assert.Single((await fixture.Store.GetAsync(fixture.SessionId, null, default))!.Parts).Configuration.Quantity);
    }

    private sealed class PendingQuote
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ControlledAuthoritativePricing : IInstantQuotationAuthoritativePricingService
    {
        private readonly Queue<PendingQuote> pending = new();
        public int Calls { get; private set; }

        public PendingQuote DelayNext()
        {
            var next = new PendingQuote();
            pending.Enqueue(next);
            return next;
        }

        public async Task<InstantQuotationOrderQuote?> QuoteAsync(
            InstantQuotationSessionState session, string? ownerIdentity,
            bool includeComparisons, CancellationToken cancellationToken)
        {
            Calls++;
            if (pending.TryDequeue(out var next))
            {
                next.Reached.SetResult();
                if (!await next.Completion.Task.WaitAsync(cancellationToken))
                {
                    return null;
                }
            }
            // Actual provisional resin kernel; unreceipted FDM comparisons remain unavailable.
            // Synthetic geometry and controlled latency are not production upload/manufacturing proof.
            return new InstantQuotationPricingService().Quote(session.RequestState);
        }
    }

    private sealed class PersistenceBoundary(IInstantQuotationSessionStore store) : IInstantQuotationSessionStore
    {
        public bool RefuseNextPut { get; set; }
        public bool PauseNextPut { get; set; }
        public TaskCompletionSource PutReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<InstantQuotationSessionState> CreateAsync(string? owner, InstantQuotationOrderState state, CancellationToken token)
            => store.CreateAsync(owner, state, token);

        public Task<InstantQuotationSessionState?> GetAsync(string id, string? owner, CancellationToken token)
            => store.GetAsync(id, owner, token);

        public Task<bool> RemoveAsync(string id, string? owner, CancellationToken token)
            => store.RemoveAsync(id, owner, token);

        public async Task<bool> PutAsync(InstantQuotationSessionState session, string? owner, CancellationToken token)
        {
            if (RefuseNextPut)
            {
                RefuseNextPut = false;
                return false;
            }
            if (PauseNextPut)
            {
                PauseNextPut = false;
                PutReached.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return await store.PutAsync(session, owner, token);
        }
    }

    private sealed class UnusedUploadClient : IInstantQuotationUploadClient
    {
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity,
            Stream content, string fileName, string contentType, long contentLength,
            InstantQuotationGeometryClaim geometryClaim, string operationId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Restored-session test must not upload");

        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Repricing test must not remove an upload");

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity,
            int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Repricing test must not finalize an upload");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        public DistributedInstantQuotationSessionStore Store { get; }
        public PersistenceBoundary Boundary { get; }
        public ControlledAuthoritativePricing Pricing { get; } = new();
        public InstantQuotationWorkflowCoordinator Workflow { get; }
        public Guid PartId { get; } = Guid.NewGuid();
        public string SessionId { get; private set; } = "";

        private Fixture()
        {
            services = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
            var protection = new EphemeralDataProtectionProvider();
            Store = new DistributedInstantQuotationSessionStore(
                services.GetRequiredService<IDistributedCache>(), protection,
                TimeProvider.System, NullLogger<DistributedInstantQuotationSessionStore>.Instance);
            Boundary = new PersistenceBoundary(Store);
            Workflow = new InstantQuotationWorkflowCoordinator(Boundary, new UnusedUploadClient(),
                new InstantQuotationPricingService(), null,
                quoteTicketService: new AdditiveQuoteTicketService(protection), authoritativePricingService: Pricing);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var result = new Fixture();
            var claim = new InstantQuotationGeometryClaim(1, new string('a', 64), 10, 10, 10,
                1_000, 600, Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(40d, 64).ToArray(),
                100, 1, true, false, false, 0.8);
            var upload = InstantQuotationUploadResult.Succeeded("synthetic-prior-upload",
                new InstantQuotationUploadReference("synthetic-opaque"), claim.Sha256);
            var part = new InstantQuotationPart(result.PartId, "synthetic.stl", upload.UploadReference!,
                AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!,
                new InstantQuotationPartConfiguration("M68", "White", 1));
            var session = await result.Store.CreateAsync(null, new InstantQuotationOrderState([part]), default);
            result.SessionId = session.SessionId;
            await result.Workflow.InitializeAsync(session.SessionId, default);
            Assert.NotNull(Assert.Single(result.Workflow.Parts).Quote);
            Assert.NotNull((await result.Store.GetAsync(session.SessionId, null, default))!.QuoteAuthorization);
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            await Workflow.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
