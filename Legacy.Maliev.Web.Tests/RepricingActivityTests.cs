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
}
