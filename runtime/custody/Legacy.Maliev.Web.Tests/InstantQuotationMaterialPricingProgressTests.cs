using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationMaterialPricingProgressTests
{
    private const string Owner = "progress-fixture-owner";
    private static readonly Guid PartId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly byte[] Bytes = Box();
    private static readonly string Digest = Convert.ToHexStringLower(SHA256.HashData(Bytes));

    [Theory]
    [InlineData(InstantQuotationMaterialPricingStatus.Pending)]
    [InlineData(InstantQuotationMaterialPricingStatus.Completed)]
    public async Task SelectedStatus_IsPublishedBeforeComparisonBytesComplete(InstantQuotationMaterialPricingStatus status)
    {
        await using var fixture = new Fixture(gateRead: 2);
        var session = await fixture.CreateAsync();
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        Task<InstantQuotationOrderQuote?> run = fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        await fixture.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(run.IsCompleted);
            var frame = Assert.Single(frames, frame => frame.MaterialKey == "ABS" && frame.Status == status);
            Assert.Equal(PartId, frame.PartId);
            if (status == InstantQuotationMaterialPricingStatus.Pending) Assert.Null(frame.UnitPrice);
            else Assert.Equal(210, frame.UnitPrice);
        }
        finally { fixture.Reader.Release.TrySetResult(); await run; }
    }

    [Fact]
    public async Task AwaitedObserver_PreventsNextComparisonReadUntilItReturns()
    {
        await using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<InstantQuotationOrderQuote?> run = fixture.Service.QuoteAsync(session, Owner, true,
            async (frame, token) =>
            {
                if (frame.MaterialKey == "ABS" && frame.Status == InstantQuotationMaterialPricingStatus.Completed)
                { entered.TrySetResult(); await release.Task.WaitAsync(token); }
            }, default);
        Task first = await Task.WhenAny(entered.Task, run);
        try
        {
            Assert.Same(entered.Task, first);
            Assert.Equal(1, fixture.Reader.ReadCount);
            Assert.False(run.IsCompleted);
        }
        finally { release.TrySetResult(); await run; }
    }

    [Fact]
    public async Task FailedComparison_HasUnavailableNullAmount_AndPreservesSelectedFinalPrice()
    {
        await using var fixture = new Fixture(failComparisons: true);
        var session = await fixture.CreateAsync();
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var quote = await fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        Assert.NotNull(quote);
        Assert.Equal(0.512, Assert.Single(quote.Parts).BoundingCm3PerUnit, 3);
        var unavailable = Assert.Single(frames, frame => frame.MaterialKey == "PLA"
            && frame.Status == InstantQuotationMaterialPricingStatus.Unavailable);
        Assert.Null(unavailable.UnitPrice);
        Assert.Equal(PartId, unavailable.PartId);
        Assert.True(fixture.Reader.ReadCount > 1);
    }

    [Fact]
    public async Task ComparisonBudget_ClosesPendingDisplayWithoutRevokingSelectedQuote()
    {
        await using var fixture = new Fixture(gateRead: 2);
        var session = await fixture.CreateAsync();
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var run = fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        await fixture.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var quote = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(quote);
        Assert.Equal(2, fixture.Reader.ReadCount);
        var attempted = Assert.Single(frames, frame => frame.MaterialKey != "ABS"
            && frame.Status == InstantQuotationMaterialPricingStatus.Pending);
        Assert.Equal(PrintProcess.Fdm, PricingCatalog.ResolveMaterial(attempted.MaterialKey)!.Process);
        var terminal = Assert.Single(frames, frame => frame.MaterialKey == attempted.MaterialKey
            && frame.Status == InstantQuotationMaterialPricingStatus.Unavailable);
        Assert.Null(terminal.UnitPrice);
        Assert.DoesNotContain(frames, frame => frame.MaterialKey != "ABS"
            && frame.Status == InstantQuotationMaterialPricingStatus.Completed);
        Assert.Equal(210, Assert.Single(quote.Parts).UnitPrice);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAfterReachedRead_WithoutCompletedComparison()
    {
        await using var fixture = new Fixture(gateRead: 2);
        var session = await fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var run = fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, cancellation.Token);
        await fixture.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(2, fixture.Reader.ReadCount);
        Assert.DoesNotContain(frames, frame => frame.MaterialKey == "PLA"
            && frame.Status == InstantQuotationMaterialPricingStatus.Completed);
    }

    [Theory]
    [InlineData(InstantQuotationMaterialPricingStatus.Pending)]
    [InlineData(InstantQuotationMaterialPricingStatus.Completed)]
    public async Task ComparisonCallbackBudget_ClosesAttemptedCardAndPreservesSelectedQuote(InstantQuotationMaterialPricingStatus gatedStatus)
    {
        var clock = new ManualClock();
        await using var fixture = new Fixture(clock: clock);
        var session = await fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new System.Collections.Concurrent.ConcurrentQueue<InstantQuotationMaterialPricingProgress>();
        using var caller = new CancellationTokenSource();
        var run = fixture.Service.QuoteAsync(session, Owner, true, (frame, _) =>
        {
            frames.Enqueue(frame);
            if (frame.MaterialKey != "ABS" && frame.Status == gatedStatus)
            {
                entered.TrySetResult();
                return new ValueTask(release.Task); // A late observer must never resume the producer.
            }
            return ValueTask.CompletedTask;
        }, caller.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var readsAtDeadline = fixture.Reader.ReadCount;
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.False(run.IsCompleted);
            release.TrySetResult();
            var quote = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(quote);
            Assert.False(caller.IsCancellationRequested);
            Assert.Equal(210, Assert.Single(quote.Parts).UnitPrice);
            Assert.Equal(readsAtDeadline, fixture.Reader.ReadCount);
            var attempted = Assert.Single(frames, frame => frame.MaterialKey != "ABS"
                && frame.Status == InstantQuotationMaterialPricingStatus.Pending);
            Assert.All(frames.Where(frame => frame.MaterialKey != "ABS"),
                frame => Assert.Equal(attempted.MaterialKey, frame.MaterialKey));
            if (gatedStatus == InstantQuotationMaterialPricingStatus.Pending)
                Assert.Contains(frames, frame => frame.MaterialKey == attempted.MaterialKey
                    && frame.Status == InstantQuotationMaterialPricingStatus.Unavailable && frame.UnitPrice is null);
            else
                Assert.DoesNotContain(frames, frame => frame.MaterialKey == attempted.MaterialKey
                    && frame.Status == InstantQuotationMaterialPricingStatus.Unavailable);
            var display = new Legacy.Maliev.Web.Components.Pages.InstantQuotation.InstantQuotationMaterialPriceDisplaySnapshot(1);
            foreach (var frame in frames) display = display.Apply(frame);
            Assert.DoesNotContain(display.Entries, frame => frame.Status == InstantQuotationMaterialPricingStatus.Pending);
            release.TrySetResult();
            Assert.Equal(readsAtDeadline, fixture.Reader.ReadCount);
        }
        finally
        {
            caller.Cancel();
            release.TrySetResult();
            // Bounded cleanup cannot mask the primary regression assertion or timeout.
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
        }
    }

    [Theory]
    [InlineData(InstantQuotationMaterialPricingStatus.Pending)]
    [InlineData(InstantQuotationMaterialPricingStatus.Completed)]
    public async Task ComparisonCallbackAbsoluteDeadline_RejectsQuoteAndLateCompletionCannotResume(InstantQuotationMaterialPricingStatus gatedStatus)
    {
        var clock = new ManualClock();
        await using var fixture = new Fixture(clock: clock);
        var session = await fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        CancellationToken callbackToken = default;
        var run = fixture.Service.QuoteAsync(session, Owner, true, (frame, token) =>
        {
            if (frame.MaterialKey != "ABS" && frame.Status == gatedStatus)
            {
                callbackToken = token;
                entered.TrySetResult();
                return new ValueTask(release.Task);
            }
            return ValueTask.CompletedTask;
        }, caller.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var readsAtDeadline = fixture.Reader.ReadCount;
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.False(run.IsCompleted);
            Assert.False(callbackToken.IsCancellationRequested);
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(callbackToken.IsCancellationRequested);
            Assert.False(caller.IsCancellationRequested);
            release.TrySetResult();
            Assert.Equal(readsAtDeadline, fixture.Reader.ReadCount);
        }
        finally
        {
            caller.Cancel();
            release.TrySetResult();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
        }
    }

    [Fact]
    public async Task ComparisonCallback_WholeQuoteCanCompleteAfterThirtySecondsWithinAbsoluteBudget()
    {
        var clock = new ManualClock();
        await using var fixture = new Fixture(clock: clock);
        var session = await fixture.CreateAsync();
        var selectedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectedRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var comparisonEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var comparisonRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        CancellationToken comparisonCallbackToken = default;
        int heldComparison = 0;
        var run = fixture.Service.QuoteAsync(session, Owner, true, (frame, token) =>
        {
            if (frame.MaterialKey == "ABS" && frame.Status == InstantQuotationMaterialPricingStatus.Completed)
            {
                selectedEntered.TrySetResult();
                return new ValueTask(selectedRelease.Task);
            }
            if (frame.MaterialKey != "ABS" && frame.Status == InstantQuotationMaterialPricingStatus.Pending
                && Interlocked.Exchange(ref heldComparison, 1) == 0)
            {
                comparisonCallbackToken = token;
                comparisonEntered.TrySetResult();
                return new ValueTask(comparisonRelease.Task);
            }
            return ValueTask.CompletedTask;
        }, caller.Token);
        try
        {
            await selectedEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(29));
            selectedRelease.TrySetResult();
            await comparisonEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.False(run.IsCompleted);
            Assert.False(comparisonCallbackToken.IsCancellationRequested);
            Assert.False(caller.IsCancellationRequested);
            comparisonRelease.TrySetResult();
            Assert.NotNull(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            caller.Cancel();
            selectedRelease.TrySetResult();
            comparisonRelease.TrySetResult();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
        }
    }

    [Fact]
    public async Task NoncooperativeObserver_CallerAbortStopsWaitAndLateCompletionCannotContinueAnalysis()
    {
        await using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = fixture.Service.QuoteAsync(session, Owner, true, (_, _) =>
        {
            entered.TrySetResult();
            return new ValueTask(release.Task); // Deliberately ignores the supplied token.
        }, cancellation.Token);
        var first = await Task.WhenAny(entered.Task, run);
        try
        {
            Assert.Same(entered.Task, first);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            int readsAfterAbort = fixture.Reader.ReadCount;
            release.TrySetResult();
            Assert.Equal(readsAfterAbort, fixture.Reader.ReadCount);
        }
        finally { cancellation.Cancel(); release.TrySetResult(); try { await run; } catch (OperationCanceledException) { } }
    }

    [Theory]
    [InlineData("io")]
    [InlineData("argument")]
    [InlineData("invalid-operation")]
    public async Task ObserverFailure_CannotReturnAUsableFinalQuote(string category)
    {
        await using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        Exception failure = category switch
        {
            "argument" => new ArgumentException("controlled observer failure"),
            "invalid-operation" => new InvalidOperationException("controlled observer failure"),
            _ => new IOException("controlled observer failure"),
        };
        var actual = await Record.ExceptionAsync(() => fixture.Service.QuoteAsync(
            session, Owner, false, (_, _) => ValueTask.FromException(failure), default));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task SelectedFailure_ReportsUnavailableAndNeverReturnsUsableQuote()
    {
        await using var fixture = new Fixture(failSelected: true);
        var session = await fixture.CreateAsync();
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var quote = await fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        Assert.Null(quote);
        Assert.Equal(1, fixture.Reader.ReadCount);
        Assert.Equal(new[] { InstantQuotationMaterialPricingStatus.Pending, InstantQuotationMaterialPricingStatus.Unavailable },
            frames.Select(frame => frame.Status));
        Assert.All(frames, frame => { Assert.Equal("ABS", frame.MaterialKey); Assert.Null(frame.UnitPrice); });
    }

    [Fact]
    public async Task OwnedSelectedDeadline_BoundsNoncooperativeCompletedCallback_AndLateFaultCannotResume()
    {
        var clock = new ManualClock();
        await using var fixture = new Fixture(clock: clock);
        var session = await fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var run = fixture.Service.QuoteAsync(session, Owner, true, (frame, token) =>
        {
            if (frame.Status != InstantQuotationMaterialPricingStatus.Completed) return ValueTask.CompletedTask;
            observed = token;
            entered.TrySetResult();
            return new ValueTask(late.Task);
        }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, fixture.Reader.ReadCount);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(observed.IsCancellationRequested);
        late.TrySetException(new InvalidOperationException("controlled late observer fault"));
        // Drain the deliberately late callback itself. Producer already returned and
        // has no continuation capable of resuming comparison analysis.
        await Assert.ThrowsAsync<InvalidOperationException>(() => late.Task);
        Assert.Equal(1, fixture.Reader.ReadCount);
    }

    [Fact]
    public async Task SynchronouslyBlockingObserver_ProducerDeadlineDoesNotWaitForDelegateReturn()
    {
        var clock = new ManualClock();
        await using var fixture = new Fixture(clock: clock);
        var session = await fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var run = Task.Run(() => fixture.Service.QuoteAsync(session, Owner, true, (frame, _) =>
        {
            if (frame.Status != InstantQuotationMaterialPricingStatus.Completed) return ValueTask.CompletedTask;
            entered.TrySetResult();
            try { release.Wait(); return ValueTask.CompletedTask; }
            finally { exited.TrySetResult(); }
        }, default));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, fixture.Reader.ReadCount);
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(exited.Task.IsCompleted);
            Assert.Equal(1, fixture.Reader.ReadCount);
        }
        finally
        {
            release.Set();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task AbsoluteDeadline_BoundsTerminalUnavailableCallbackAfterComparisonBudget()
    {
        var clock = new ManualClock();
        await using var fixture = new Fixture(gateRead: 2, clock: clock);
        var session = await fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = fixture.Service.QuoteAsync(session, Owner, true, (frame, _) =>
        {
            if (frame.Status != InstantQuotationMaterialPricingStatus.Unavailable) return ValueTask.CompletedTask;
            entered.TrySetResult(); return new ValueTask(late.Task);
        }, default);
        await fixture.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(3));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(run.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        late.TrySetResult();
        Assert.Equal(2, fixture.Reader.ReadCount);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("collision")]
    public async Task UnsupportedMaterialOrPartCollision_ReturnsNoQuoteAndNoByteReads(string fault)
    {
        await using var fixture = new Fixture();
        var original = await fixture.CreateAsync();
        var part = original.Parts.Single();
        var state = fault == "collision"
            ? new InstantQuotationOrderState([part, part])
            : new InstantQuotationOrderState([part with { Configuration = part.Configuration with { MaterialKey = "unsupported" } }]);
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var quote = await fixture.Service.QuoteAsync(original with { RequestState = state }, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        Assert.Null(quote); Assert.Empty(frames); Assert.Equal(0, fixture.Reader.ReadCount);
    }

    [Fact]
    public async Task MultiPart_SelectedFramesCompleteBeforeComparisons_AndEachAmountMatchesFinalCard()
    {
        await using var fixture = new Fixture();
        var session = await fixture.CreateAsync(partCount: 2);
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var quote = await fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        Assert.NotNull(quote);
        Assert.Equal(2, quote.Parts.Count);
        var selected = frames.Where(frame => frame.MaterialKey == "ABS" && frame.Status == InstantQuotationMaterialPricingStatus.Completed).ToArray();
        Assert.Equal(2, selected.Length);
        Assert.All(selected, frame => Assert.Equal(210, frame.UnitPrice));
        Assert.True(frames.FindIndex(frame => frame.MaterialKey != "ABS") > frames.FindLastIndex(frame => frame.MaterialKey == "ABS"));
        foreach (var frame in frames.Where(frame => frame.Status == InstantQuotationMaterialPricingStatus.Completed))
        {
            Assert.True(frame.UnitPrice is { } amount && double.IsFinite(amount) && amount > 0);
            var line = Assert.Single(quote.Parts, line => line.PartId == frame.PartId);
            var card = Assert.Single(line.MaterialPrices, card => card.MaterialKey == frame.MaterialKey);
            Assert.Equal(card.UnitPrice, frame.UnitPrice);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(frame));
            Assert.Equal(new[] { "MaterialKey", "PartId", "Status", "UnitPrice" },
                json.RootElement.EnumerateObject().Select(property => property.Name).Order());
        }
        Assert.All(frames.Where(frame => frame.Status != InstantQuotationMaterialPricingStatus.Completed),
            frame => Assert.Null(frame.UnitPrice));
    }

    [Fact]
    public async Task CaseInsensitiveSelectedMaterial_IsNotAnalyzedOrPublishedAgainAsComparison()
    {
        await using var fixture = new Fixture();
        var session = await fixture.CreateAsync(materialKey: "abs");
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var quote = await fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        Assert.NotNull(quote);
        Assert.Equal(210, Assert.Single(quote.Parts).UnitPrice);
        Assert.Equal(new[] { InstantQuotationMaterialPricingStatus.Pending, InstantQuotationMaterialPricingStatus.Completed },
            frames.Where(frame => frame.MaterialKey == "ABS").Select(frame => frame.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResinSelected_UsesSameExistingKernelAndComparisonLiteral(bool includeComparisons)
    {
        await using var fixture = new Fixture();
        var session = await fixture.CreateAsync(materialKey: "M68");
        var expected = await fixture.Service.QuoteAsync(session, Owner, includeComparisons, default);
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var quote = await fixture.Service.QuoteAsync(session, Owner, includeComparisons,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        Assert.NotNull(quote);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(quote));
        if (!includeComparisons) Assert.Equal(0, fixture.Reader.ReadCount);
        else Assert.True(fixture.Reader.ReadCount > 0);
        Assert.Null(Assert.Single(quote.Parts).PhysicalReceipt);
        var completed = Assert.Single(frames, frame => frame.MaterialKey == "M68"
            && frame.Status == InstantQuotationMaterialPricingStatus.Completed);
        Assert.Equal("M68", completed.MaterialKey);
        Assert.Equal(Assert.Single(quote.Parts).UnitPrice, completed.UnitPrice);
        if (includeComparisons)
        {
            // Same literal 8mm ABS physics/commercial anchor, now as comparison rather
            // than selected. Originating selected resin cannot change this unit price.
            var comparison = Assert.Single(frames, frame => frame.MaterialKey == "ABS"
                && frame.Status == InstantQuotationMaterialPricingStatus.Completed);
            Assert.Equal(210, comparison.UnitPrice);
            Assert.Equal(210, Assert.Single(quote.Parts.Single().MaterialPrices,
                card => card.MaterialKey == "ABS").UnitPrice);
            Assert.True(frames.IndexOf(completed) < frames.IndexOf(comparison));
        }
    }

    [Fact]
    public async Task DisplayOverload_DoesNotChangeFinalKernelMoneyOrProtectedSession()
    {
        await using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var expected = await fixture.Service.QuoteAsync(session, Owner, true, default);
        var frames = new List<InstantQuotationMaterialPricingProgress>();
        var actual = await fixture.Service.QuoteAsync(session, Owner, true,
            (frame, _) => { frames.Add(frame); return ValueTask.CompletedTask; }, default);
        Assert.NotNull(actual);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        var stored = await fixture.Store.GetAsync(session.SessionId, Owner, default);
        Assert.Null(stored!.QuoteAuthorization);
        Assert.Equal(session.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(0.512, Assert.Single(actual.Parts).BoundingCm3PerUnit, 3);
        Assert.Equal(1, Assert.Single(actual.Parts).Quantity);
        // Independent commercial anchor: raw base rounds to 100, process floor 300,
        // reserve 30, setup 39.0625, packaging 20, gross 430, unrounded VAT
        // 28.0766752577, explicit minimum surcharge 200; ceil10(201.9233) = 210.
        Assert.InRange(Assert.Single(actual.Parts).DirectCostPerUnit, 47.5000001, 50);
        Assert.Equal(210, Assert.Single(actual.Parts).UnitPrice);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Factory factory;
        private readonly AsyncServiceScope scope;
        public Reader Reader { get; }
        public IInstantQuotationAuthoritativePricingService Service { get; }
        public IInstantQuotationSessionStore Store { get; }
        public Fixture(int gateRead = 0, bool failComparisons = false, bool failSelected = false, TimeProvider? clock = null)
        {
            Reader = new Reader(gateRead, failComparisons, failSelected);
            factory = new Factory(Reader, clock);
            scope = factory.Services.CreateAsyncScope();
            Service = scope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>();
            Store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
        }
        public async Task<InstantQuotationSessionState> CreateAsync(string materialKey = "ABS", int partCount = 1)
        {
            var reference = new InstantQuotationUploadReference(FileId.ToString("D"));
            var claim = new InstantQuotationGeometryClaim(1, Digest, 8, 8, 8, 512, 384,
                Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                12, 1, true, false, false, 1);
            var part = new InstantQuotationPart(PartId, "part.stl", reference,
                AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("controlled-admission", reference, Digest), claim)!,
                new InstantQuotationPartConfiguration(materialKey, "Black", 1),
                new InstantQuotationPhysicalAnalysisUpload(FileId, "part.stl", "model/stl", Bytes.Length, Digest, "clean"));
            var parts = Enumerable.Range(0, partCount).Select(index => index == 0 ? part : part with { PartId = Guid.Parse("33333333-3333-3333-3333-333333333333") }).ToArray();
            Reader.PartIds = parts.Select(part => part.PartId).ToHashSet();
            var session = await Store.CreateAsync(Owner, new InstantQuotationOrderState(parts), default);
            Reader.SessionId = session.SessionId;
            return session;
        }
        public async ValueTask DisposeAsync() { Reader.Release.TrySetResult(); await scope.DisposeAsync(); await factory.DisposeAsync(); }
    }
    private sealed class Factory(Reader reader, TimeProvider? clock) : TestingWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                services.AddSingleton<IInstantQuotationPhysicalAnalysisInputReader>(reader);
                if (clock is not null) services.AddSingleton(clock);
            });
        }
    }
    private sealed class Reader(int gateRead, bool failComparisons, bool failSelected) : IInstantQuotationPhysicalAnalysisInputReader
    {
        public string? SessionId { get; set; }
        public int ReadCount { get; private set; }
        public HashSet<Guid> PartIds { get; set; } = [PartId];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            Assert.Equal(SessionId, sessionId); Assert.Equal(Owner, ownerIdentity); Assert.Contains(partId, PartIds);
            ReadCount++;
            if (ReadCount == gateRead) { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return failSelected || (failComparisons && ReadCount > 1)
                ? InstantQuotationPhysicalAnalysisInputResult.Unavailable(InstantQuotationPhysicalAnalysisInputFailure.ContentUnavailable)
                : new(Bytes, InstantQuotationPhysicalAnalysisInputFailure.None, FileId, "part.stl", Digest);
        }
    }
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<Timer> timers = [];
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            now += elapsed;
            foreach (var timer in timers.ToArray()) timer.Fire();
        }
        private sealed class Timer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? deadline;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) throw new InvalidOperationException("Only owned one-shot deadlines are tested.");
                deadline = dueTime == Timeout.InfiniteTimeSpan ? null : owner.now + dueTime; return true;
            }
            public void Fire() { if (deadline is { } due && due <= owner.now) { deadline = null; callback(state); } }
            public void Dispose() => deadline = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private static byte[] Box()
    {
        Vector3[] points = [new(0, 0, 0), new(8, 0, 0), new(8, 8, 0), new(0, 8, 0), new(0, 0, 8), new(8, 0, 8), new(8, 8, 8), new(0, 8, 8)];
        int[][] faces = [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5], [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7]];
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]); writer.Write((uint)faces.Length);
        foreach (var face in faces) { writer.Write(new byte[12]); foreach (int vertex in face) { writer.Write(points[vertex].X); writer.Write(points[vertex].Y); writer.Write(points[vertex].Z); } writer.Write((ushort)0); }
        return stream.ToArray();
    }
}
