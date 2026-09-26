using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class RepricingActivityTests
{
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
