namespace Legacy.Maliev.Web.Tests;

public sealed class MaterialPricingAsyncCleanupTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task AsyncReleaseFault_AttemptsEveryLaterReleaseAndRethrowsOriginal(int failedRelease)
    {
        var order = new List<int>();
        var failure = new IOException("Synthetic cleanup failure");
        var releases = Enumerable.Range(0, 7).Select(index => (Func<ValueTask>)(async () =>
        {
            order.Add(index);
            await Task.Yield();
            if (index == failedRelease) throw failure;
        })).ToArray();

        var observed = await Assert.ThrowsAsync<IOException>(() =>
            MaterialPricingTestCleanup.ReleaseAllAsync(null, releases).AsTask());

        Assert.Same(failure, observed);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], order);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task SynchronousReleaseFault_DoesNotPreventOtherReleases(int failedRelease)
    {
        var order = new List<int>();
        var failure = new InvalidOperationException("Synthetic synchronous cleanup failure");
        var releases = Enumerable.Range(0, 7).Select(index => (Func<ValueTask>)(() =>
        {
            order.Add(index);
            if (index == failedRelease) throw failure;
            return ValueTask.CompletedTask;
        })).ToArray();

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MaterialPricingTestCleanup.ReleaseAllAsync(null, releases).AsTask());

        Assert.Same(failure, observed);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], order);
    }

    [Fact]
    public async Task TwoReleaseFaults_RetainBothReferencesAndThrowFirst()
    {
        var first = new TimeoutException("Synthetic unsettled drain");
        var second = new IOException("Synthetic resource release failure");
        var lastReleased = false;

        var observed = await Assert.ThrowsAsync<TimeoutException>(() =>
            MaterialPricingTestCleanup.ReleaseAllAsync(null,
                () => ValueTask.FromException(first),
                () => ValueTask.FromException(second),
                () => { lastReleased = true; return ValueTask.CompletedTask; }).AsTask());

        Assert.Same(first, observed);
        var failures = Assert.IsType<Exception[]>(observed.Data["MaterialPricingCleanupFailures"]);
        Assert.Collection(failures, error => Assert.Same(first, error), error => Assert.Same(second, error));
        Assert.True(lastReleased);
    }

    [Fact]
    public async Task ConstructionPrimary_IsRethrownWithAllCleanupFaultsAttached()
    {
        var primary = new InvalidOperationException("Synthetic construction failure");
        var first = new TimeoutException("Synthetic unsettled drain");
        var second = new IOException("Synthetic resource release failure");

        async Task ConstructAsync()
        {
            try { throw primary; }
            catch (Exception original)
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(original,
                    () => ValueTask.FromException(first), () => ValueTask.FromException(second));
                throw;
            }
        }

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(ConstructAsync);

        Assert.Same(primary, observed);
        var failures = Assert.IsType<Exception[]>(observed.Data["MaterialPricingCleanupFailures"]);
        Assert.Collection(failures, error => Assert.Same(first, error), error => Assert.Same(second, error));
    }

    [Fact]
    public async Task SuccessfulReleases_AreAwaitedInOriginalOrder()
    {
        var order = new List<int>();
        await MaterialPricingTestCleanup.ReleaseAllAsync(null,
            async () => { order.Add(0); await Task.Yield(); order.Add(1); },
            () => { order.Add(2); return ValueTask.CompletedTask; });

        Assert.Equal([0, 1, 2], order);
    }

    [Fact]
    public async Task CanceledRelease_AttemptsLaterResourceAndRetainsCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var laterReleased = false;

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MaterialPricingTestCleanup.ReleaseAllAsync(null,
                () => ValueTask.FromCanceled(cancellation.Token),
                () => { laterReleased = true; return ValueTask.CompletedTask; }).AsTask());

        Assert.Equal(cancellation.Token, observed.CancellationToken);
        Assert.True(laterReleased);
    }

    [Fact]
    public async Task PendingRelease_IsNotReportedCompleteOrSkipped()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterReleased = false;
        var cleanup = MaterialPricingTestCleanup.ReleaseAllAsync(null,
            () => new ValueTask(release.Task),
            () => { laterReleased = true; return ValueTask.CompletedTask; }).AsTask();
        try
        {
            Assert.False(cleanup.IsCompleted);
            Assert.False(laterReleased);
        }
        finally
        {
            release.TrySetResult();
            await cleanup.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True(laterReleased);
    }
}
