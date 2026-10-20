using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class RepricingActivityTests
{
    [Fact]
    public async Task MaterialPreviewPending_KeepsReviewActivityOpenAfterPricingCompletes()
    {
        var activity = new RepricingActivity();
        var previewStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previewCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transitions = new List<bool>();
        var pricingCalls = 0;
        var operation = activity.RunAsync(
            () =>
            {
                pricingCalls++;
                return Task.CompletedTask;
            },
            () =>
            {
                transitions.Add(activity.IsActive);
                return Task.CompletedTask;
            },
            async () =>
            {
                previewStarted.SetResult();
                await previewCompletion.Task.WaitAsync(deadline.Token);
            });
        try
        {
            await previewStarted.Task.WaitAsync(deadline.Token);
            Assert.Equal(1, pricingCalls);
            Assert.True(activity.IsActive);
            Assert.False(operation.IsCompleted);
            Assert.Equal([true], transitions);
            previewCompletion.SetResult();
            await operation.WaitAsync(deadline.Token);
            Assert.False(activity.IsActive);
            Assert.Equal([true, false], transitions);
            Assert.Equal(1, pricingCalls);
        }
        finally
        {
            previewCompletion.TrySetResult();
            await operation.WaitAsync(deadline.Token);
        }
    }

    [Fact]
    public async Task MaterialPreviewFailure_ClearsActivityAndPreservesOriginalFailure()
    {
        var activity = new RepricingActivity();
        var failure = new InvalidOperationException("preview failed");
        var transitions = new List<bool>();
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => activity.RunAsync(
            () => Task.CompletedTask,
            () =>
            {
                transitions.Add(activity.IsActive);
                return Task.CompletedTask;
            },
            () => throw failure));
        Assert.Same(failure, observed);
        Assert.False(activity.IsActive);
        Assert.Equal([true, false], transitions);
    }

    [Fact]
    public async Task PendingAndOverlappingUpdates_AnnounceStartAndFinalClear()
    {
        var activity = new RepricingActivity();
        var firstCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transitions = new List<bool>();
        Task Notify()
        {
            transitions.Add(activity.IsActive);
            return Task.CompletedTask;
        }

        var first = activity.RunAsync(() => firstCompletion.Task, Notify);
        var second = activity.RunAsync(() => secondCompletion.Task, Notify);
        Assert.True(activity.IsActive);
        Assert.Equal([true, true], transitions);

        firstCompletion.SetResult();
        await first;
        Assert.True(activity.IsActive);
        Assert.Equal([true, true, true], transitions);

        secondCompletion.SetResult();
        await second;
        Assert.False(activity.IsActive);
        Assert.Equal([true, true, true, false], transitions);
    }

    [Fact]
    public async Task FailedUpdate_StillAnnouncesClear()
    {
        var activity = new RepricingActivity();
        var transitions = new List<bool>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => activity.RunAsync(
            () => throw new InvalidOperationException("pricing failed"),
            () =>
            {
                transitions.Add(activity.IsActive);
                return Task.CompletedTask;
            }));

        Assert.Equal([true, false], transitions);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SamePartSupersession_RetiresOnlyOldGateAndNeverLetsLateCompletionRestoreIt(bool failLate)
    {
        var activity = new RepricingActivity();
        var part = Guid.NewGuid();
        var oldWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unrelatedWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var obsoleteAfterUpdates = 0;
        Task Notify() => Task.CompletedTask;
        var old = activity.RunAsync(part, () => oldWork.Task, Notify,
            _ => { obsoleteAfterUpdates++; return Task.CompletedTask; });
        var unrelated = activity.RunAsync(Guid.NewGuid(), () => unrelatedWork.Task, Notify);
        var current = activity.RunAsync(part, () => currentWork.Task, Notify);
        try
        {
            Assert.True(activity.IsActive);
            currentWork.TrySetResult();
            await current.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(activity.IsActive);
            unrelatedWork.TrySetResult();
            await unrelated.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(activity.IsActive);
            Assert.False(old.IsCompleted);
            if (failLate) oldWork.TrySetException(new IOException("controlled obsolete failure"));
            else oldWork.TrySetResult();
            if (failLate) await Assert.ThrowsAsync<IOException>(() => old);
            else await old;
            Assert.False(activity.IsActive);
            Assert.Equal(0, obsoleteAfterUpdates);
        }
        finally
        {
            oldWork.TrySetResult(); currentWork.TrySetResult(); unrelatedWork.TrySetResult();
            try { await old.WaitAsync(TimeSpan.FromSeconds(5)); } catch (IOException) when (failLate) { }
            await current.WaitAsync(TimeSpan.FromSeconds(5));
            await unrelated.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SamePartSupersession_InvalidatesAReachedAfterUpdateWithoutRestoringItsGate()
    {
        var activity = new RepricingActivity();
        var part = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleWrites = 0;
        var old = activity.RunAsync(part, () => Task.CompletedTask, () => Task.CompletedTask, async current =>
        {
            entered.TrySetResult();
            await release.Task;
            if (current()) staleWrites++;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await activity.RunAsync(part, () => Task.CompletedTask, () => Task.CompletedTask);
            Assert.False(activity.IsActive);
            Assert.False(old.IsCompleted);
            release.TrySetResult();
            await old.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, staleWrites);
            Assert.False(activity.IsActive);
        }
        finally { release.TrySetResult(); await old.WaitAsync(TimeSpan.FromSeconds(5)); }
    }


}
