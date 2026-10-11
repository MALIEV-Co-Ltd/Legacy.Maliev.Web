namespace Legacy.Maliev.Web.Tests;

public sealed class CompanyOwnedProcessTests
{
    private static readonly CompanyProcessBudgets Budgets = new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30));

    [Theory]
    [InlineData("birth")]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task StartPartialFailure_StopsOwnedChildAndClosesDrains(string stage)
    {
        var backend = new FakeBackend { StartupFailure = stage };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CompanyOwnedProcess.StartAsync(backend, Budgets));
        Assert.True(backend.Started);
        Assert.True(backend.HasExited);
        Assert.True(backend.Disposed);
        Assert.True(backend.DrainObserved);
        Assert.Equal(1, backend.GracefulSignals);
    }

    [Theory]
    [InlineData("signal")]
    [InlineData("identity")]
    [InlineData("wait")]
    [InlineData("drain")]
    public async Task StopFailure_StillForcesAndWaitsBeforeSurfacingAggregate(string stage)
    {
        var backend = new FakeBackend { StopFailure = stage, GracefulExits = false };
        var owned = await CompanyOwnedProcess.StartAsync(backend, Budgets);
        await Assert.ThrowsAsync<AggregateException>(async () => await owned.DisposeAsync());
        Assert.Equal(1, backend.ForcedSignals);
        Assert.True(backend.WaitCalls >= 2);
        Assert.True(backend.HasExited);
        Assert.True(backend.Disposed);
        Assert.True(backend.DrainObserved);
    }

    [Fact]
    public async Task DrainNeverCompletes_IsBoundedCanceledAndReported()
    {
        var backend = new FakeBackend { NeverDrain = true };
        var owned = await CompanyOwnedProcess.StartAsync(backend, Budgets);
        await Assert.ThrowsAsync<AggregateException>(async () => await owned.DisposeAsync());
        Assert.True(backend.DrainCanceled);
        Assert.True(backend.Disposed);
        Assert.True(backend.HasExited);
    }

    [Fact]
    public async Task GracefulExit_ClosesProcessAndAllDrainsWithoutForcedStop()
    {
        var backend = new FakeBackend();
        var owned = await CompanyOwnedProcess.StartAsync(backend, Budgets);
        await owned.DisposeAsync();
        Assert.Equal(0, backend.ForcedSignals);
        Assert.True(backend.HasExited);
        Assert.True(backend.DrainObserved);
        Assert.True(backend.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostSpawnAcquisitionFailure_WaitsForSupervisorOrReportsRetainedOwnership(bool supervisorWaitFails)
    {
        var backend = new FakeBackend { StartupFailure = "post-spawn-acquisition", SupervisorWaitFails = supervisorWaitFails };
        await Assert.ThrowsAsync<AggregateException>(() => CompanyOwnedProcess.StartAsync(backend, Budgets));
        Assert.True(backend.Started);
        Assert.Equal(1, backend.GracefulSignals);
        Assert.Equal(1, backend.ForcedSignals);
        Assert.True(backend.SupervisorWaitObserved);
        Assert.Equal(!supervisorWaitFails, backend.HasExited);
        Assert.Equal(!supervisorWaitFails, backend.Disposed);
        Assert.Equal(supervisorWaitFails, backend.OwnershipRetained);
    }

    private sealed class FakeBackend : ICompanyProcessBackend
    {
        public string? StartupFailure { get; init; }
        public string? StopFailure { get; init; }
        public bool GracefulExits { get; init; } = true;
        public bool NeverDrain { get; init; }
        public bool SupervisorWaitFails { get; init; }
        public bool SupervisorWaitObserved { get; private set; }
        public bool OwnershipRetained { get; private set; }
        public bool Started { get; private set; }
        public bool HasExited { get; private set; }
        public bool Disposed { get; private set; }
        public bool DrainObserved { get; private set; }
        public bool DrainCanceled { get; private set; }
        public int GracefulSignals { get; private set; }
        public int ForcedSignals { get; private set; }
        public int WaitCalls { get; private set; }
        public void Start()
        {
            Started = true;
            if (StartupFailure == "post-spawn-acquisition") throw new InvalidOperationException("pidfd acquisition failed after spawn");
        }
        public void CaptureOwnership()
        {
            if (StartupFailure == "birth") throw new InvalidOperationException("birth failed");
        }
        public void BeginDrains()
        {
            if (StartupFailure is "stdout" or "stderr") throw new InvalidOperationException("drain start failed");
        }
        public void Signal(bool force)
        {
            if (StartupFailure == "post-spawn-acquisition")
            {
                if (force) ForcedSignals++; else GracefulSignals++;
                throw new InvalidOperationException("pidfd acquisition still unavailable");
            }
            if (force) { ForcedSignals++; HasExited = true; return; }
            GracefulSignals++;
            if (StopFailure is "signal" or "identity") throw new InvalidOperationException("signal identity failed");
            if (GracefulExits) HasExited = true;
        }
        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            WaitCalls++;
            if (StopFailure == "wait" && WaitCalls == 1) throw new InvalidOperationException("wait failed");
            if (!HasExited) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public async Task DrainAsync(CancellationToken cancellationToken)
        {
            DrainObserved = true;
            if (StopFailure == "drain") throw new InvalidOperationException("drain failed");
            if (NeverDrain)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { DrainCanceled = true; throw; }
            }
        }
        public Task WaitForLeaseExitAsync(CancellationToken cancellationToken)
        {
            SupervisorWaitObserved = true;
            if (SupervisorWaitFails) throw new InvalidOperationException("supervisor exit unverified");
            HasExited = true;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            if (Started && !HasExited)
            {
                OwnershipRetained = true;
                throw new InvalidOperationException("ownership retained; cleanup failed");
            }
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
