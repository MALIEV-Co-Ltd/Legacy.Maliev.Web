using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Display seam/observer reach, with registered kernel/store; not real uploaded-byte or renderer proof.</summary>
public sealed class MaterialPricingDisplayContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Coordinator_MustDeliverRealSelectedDisplayBeforeComparisonCompletes(bool restoring)
    {
        await using var fixture = await Fixture.CreateAsync(initialize: !restoring);
        var pending = fixture.Pricing.Arm();
        var revision = fixture.Workflow.AuthoritativeQuoteRevision;
        var run = restoring ? fixture.Workflow.InitializeAsync(fixture.SessionId, default)
            : fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(fixture.Workflow.OrderQuote);
            Assert.Equal(revision, fixture.Workflow.AuthoritativeQuoteRevision);
            var stored = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Null(stored.QuoteAuthorization);
            Assert.True(pending.ObserverRoute, "The coordinator reached ordinary pricing without a display observer.");
            var selected = Assert.Single(fixture.Workflow.MaterialPriceDisplay.Entries, row => row.MaterialKey == "M68");
            Assert.Equal(InstantQuotationMaterialPricingStatus.Completed, selected.Status);
            Assert.Equal(restoring ? 770d : 600d, selected.UnitPrice);
            Assert.Equal(fixture.PartId, selected.PartId);
            Assert.Throws<InvalidOperationException>(fixture.Workflow.EnterReview);
        }
        finally { pending.Release.TrySetResult(); await run; }
        Assert.Equal(restoring ? 930.9d : 1391d, fixture.Workflow.OrderQuote!.FinalOrderPrice);
        Assert.Equal(restoring ? 1 : 2, Assert.Single(fixture.Workflow.OrderQuote.Parts).Quantity);
    }

    [Fact]
    public async Task DirectBackend_ControlDeliversLiteralSelectedFramesWithoutProtectedWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
        var part = Assert.Single(before.Parts);
        var request = before with
        {
            RequestState = new InstantQuotationOrderState([part with
            {
                Configuration = part.Configuration with { Quantity = 2 },
            }]),
        };
        var pending = fixture.Pricing.Arm();
        var run = fixture.Pricing.QuoteAsync(request, Owner, true,
            (_, _) => ValueTask.CompletedTask, default);
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(pending.ObserverRoute);
            var selected = Assert.Single(pending.Frames, frame => frame.Status == InstantQuotationMaterialPricingStatus.Completed);
            Assert.Equal("M68", selected.MaterialKey);
            Assert.Equal(600d, selected.UnitPrice);
            Assert.Equal(InstantQuotationMaterialPricingStatus.Completed, selected.Status);
            var current = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Equal(before.UpdatedAt, current.UpdatedAt);
            Assert.True(before.QuoteAuthorization!.OrderTicket == current.QuoteAuthorization!.OrderTicket,
                "Display frames changed protected authorization.");
            Assert.False(run.IsCompleted);
        }
        finally { pending.Release.TrySetResult(); }
        var final = await run;
        Assert.Equal(600d, Assert.Single(final!.Parts).UnitPrice);
        Assert.Equal(1391d, final.FinalOrderPrice);
        Assert.Contains(pending.Frames, row => row.MaterialKey != "M68");
    }

    [Fact]
    public async Task QueuedIntent_DoesNotBecomeAcceptedConfigurationBeforeStateGate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = fixture.Pricing.Arm();
        var firstRun = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstDisplay = fixture.Workflow.MaterialPriceDisplay;
        var second = fixture.Pricing.Arm();
        var secondRun = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 3, default);
        try
        {
            Assert.False(second.Entered.Task.IsCompleted);
            Assert.Same(firstDisplay, fixture.Workflow.MaterialPriceDisplay);
            Assert.Equal(2, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
            var accepted = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Equal(2, Assert.Single(accepted.Parts).Configuration.Quantity);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 0, default));
            Assert.Equal(accepted.UpdatedAt, (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.UpdatedAt);
            first.Release.TrySetResult(); await firstRun;
            await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fixture.Workflow.MaterialPriceDisplay.Generation > firstDisplay.Generation);
            Assert.Equal(3, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
            Assert.Equal(3, Assert.Single((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.Parts).Configuration.Quantity);
        }
        finally { first.Release.TrySetResult(); second.Release.TrySetResult(); await Task.WhenAll(firstRun, secondRun); }
    }

    [Fact]
    public async Task LateRealFrame_FromPreviousAcceptedGenerationCannotChangeCurrentSnapshot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = fixture.Pricing.Arm();
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await older.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oldFrame = Assert.Single(older.Frames, frame => frame.Status == InstantQuotationMaterialPricingStatus.Completed);
        older.Release.TrySetResult(); await first;
        var newer = fixture.Pricing.Arm();
        var second = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 3, default);
        try
        {
            await newer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var current = fixture.Workflow.MaterialPriceDisplay;
            var before = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            await older.Observer!(oldFrame, default);
            Assert.Same(current, fixture.Workflow.MaterialPriceDisplay);
            Assert.Null(fixture.Workflow.OrderQuote);
            var after = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Equal(before.UpdatedAt, after.UpdatedAt);
            Assert.Null(after.QuoteAuthorization);
        }
        finally { newer.Release.TrySetResult(); await second; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrDisposal_ClearsActualSnapshotAndRejectsLateRealFrame(bool dispose)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        var pending = fixture.Pricing.Arm();
        var run = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, caller.Token);
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEmpty(fixture.Workflow.MaterialPriceDisplay.Entries);
            if (dispose) await fixture.Workflow.DisposeAsync(); else caller.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            if (!dispose) Assert.Equal(caller.Token, error.CancellationToken);
            var empty = fixture.Workflow.MaterialPriceDisplay;
            Assert.Empty(empty.Entries);
            await pending.Observer!(Assert.Single(pending.Frames, frame => frame.Status == InstantQuotationMaterialPricingStatus.Completed), default);
            Assert.Same(empty, fixture.Workflow.MaterialPriceDisplay);
            Assert.Null(fixture.Workflow.OrderQuote);
            Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        }
        finally { pending.Release.TrySetResult(); try { await run; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task InvalidAndMissingPartRequests_DoNotInvalidateAcceptedDisplay()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Pricing.Arm();
        var run = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var accepted = fixture.Workflow.MaterialPriceDisplay;
        var missing = fixture.Workflow.UpdateConfigurationAsync(Guid.Parse("77777777-7777-7777-7777-777777777777"), "M68", "White", 3, default);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "unknown", "White", 3, default));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 0, default));
            Assert.Same(accepted, fixture.Workflow.MaterialPriceDisplay);
            pending.Release.TrySetResult(); await run;
            await Assert.ThrowsAsync<ArgumentException>(() => missing);
            Assert.Equal(accepted.Generation, fixture.Workflow.MaterialPriceDisplay.Generation);
            Assert.NotEmpty(fixture.Workflow.MaterialPriceDisplay.Entries);
            Assert.Equal(1391d, fixture.Workflow.OrderQuote!.FinalOrderPrice);
        }
        finally { pending.Release.TrySetResult(); await run; try { await missing; } catch (ArgumentException) { } }
    }

    [Fact]
    public async Task KnownProtectedPeerDrift_ClearsProgressAtExistingFinalReadback()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Pricing.Arm();
        var run = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.NotEmpty(fixture.Workflow.MaterialPriceDisplay.Entries);
            var stored = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            var part = Assert.Single(stored.Parts);
            Assert.True(await fixture.Store.PutAsync(stored with
            {
                RequestState = new InstantQuotationOrderState([part with { Configuration = part.Configuration with { Quantity = 3 } }]),
            }, Owner, default));
        }
        finally { pending.Release.TrySetResult(); await run; }
        Assert.Empty(fixture.Workflow.MaterialPriceDisplay.Entries);
        Assert.Null(fixture.Workflow.OrderQuote);
        Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        await pending.Observer!(Assert.Single(pending.Frames, frame => frame.MaterialKey == "M68" && frame.Status == InstantQuotationMaterialPricingStatus.Completed), default);
        Assert.Empty(fixture.Workflow.MaterialPriceDisplay.Entries);
    }

    [Fact]
    public async Task Subscriber_CanReadSnapshotAndUnsubscribeWithoutDisplayLockOrStateGateDeadlock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var calls = 0;
        IDisposable? subscription = null;
        subscription = fixture.Workflow.SubscribeMaterialPriceDisplay((snapshot, _) =>
        {
            Assert.Same(snapshot, fixture.Workflow.MaterialPriceDisplay);
            calls++;
            if (snapshot.Entries.Any(row => row.MaterialKey == "M68" && row.Status == InstantQuotationMaterialPricingStatus.Completed))
                subscription!.Dispose();
            return ValueTask.CompletedTask;
        });
        using (subscription)
        {
            await fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, calls);
            Assert.Equal(1391d, fixture.Workflow.OrderQuote!.FinalOrderPrice);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubscriberFailure_DoesNotBecomeUsableQuoteOrSilentRestoreFallback(bool restoring)
    {
        await using var fixture = await Fixture.CreateAsync(initialize: !restoring);
        var cause = new ArgumentException("controlled subscriber failure");
        using var subscription = fixture.Workflow.SubscribeMaterialPriceDisplay((_, _) => throw cause);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => restoring
            ? fixture.Workflow.InitializeAsync(fixture.SessionId, default)
            : fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default));
        Assert.Same(cause, error.InnerException);
        Assert.Null(fixture.Workflow.OrderQuote);
        Assert.Empty(fixture.Workflow.MaterialPriceDisplay.Entries);
        Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        Assert.Equal(fixture.SessionId, fixture.Workflow.ProtectedSessionIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoncooperativeSubscriber_IsBoundedAndLateFaultCannotWriteAuthorization(bool callerAbort)
    {
        var clock = new ManualClock();
        await using var fixture = await Fixture.CreateAsync(clock: clock);
        using var caller = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken callbackToken = default;
        using var subscription = fixture.Workflow.SubscribeMaterialPriceDisplay((snapshot, token) =>
        {
            if (!snapshot.Entries.Any(row => row.MaterialKey == "M68" && row.Status == InstantQuotationMaterialPricingStatus.Completed))
                return ValueTask.CompletedTask;
            callbackToken = token; entered.TrySetResult();
            return new ValueTask(BlockAsync());
            async Task BlockAsync() { try { await release.Task; } finally { exited.TrySetResult(); } }
        });
        var run = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, caller.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var persisted = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            if (callerAbort)
            {
                caller.Cancel();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(caller.Token, error.CancellationToken);
            }
            else { clock.Advance(TimeSpan.FromSeconds(30)); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            Assert.True(callbackToken.IsCancellationRequested);
            Assert.False(exited.Task.IsCompleted);
            Assert.Empty(fixture.Workflow.MaterialPriceDisplay.Entries);
            Assert.Null(fixture.Workflow.OrderQuote);
            release.TrySetException(new InvalidOperationException("controlled late subscriber fault"));
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var after = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Equal(persisted.UpdatedAt, after.UpdatedAt);
            Assert.Null(after.QuoteAuthorization);
            Assert.Equal(2, Assert.Single(after.Parts).Configuration.Quantity);
        }
        finally { release.TrySetResult(); await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task SynchronousSubscriberBlock_IsBoundedWithoutClaimingThreadTermination()
    {
        var clock = new ManualClock();
        await using var fixture = await Fixture.CreateAsync(clock: clock);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = fixture.Workflow.SubscribeMaterialPriceDisplay((snapshot, _) =>
        {
            if (!snapshot.Entries.Any(row => row.Status == InstantQuotationMaterialPricingStatus.Completed)) return ValueTask.CompletedTask;
            entered.TrySetResult();
            try { release.Wait(); } finally { exited.TrySetResult(); }
            return ValueTask.CompletedTask;
        });
        var run = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(30)); await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(exited.Task.IsCompleted);
            Assert.Null(fixture.Workflow.OrderQuote);
            Assert.Empty(fixture.Workflow.MaterialPriceDisplay.Entries);
            Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        }
        finally { release.Set(); await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ReentrantMutator_IsCanceledAtProducerBudget_NotGracefulReentrancy()
    {
        var clock = new ManualClock();
        await using var fixture = await Fixture.CreateAsync(clock: clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? nested = null;
        CancellationToken token = default;
        using var subscription = fixture.Workflow.SubscribeMaterialPriceDisplay((snapshot, callbackToken) =>
        {
            if (!snapshot.Entries.Any(row => row.Status == InstantQuotationMaterialPricingStatus.Completed)) return ValueTask.CompletedTask;
            token = callbackToken;
            nested = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 3, callbackToken);
            entered.TrySetResult();
            return new ValueTask(nested);
        });
        var run = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(30));
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => nested!.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(token, error.CancellationToken);
        Assert.Null(fixture.Workflow.OrderQuote);
        Assert.Empty(fixture.Workflow.MaterialPriceDisplay.Entries);
        var stored = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
        Assert.Equal(2, Assert.Single(stored.Parts).Configuration.Quantity);
        Assert.Null(stored.QuoteAuthorization);
    }

    [Fact]
    public async Task TwoParts_PublishSeparateRowsAndPreserveLiteralFinalQuote()
    {
        await using var fixture = await Fixture.CreateAsync(partCount: 2);
        var pending = fixture.Pricing.Arm();
        var run = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var selected = Assert.Single(fixture.Workflow.MaterialPriceDisplay.Entries);
            Assert.Equal(fixture.PartId, selected.PartId);
            Assert.Equal(600d, selected.UnitPrice);
            Assert.Null(fixture.Workflow.OrderQuote);
        }
        finally { pending.Release.TrySetResult(); await run; }
        var quote = fixture.Workflow.OrderQuote!;
        Assert.Equal(new[] { fixture.PartId, Guid.Parse("88888888-8888-8888-8888-888888888888") }, quote.Parts.Select(part => part.PartId));
        Assert.Equal(new[] { 600d, 770d }, quote.Parts.Select(part => part.UnitPrice));
        Assert.Equal(2214.9d, quote.FinalOrderPrice);
        foreach (var part in quote.Parts)
        {
            var row = Assert.Single(fixture.Workflow.MaterialPriceDisplay.Entries,
                entry => entry.PartId == part.PartId && entry.MaterialKey == part.MaterialKey);
            Assert.Equal(part.UnitPrice, row.UnitPrice);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Snapshot_TerminalTransitionDoesNotMutatePreviouslyPublishedPending(bool unavailable)
    {
        var empty = new InstantQuotationMaterialPriceDisplaySnapshot(7);
        var pending = empty.Apply(new(PartId, "M68", InstantQuotationMaterialPricingStatus.Pending, null));
        var terminal = pending.Apply(new(PartId, "M68",
            unavailable ? InstantQuotationMaterialPricingStatus.Unavailable : InstantQuotationMaterialPricingStatus.Completed,
            unavailable ? null : 600d));
        Assert.Empty(empty.Entries);
        Assert.Equal(InstantQuotationMaterialPricingStatus.Pending, Assert.Single(pending.Entries).Status);
        Assert.Equal(7, terminal.Generation);
        Assert.Equal(unavailable ? null : 600d, Assert.Single(terminal.Entries).UnitPrice);
        var list = Assert.IsAssignableFrom<IList<InstantQuotationMaterialPricingProgress>>(terminal.Entries);
        Assert.Throws<NotSupportedException>(() => list[0] = new(PartId, "M68", InstantQuotationMaterialPricingStatus.Pending, null));
        Assert.Same(terminal, terminal.Apply(Assert.Single(terminal.Entries)));
        Assert.Throws<InvalidOperationException>(() => terminal.Apply(new(PartId, "M68", InstantQuotationMaterialPricingStatus.Pending, null)));
        Assert.Throws<InvalidOperationException>(() => terminal.Apply(new(PartId, "M68",
            unavailable ? InstantQuotationMaterialPricingStatus.Completed : InstantQuotationMaterialPricingStatus.Unavailable,
            unavailable ? 601d : null)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("null-key")]
    [InlineData("unknown-part")]
    [InlineData("unknown-key")]
    [InlineData("nonfinite")]
    [InlineData("unknown-status")]
    public async Task ActualCallback_MalformedFrameFailsClosedWithFixedCategory(string malformed)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Pricing.Arm(frame => malformed switch
        {
            "null" => null,
            "null-key" => frame with { MaterialKey = null! },
            "unknown-part" => frame with { PartId = Guid.Parse("77777777-7777-7777-7777-777777777777") },
            "unknown-key" => frame with { MaterialKey = "not-a-material" },
            "nonfinite" => frame with { Status = InstantQuotationMaterialPricingStatus.Completed, UnitPrice = double.NaN },
            _ => frame with { Status = (InstantQuotationMaterialPricingStatus)99 },
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default));
        Assert.Empty(fixture.Workflow.MaterialPriceDisplay.Entries);
        Assert.Null(fixture.Workflow.OrderQuote);
        Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1d)]
    public void Snapshot_NonfiniteOrNegativeCompletedAmountIsRejected(double amount)
    {
        var snapshot = new InstantQuotationMaterialPriceDisplaySnapshot(1)
            .Apply(new(PartId, "M68", InstantQuotationMaterialPricingStatus.Pending, null));
        Assert.Throws<ArgumentException>(() => snapshot.Apply(new(PartId, "M68", InstantQuotationMaterialPricingStatus.Completed, amount)));
        Assert.Null(Assert.Single(snapshot.Entries).UnitPrice);
    }

    [Theory]
    [InlineData(InstantQuotationMaterialPricingStatus.Pending)]
    [InlineData(InstantQuotationMaterialPricingStatus.Unavailable)]
    public void Snapshot_NonCompletedAmountIsNotZeroOrNumeric(InstantQuotationMaterialPricingStatus status)
    {
        var snapshot = new InstantQuotationMaterialPriceDisplaySnapshot(1);
        Assert.Throws<ArgumentException>(() => snapshot.Apply(new(PartId, "M68", status, 0d)));
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public void Snapshot_MissingIdentityAmountAndUnknownStatusFailClosed()
    {
        var snapshot = new InstantQuotationMaterialPriceDisplaySnapshot(1);
        Assert.Throws<ArgumentNullException>(() => snapshot.Apply(null!));
        Assert.Throws<ArgumentException>(() => snapshot.Apply(new(Guid.Empty, "M68", InstantQuotationMaterialPricingStatus.Pending, null)));
        Assert.Throws<ArgumentException>(() => snapshot.Apply(new(PartId, "", InstantQuotationMaterialPricingStatus.Pending, null)));
        Assert.Throws<ArgumentException>(() => snapshot.Apply(new(PartId, "M68", (InstantQuotationMaterialPricingStatus)99, null)));
        Assert.Throws<ArgumentException>(() => snapshot.Apply(new(PartId, "M68", InstantQuotationMaterialPricingStatus.Completed, null)));
        Assert.Throws<InvalidOperationException>(() => snapshot.Apply(new(PartId, "M68", InstantQuotationMaterialPricingStatus.Completed, 600)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstantQuotationMaterialPriceDisplaySnapshot(0));
    }

    private const string Owner = "material-display-fixture-owner";
    private static readonly Guid PartId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private sealed class Pending
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObserverRoute { get; set; }
        public List<InstantQuotationMaterialPricingProgress> Frames { get; } = [];
        public InstantQuotationMaterialPriceDisplaySnapshot Snapshot { get; set; } = new(1);
        public Func<InstantQuotationMaterialPricingProgress, InstantQuotationMaterialPricingProgress?>? InvalidFrame { get; set; }
        public Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask>? Observer { get; set; }
    }

    // Both overloads reach the actual kernel. This captures only display output, not a partial OrderQuote.
    private sealed class DisplayBoundary(IInstantQuotationAuthoritativePricingService inner)
        : IInstantQuotationAuthoritativePricingService
    {
        private readonly Queue<Pending> pending = new();
        private readonly List<Pending> all = [];
        public Pending Arm(Func<InstantQuotationMaterialPricingProgress, InstantQuotationMaterialPricingProgress?>? invalidFrame = null)
        { var next = new Pending { InvalidFrame = invalidFrame }; pending.Enqueue(next); all.Add(next); return next; }
        public void ReleaseAll() { foreach (var entry in all) entry.Release.TrySetResult(); }
        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool includeComparisons, CancellationToken token)
        {
            if (pending.TryDequeue(out var next))
            {
                next.Entered.TrySetResult(); await next.Release.Task.WaitAsync(token);
            }
            return await inner.QuoteAsync(session, owner, includeComparisons, token);
        }
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool includeComparisons,
            Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer, CancellationToken token)
        {
            pending.TryDequeue(out var next);
            if (next is not null) next.Observer = observer;
            bool paused = false;
            bool injected = false;
            return inner.QuoteAsync(session, owner, includeComparisons, async (frame, callbackToken) =>
            {
                if (!injected && next?.InvalidFrame is { } invalid)
                {
                    injected = true;
                    await observer(invalid(frame)!, callbackToken);
                }
                await observer(frame, callbackToken);
                if (next is not null)
                {
                    next.Frames.Add(frame);
                    next.Snapshot = next.Snapshot.Apply(frame);
                }
                if (!paused && next is not null && frame.Status == InstantQuotationMaterialPricingStatus.Completed
                    && frame.MaterialKey == session.Parts.First().Configuration.MaterialKey)
                {
                    paused = true; next.ObserverRoute = true; next.Entered.TrySetResult();
                    await next.Release.Task.WaitAsync(callbackToken);
                }
            }, token);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TestingWebApplicationFactory factory;
        private readonly AsyncServiceScope scope;
        public IInstantQuotationSessionStore Store { get; }
        public DisplayBoundary Pricing { get; }
        public InstantQuotationWorkflowCoordinator Workflow { get; }
        public Guid PartId { get; } = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public string SessionId { get; private set; } = "";
        private Fixture(TimeProvider? clock)
        {
            factory = new Factory(clock);
            scope = factory.Services.CreateAsyncScope();
            Store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
            Pricing = new(scope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>());
            Workflow = new(Store,
                scope.ServiceProvider.GetRequiredService<IInstantQuotationUploadClient>(),
                scope.ServiceProvider.GetRequiredService<IInstantQuotationPricingService>(), Owner,
                quoteTicketService: scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>(),
                authoritativePricingService: Pricing);
        }
        public static async Task<Fixture> CreateAsync(bool initialize = true, TimeProvider? clock = null, int partCount = 1)
        {
            var fixture = new Fixture(clock);
            try
            {
                var reference = new InstantQuotationUploadReference("66666666-6666-6666-6666-666666666666");
                var digest = new string('a', 64);
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("controlled-historical-session", reference, digest),
                    new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                        12, 1, true, false, false, 1))!;
                var part = new InstantQuotationPart(fixture.PartId, "controlled.stl", reference, geometry,
                    new InstantQuotationPartConfiguration("M68", "White", 1));
                var parts = partCount == 1 ? new[] { part } : new[] { part, part with
                {
                    PartId = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                    UploadReference = new("99999999-9999-9999-9999-999999999999"),
                    DisplayFileName = "controlled-second.stl",
                } };
                var state = await fixture.Store.CreateAsync(Owner, new InstantQuotationOrderState(parts), default);
                fixture.SessionId = state.SessionId;
                if (initialize) await fixture.Workflow.InitializeAsync(state.SessionId, default);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Pricing.ReleaseAll();
            await Workflow.DisposeAsync();
            await scope.DisposeAsync();
            await factory.DisposeAsync();
        }
    }

    private sealed class Factory(TimeProvider? clock) : TestingWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (clock is not null) builder.ConfigureServices(services => services.AddSingleton(clock));
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (gate) { timers.Add(timer); timer.Change(dueTime, period); }
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            ManualTimer[] pending;
            lock (gate) { now += elapsed; pending = timers.ToArray(); }
            foreach (var timer in pending) timer.Fire();
        }
        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? deadline;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) throw new InvalidOperationException("Only one-shot owned deadlines are tested.");
                lock (owner.gate) deadline = dueTime == Timeout.InfiniteTimeSpan ? null : owner.now + dueTime;
                return true;
            }
            public void Fire()
            {
                lock (owner.gate)
                {
                    if (deadline is not { } due || due > owner.now) return;
                    deadline = null;
                }
                callback(state);
            }
            public void Dispose() { lock (owner.gate) deadline = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
