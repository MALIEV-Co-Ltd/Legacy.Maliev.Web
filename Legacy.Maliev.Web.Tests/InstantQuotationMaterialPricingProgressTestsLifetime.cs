using Microsoft.Extensions.DependencyInjection;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationMaterialPricingProgressTestsLifetime
{
    [Fact]
    public async Task ScopeDisposalFailure_StillReleasesOwnedFactoryResource()
    {
        using var scopeStream = new MemoryStream();
        using var factoryStream = new MemoryStream();
        var failure = new InvalidOperationException("controlled scope disposal failure");
        await using var provider = new ServiceCollection()
            .AddScoped(_ => new ScopeResource(scopeStream, failure)).BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeResource>();
        var receipts = new List<MaterialPricingFixtureCleanupReceipt>();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => MaterialPricingTestCleanup.RunAsync(
            () => Task.CompletedTask, scope.DisposeAsync, factoryStream.DisposeAsync, receipts.Add));

        Assert.Same(failure, actual);
        Assert.Throws<ObjectDisposedException>(() => scopeStream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => factoryStream.ReadByte());
        Assert.Equal("faulted", Assert.Single(receipts).ScopeState);
        Assert.Equal("disposed", Assert.Single(receipts).FactoryState);
    }

    [Fact]
    public async Task PrimaryTestFailure_SurvivesBothOwnedDisposalFailures()
    {
        using var scopeStream = new MemoryStream();
        using var factoryStream = new MemoryStream();
        var primary = new InvalidOperationException("controlled test failure");
        await using var provider = new ServiceCollection()
            .AddScoped(_ => new ScopeResource(scopeStream, new IOException("controlled scope failure")))
            .BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeResource>();
        var receipts = new List<MaterialPricingFixtureCleanupReceipt>();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => MaterialPricingTestCleanup.RunAsync(
            () => Task.FromException(primary), scope.DisposeAsync, DisposeFactoryAsync, receipts.Add));

        Assert.Same(primary, actual);
        Assert.Throws<ObjectDisposedException>(() => scopeStream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => factoryStream.ReadByte());
        var receipt = Assert.Single(receipts);
        Assert.Equal("faulted", receipt.ScopeState);
        Assert.Equal("faulted", receipt.FactoryState);
        Assert.True(receipt.PrimaryFailureRetained);

        ValueTask DisposeFactoryAsync()
        {
            factoryStream.Dispose();
            return ValueTask.FromException(new IOException("controlled factory failure"));
        }
    }

    [Fact]
    public async Task FactoryDisposalFailure_IsReportedAfterScopeResourceRelease()
    {
        using var stream = new MemoryStream();
        await using var provider = new ServiceCollection().AddScoped(_ => new ScopeResource(stream, null))
            .BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeResource>();
        var failure = new IOException("controlled factory failure");
        var receipts = new List<MaterialPricingFixtureCleanupReceipt>();

        var actual = await Assert.ThrowsAsync<IOException>(() => MaterialPricingTestCleanup.RunAsync(
            () => Task.CompletedTask, scope.DisposeAsync, () => ValueTask.FromException(failure), receipts.Add));

        Assert.Same(failure, actual);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        Assert.Equal("disposed", Assert.Single(receipts).ScopeState);
        Assert.Equal("faulted", Assert.Single(receipts).FactoryState);
    }

    [Fact]
    public async Task TimedOutOwnedQuote_IsRecordedAsUnsettledWithoutReplacingPrimaryFailure()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        var owned = AwaitReleaseAsync(release.Task);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        try
        {
            await MaterialPricingTestCleanup.DrainQuoteAsync(owned, caller.Cancel, () => { }, caller.Token,
                new InvalidOperationException("controlled primary failure"), receipts.Add, TimeSpan.Zero);
            Assert.False(owned.IsCompleted);
            var receipt = Assert.Single(receipts);
            Assert.False(receipt.ActualTaskSettled);
            Assert.True(receipt.TimedOut);
            Assert.True(receipt.CallerCancellationRequested);
        }
        finally { release.TrySetResult(); await owned.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task TimedOutOwnedQuote_WithoutPrimaryFailureCannotReportSuccessfulCleanup()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        var owned = AwaitReleaseAsync(release.Task);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => MaterialPricingTestCleanup.DrainQuoteAsync(
                owned, caller.Cancel, () => { }, caller.Token, null, receipts.Add, TimeSpan.Zero));
            Assert.False(owned.IsCompleted);
            Assert.False(Assert.Single(receipts).ActualTaskSettled);
        }
        finally { release.TrySetResult(); await owned.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task FaultedOwnedQuote_IsRecordedAsActualSettlementWithPrimaryFailureRetained()
    {
        using var caller = new CancellationTokenSource();
        var owned = Task.FromException(new IOException("controlled quote failure"));
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();

        await MaterialPricingTestCleanup.DrainQuoteAsync(owned, caller.Cancel, () => { }, caller.Token,
            new InvalidOperationException("controlled primary failure"), receipts.Add, TimeSpan.Zero);

        Assert.True(owned.IsCompleted);
        Assert.True(Assert.Single(receipts).ActualTaskSettled);
        Assert.Equal("Faulted", Assert.Single(receipts).TaskState);
        Assert.Equal(nameof(IOException), Assert.Single(receipts).CleanupFailureType);
    }

    [Fact]
    public async Task CanceledOwnedQuote_IsActualSettlementWithoutAFalseTimeoutReceipt()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var owned = Task.FromCanceled(caller.Token);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();

        await MaterialPricingTestCleanup.DrainQuoteAsync(owned, caller.Cancel, () => { }, caller.Token,
            null, receipts.Add, TimeSpan.Zero);

        Assert.True(owned.IsCompleted);
        Assert.True(Assert.Single(receipts).ActualTaskSettled);
        Assert.False(Assert.Single(receipts).TimedOut);
        Assert.Equal("Canceled", Assert.Single(receipts).TaskState);
    }

    [Fact]
    public async Task CancellationCallbackFailure_StillReleasesAndSettlesActualOwnedTask()
    {
        using var caller = new CancellationTokenSource();
        using var registration = caller.Token.Register(() => throw new IOException("controlled cancellation failure"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owned = AwaitReleaseAsync(release.Task);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        try
        {
            await Assert.ThrowsAsync<AggregateException>(() => MaterialPricingTestCleanup.DrainQuoteAsync(
                owned, caller.Cancel, () => release.TrySetResult(), caller.Token, null, receipts.Add, TimeSpan.FromSeconds(10)));
            Assert.True(owned.IsCompletedSuccessfully);
            Assert.True(Assert.Single(receipts).ActualTaskSettled);
        }
        finally { release.TrySetResult(); await owned.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public void FirstGateReleaseFailure_DoesNotSkipRemainingOwnedGate()
    {
        using var gate = new ManualResetEventSlim();
        var failure = new IOException("controlled release failure");
        var actual = Assert.Throws<IOException>(() => MaterialPricingTestCleanup.ReleaseAll(
            () => throw failure, gate.Set));
        Assert.Same(failure, actual);
        Assert.True(gate.IsSet);
    }

    [Fact]
    public async Task CompletionAtZeroWaitBoundary_IsActualSettlement()
    {
        using var caller = new CancellationTokenSource();
        var owned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        await MaterialPricingTestCleanup.DrainQuoteAsync(owned.Task, caller.Cancel,
            () => owned.TrySetResult(), caller.Token, null, receipts.Add, TimeSpan.Zero);
        Assert.True(Assert.Single(receipts).ActualTaskSettled);
        Assert.False(Assert.Single(receipts).TimedOut);
    }

    [Fact]
    public async Task DiagnosticSinkFailure_CannotReplacePrimaryFailureOrSkipDisposals()
    {
        using var scope = new MemoryStream();
        using var factory = new MemoryStream();
        var primary = new InvalidOperationException("controlled primary failure");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => MaterialPricingTestCleanup.RunAsync(
            () => Task.FromException(primary), scope.DisposeAsync, factory.DisposeAsync,
            _ => throw new IOException("controlled sink failure")));
        Assert.Same(primary, actual);
        Assert.Throws<ObjectDisposedException>(() => scope.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => factory.ReadByte());
    }

    [Fact]
    public async Task QuoteDiagnosticSinkFailure_IsReportedWhenNoPrimaryFailureExists()
    {
        using var caller = new CancellationTokenSource();
        var failure = new IOException("controlled sink failure");
        var actual = await Assert.ThrowsAsync<IOException>(() => MaterialPricingTestCleanup.DrainQuoteAsync(
            Task.CompletedTask, caller.Cancel, () => { }, caller.Token, null, _ => throw failure, TimeSpan.Zero));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task QuoteDiagnosticSinkFailure_WithPrimaryStillReleasesAndSettlesOwnedTask()
    {
        using var caller = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owned = AwaitReleaseAsync(release.Task);
        try
        {
            await MaterialPricingTestCleanup.DrainQuoteAsync(owned, caller.Cancel, () => release.TrySetResult(),
                caller.Token, new InvalidOperationException("controlled primary failure"),
                _ => throw new IOException("controlled sink failure"), TimeSpan.FromSeconds(10));
            Assert.True(owned.IsCompletedSuccessfully);
        }
        finally { release.TrySetResult(); await owned.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedDiagnosticContinuation_DoesNotReplaceActualQuoteTask(bool display)
    {
        var actual = new TaskCompletionSource<InstantQuotationOrderQuote?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new QueuedScheduler();
        var diagnostics = new MaterialPricingTestDiagnostics(new BoundaryService(actual.Task), scheduler);
        try
        {
            Task<InstantQuotationOrderQuote?> returned = display
                ? diagnostics.QuoteAsync(UnitSession(), null, false, (_, _) => ValueTask.CompletedTask, default)
                : diagnostics.QuoteAsync(UnitSession(), null, false, default);
            Assert.Same(actual.Task, returned);
            Assert.False(returned.IsCompleted);
            actual.SetResult(null);
            await returned.WaitAsync(TimeSpan.FromSeconds(10));
            await scheduler.Queued.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(returned.IsCompletedSuccessfully);
            Assert.False(Assert.Single(diagnostics.ObservationTasks).IsCompleted);
            scheduler.RunAll();
            Assert.True(Assert.Single(diagnostics.ObservationTasks).IsCompletedSuccessfully);
        }
        finally { actual.TrySetResult(null); scheduler.RunAll(); diagnostics.DisposeSubscriptions(); }
    }

    [Fact]
    public async Task SynchronouslyBlockingCallback_IsInvokedWithoutDiagnosticOffload()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = Task.FromResult<InstantQuotationOrderQuote?>(null);
        var service = new BoundaryService(completed, observer =>
        {
            observer(UnitFrame(), default).GetAwaiter().GetResult();
            return completed;
        });
        var diagnostics = new MaterialPricingTestDiagnostics(service);
        var invocation = Task.Run(() => diagnostics.QuoteAsync(UnitSession(), null, false, (_, _) =>
        {
            entered.SetResult(); release.Wait(); return ValueTask.CompletedTask;
        }, default));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(invocation.IsCompleted);
            release.Set();
            await invocation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(completed, Assert.Single(diagnostics.OwnedTasks));
        }
        finally
        {
            release.Set(); await invocation.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(diagnostics.ObservationTasks).WaitAsync(TimeSpan.FromSeconds(10));
            diagnostics.DisposeSubscriptions();
        }
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("fault")]
    [InlineData("cancel")]
    public async Task CallbackFailure_PreservesRealQuoteOutcomeAndExceptionIdentity(string category)
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var failure = new IOException("controlled callback failure");
        var service = new BoundaryService(Task.FromResult<InstantQuotationOrderQuote?>(null), InvokeAsync);
        var diagnostics = new MaterialPricingTestDiagnostics(service);
        try
        {
            var returned = diagnostics.QuoteAsync(UnitSession(), null, false, (_, _) => category switch
            {
                "throw" => throw failure,
                "fault" => ValueTask.FromException(failure),
                _ => ValueTask.FromCanceled(caller.Token),
            }, default);
            Assert.Same(service.LastDisplayTask, returned);
            var actual = await Record.ExceptionAsync(() => returned);
            var original = await Record.ExceptionAsync(() => service.LastDisplayTask!);
            Assert.Same(original, actual);
            if (category == "cancel") Assert.True(returned.IsCanceled);
            else Assert.Same(failure, actual);
        }
        finally
        {
            await Task.WhenAll(diagnostics.ObservationTasks).WaitAsync(TimeSpan.FromSeconds(10));
            diagnostics.DisposeSubscriptions();
        }
    }

    [Fact]
    public void SynchronousQuoteBoundaryThrow_PreservesOriginalExceptionIdentity()
    {
        var failure = new IOException("controlled synchronous failure");
        var service = new BoundaryService(Task.FromResult<InstantQuotationOrderQuote?>(null), observer =>
        {
            observer(UnitFrame(), default).GetAwaiter().GetResult();
            return Task.FromResult<InstantQuotationOrderQuote?>(null);
        });
        var diagnostics = new MaterialPricingTestDiagnostics(service);
        try
        {
            var actual = Assert.Throws<IOException>(() =>
            {
                _ = diagnostics.QuoteAsync(UnitSession(), null, false, (_, _) => throw failure, default);
            });
            Assert.Same(failure, actual);
            Assert.Empty(diagnostics.OwnedTasks);
        }
        finally { diagnostics.DisposeSubscriptions(); }
    }

    [Fact]
    public async Task SuccessfulAsynchronousCallback_PreservesActualQuoteCompletion()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new BoundaryService(Task.FromResult<InstantQuotationOrderQuote?>(null), InvokeAsync);
        var diagnostics = new MaterialPricingTestDiagnostics(service);
        var returned = diagnostics.QuoteAsync(UnitSession(), null, false, (_, _) => new ValueTask(release.Task), default);
        try
        {
            Assert.Same(service.LastDisplayTask, returned);
            Assert.Same(release.Task, Assert.Single(diagnostics.CallbackTasks));
            Assert.False(returned.IsCompleted);
            release.SetResult();
            Assert.Null(await returned.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            release.TrySetResult(); await returned.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(diagnostics.ObservationTasks).WaitAsync(TimeSpan.FromSeconds(10));
            diagnostics.DisposeSubscriptions();
        }
    }

    [Theory]
    [InlineData("success", "RanToCompletion", null)]
    [InlineData("fault", "Faulted", "IOException")]
    [InlineData("cancel", "Canceled", "OperationCanceledException")]
    public async Task ActualTerminalAfterGenuineWaitTimeout_RecordsBothIndependentOutcomes(
        string outcome, string state, string? failureType)
    {
        using var caller = new CancellationTokenSource();
        var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        bool timeoutBoundaryReached = false;
        await Assert.ThrowsAsync<TimeoutException>(() => MaterialPricingTestCleanup.DrainQuoteAsync(actual.Task,
            () => { }, () => { }, caller.Token, null, receipts.Add, TimeSpan.Zero, afterWaitFailure: () =>
            {
                Assert.False(actual.Task.IsCompleted); // Actual WaitAsync has already timed out.
                timeoutBoundaryReached = true;
                if (outcome == "success") actual.SetResult();
                else if (outcome == "fault") actual.SetException(new IOException("controlled late terminal failure"));
                else actual.SetCanceled();
            }));
        Assert.True(timeoutBoundaryReached);
        var receipt = Assert.Single(receipts);
        Assert.True(receipt.TimedOut);
        Assert.True(receipt.ActualTaskSettled);
        Assert.Equal(state, receipt.TaskState);
        Assert.Equal(nameof(TimeoutException), receipt.WaitFailureType);
        Assert.Equal(failureType, receipt.ActualFailureType);
    }

    [Fact]
    public async Task UnexpectedUnconsumedCompletedQuoteFault_CannotPassFixtureCleanup()
    {
        var failure = new IOException("controlled unconsumed task failure");
        var owned = Task.FromException(failure);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        var actual = await Assert.ThrowsAsync<IOException>(() => MaterialPricingTestCleanup.DrainQuoteAsync(
            owned, () => { }, () => { }, default, null, receipts.Add, TimeSpan.Zero));
        Assert.Same(failure, actual);
        Assert.True(Assert.Single(receipts).ActualTaskSettled);
        Assert.False(Assert.Single(receipts).ExpectedTerminalFailure);
    }

    [Fact]
    public async Task ExplicitlyAssertedQuoteFault_IsAccountedWithoutFailingAgain()
    {
        var failure = new IOException("controlled intentional task failure");
        var owned = Task.FromException(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => owned));
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        await MaterialPricingTestCleanup.DrainQuoteAsync(owned, () => { }, () => { }, default,
            null, receipts.Add, TimeSpan.Zero, expectedTerminalFailure: true);
        Assert.True(Assert.Single(receipts).ExpectedTerminalFailure);
        Assert.Equal(nameof(IOException), Assert.Single(receipts).ActualFailureType);
    }

    [Fact]
    public async Task ExpectedFailureAcknowledgment_CannotWaiveUnsettledWaitTimeout()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owned = AwaitReleaseAsync(release.Task);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => MaterialPricingTestCleanup.DrainQuoteAsync(owned,
                () => { }, () => { }, default, null, receipts.Add, TimeSpan.Zero, expectedTerminalFailure: true));
            Assert.False(Assert.Single(receipts).ActualTaskSettled);
            Assert.True(Assert.Single(receipts).TimedOut);
        }
        finally { release.TrySetResult(); await owned.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task ActualProducerTimeoutException_IsNotMisreportedAsCleanupWaitTimeout()
    {
        var failure = new TimeoutException("controlled producer failure");
        var owned = Task.FromException(failure);
        var receipts = new List<MaterialPricingQuoteCleanupReceipt>();
        var actual = await Assert.ThrowsAsync<TimeoutException>(() => MaterialPricingTestCleanup.DrainQuoteAsync(
            owned, () => { }, () => { }, default, null, receipts.Add, TimeSpan.Zero));
        Assert.Same(failure, actual);
        Assert.True(Assert.Single(receipts).ActualTaskSettled);
        Assert.False(Assert.Single(receipts).TimedOut);
        Assert.Equal(nameof(TimeoutException), Assert.Single(receipts).ActualFailureType);
    }

    private static InstantQuotationSessionState UnitSession() => new("unit", "unit", new([]),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static InstantQuotationMaterialPricingProgress UnitFrame() => new(Guid.Empty, "unit",
        InstantQuotationMaterialPricingStatus.Pending, null);
    private static async Task<InstantQuotationOrderQuote?> InvokeAsync(
        Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer)
    {
        await observer(UnitFrame(), default);
        return null;
    }
    // Boundary task controls exercise forwarding only; they do not supply physical/financial evidence.
    private sealed class BoundaryService(Task<InstantQuotationOrderQuote?> plain,
        Func<Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask>, Task<InstantQuotationOrderQuote?>>? display = null)
        : IInstantQuotationAuthoritativePricingService
    {
        public Task<InstantQuotationOrderQuote?>? LastDisplayTask { get; private set; }
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? ownerIdentity,
            bool includeComparisons, CancellationToken cancellationToken) => plain;
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? ownerIdentity,
            bool includeComparisons, Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken cancellationToken)
        {
            var actual = display?.Invoke(observer) ?? plain;
            LastDisplayTask = actual;
            return actual;
        }
    }
    private sealed class QueuedScheduler : TaskScheduler
    {
        private readonly Queue<Task> tasks = new();
        public TaskCompletionSource Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override IEnumerable<Task> GetScheduledTasks() { lock (tasks) return tasks.ToArray(); }
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        protected override void QueueTask(Task task) { lock (tasks) tasks.Enqueue(task); Queued.TrySetResult(); }
        public void RunAll()
        {
            while (true)
            {
                Task task;
                lock (tasks) { if (tasks.Count == 0) return; task = tasks.Dequeue(); }
                Assert.True(TryExecuteTask(task));
            }
        }
    }

    private static async Task AwaitReleaseAsync(Task release) => await release;

    private sealed class ScopeResource(MemoryStream stream, Exception? failure) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
        }
    }
}
