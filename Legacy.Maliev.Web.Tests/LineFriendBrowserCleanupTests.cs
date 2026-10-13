namespace Legacy.Maliev.Web.Tests;

public sealed class LineFriendBrowserCleanupTests
{
    [Fact]
    public async Task BlockingInvocationDoesNotBlockIndependentOwnerAndRemainsInCustody()
    {
        using var lease = LineFriendBrowserCleanup.Reserve(false);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var observed = await LineFriendBrowserCleanup.CloseAsync(lease,
                [new("context", () => { entered.SetResult(); release.Wait(); return Task.CompletedTask; }),
                 new("client", () => { next.SetResult(); return Task.CompletedTask; })],
                new Exception(), TimeSpan.Zero, _ => { });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await next.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(LineFriendBrowserCleanup.HasCustody(lease.Id)); Assert.False(observed.Completion.IsCompleted);
        }
        finally { release.Set(); await lease.Completion.WaitAsync(TimeSpan.FromSeconds(2)); LineFriendBrowserCleanup.ReleaseSyntheticControl(lease); }
    }
    [Fact]
    public async Task LateFaultIsObservedAndDurableCustodySurvivesCallerReturn()
    {
        using var lease = LineFriendBrowserCleanup.Reserve(false);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await LineFriendBrowserCleanup.CloseAsync(lease, [new("context", () => held.Task)], new Exception(), TimeSpan.Zero, _ => { });
            held.SetException(new InvalidOperationException("private")); await lease.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(LineFriendBrowserCleanup.HasCustody(lease.Id)); Assert.IsType<InvalidOperationException>(lease.Nodes["context"].Failure);
            Assert.Same(held.Task, lease.Nodes["context"].Actual);
        }
        finally { held.TrySetResult(); await lease.Completion.WaitAsync(TimeSpan.FromSeconds(2)); LineFriendBrowserCleanup.ReleaseSyntheticControl(lease); }
    }
    [Fact]
    public async Task NoPrimaryThrowOccursOnlyAfterCustodyRegistration()
    {
        using var lease = LineFriendBrowserCleanup.Reserve(false);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => LineFriendBrowserCleanup.CloseAsync(lease,
                [new("context", () => held.Task)], null, TimeSpan.Zero, _ => { }));
            Assert.True(LineFriendBrowserCleanup.HasCustody(lease.Id));
        }
        finally { held.SetResult(); await lease.Completion.WaitAsync(TimeSpan.FromSeconds(2)); LineFriendBrowserCleanup.ReleaseSyntheticControl(lease); }
    }
    [Fact]
    public async Task ParentReleaseWaitsForActualDependencySuccess()
    {
        using var lease = LineFriendBrowserCleanup.Reserve(false);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var parent = false;
        try
        {
            await LineFriendBrowserCleanup.CloseAsync(lease,
                [new("context", () => held.Task), new("browser", () => { parent = true; return Task.CompletedTask; }, ["context"])],
                new Exception(), TimeSpan.Zero, _ => { });
            Assert.False(parent); held.SetResult(); await lease.Completion.WaitAsync(TimeSpan.FromSeconds(2)); Assert.True(parent);
        }
        finally { held.TrySetResult(); await lease.Completion.WaitAsync(TimeSpan.FromSeconds(2)); LineFriendBrowserCleanup.ReleaseSyntheticControl(lease); }
    }
    [Fact]
    public async Task PrimaryIdentitySurvivesCleanupFailureAndThrowingReporter()
    {
        using var lease = LineFriendBrowserCleanup.Reserve(false); var original = new InvalidOperationException("primary");
        try
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                Exception? primary = null;
                try { throw original; }
                catch (Exception exception) { primary = exception; throw; }
                finally { await LineFriendBrowserCleanup.CloseAsync(lease, [new("context", () => throw new Exception())], primary, TimeSpan.FromSeconds(1), _ => throw new Exception()); }
            });
            Assert.Same(original, failure); Assert.True(LineFriendBrowserCleanup.HasCustody(lease.Id));
        }
        finally { await lease.Completion.WaitAsync(TimeSpan.FromSeconds(2)); LineFriendBrowserCleanup.ReleaseSyntheticControl(lease); }
    }
}
