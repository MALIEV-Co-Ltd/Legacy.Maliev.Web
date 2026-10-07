namespace Legacy.Maliev.Web.Tests;

/// <summary>Pure controlled lifecycle faults; no process, browser, Docker or database is started.</summary>
public sealed class BillingProofLifetimeTests
{
    [Fact]
    public async Task Expiry_JoinsAdmittedSynchronousBodyBeforeDependencyRelease()
    {
        var expire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new BillingProofLifetime(expirySignal: expire.Task);
        var removed = false;
        owner.OwnHost(() => { closing.TrySetResult(); return ValueTask.CompletedTask; });
        owner.OwnDependency(() => { removed = true; return ValueTask.CompletedTask; });
        var running = Task.Run(() => owner.RunAsync(() =>
        {
            entered.TrySetResult();
            resume.Task.GetAwaiter().GetResult();
            return Task.CompletedTask;
        }));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            expire.TrySetResult();
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(() => owner.ExpirySettled.WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.False(removed);
        }
        finally { resume.TrySetResult(); await running; await owner.ExpirySettled; await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DisposeAsync().AsTask()); }
        Assert.True(removed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expiry_JoinsAllocationReservationAndReleasesLateResourceExactlyOnce(bool dependency)
    {
        var expire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new BillingProofLifetime(expirySignal: expire.Task);
        var releases = 0;
        owner.OwnHost(() => { closing.TrySetResult(); return ValueTask.CompletedTask; });
        object Allocate() { entered.TrySetResult(); resume.Task.GetAwaiter().GetResult(); return new object(); }
        ValueTask Release(object _) { releases++; return ValueTask.CompletedTask; }
        var acquiring = Task.Run(() => dependency ? owner.AcquireDependency(Allocate, Release) : owner.AcquireHost(Allocate, Release));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            expire.TrySetResult();
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(owner.IsRetained);
            Assert.Equal(0, releases);
        }
        finally { resume.TrySetResult(); await Assert.ThrowsAsync<InvalidOperationException>(async () => { await acquiring; }); await owner.ExpirySettled; await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DisposeAsync().AsTask()); }
        Assert.Equal(1, releases);
        Assert.False(owner.IsRetained);
        Assert.Throws<InvalidOperationException>(() => owner.OwnDependency(() => ValueTask.CompletedTask));
        Assert.Throws<InvalidOperationException>(() => owner.OwnHost(() => ValueTask.CompletedTask));
    }

    [Theory]
    [InlineData("exited")]
    [InlineData("refused")]
    public async Task ProvenNoLiveChild_ClosesOriginalWrapperWithoutLiveExecutableLookup(string state)
    {
        var owner = new BillingProofLifetime();
        var process = new ControlledChild { ProvenStartState = state, AcquisitionFailure = "identity" };
        Assert.Throws<IOException>(() => owner.StartChild(process));
        await owner.DisposeAsync();
        Assert.True(process.HandleClosed);
        Assert.Equal(0, process.Kills);
        Assert.False(owner.IsRetained);
    }

    [Fact]
    public async Task ActualIdleExpiry_ClosesOriginalChildBeforeDependency_AndReleasesOwner()
    {
        var owner = new BillingProofLifetime(TimeSpan.FromMilliseconds(100));
        var process = new ControlledChild();
        owner.StartChild(process);
        var removed = false;
        owner.OwnDependency(() => { Assert.True(process.HandleClosed); removed = true; return ValueTask.CompletedTask; });
        try
        {
            await owner.ExpirySettled.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(removed);
            Assert.False(owner.IsRetained);
            Assert.Throws<InvalidOperationException>(() => owner.StartChild(new ControlledChild()));
        }
        finally { await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DisposeAsync().AsTask()); }
    }

    [Fact]
    public async Task ActualExpiry_DelayedFrontendShutdownRetainsBackendAndCaseWorkUntilSettled()
    {
        var owner = new BillingProofLifetime(TimeSpan.FromMilliseconds(100));
        var enteringShutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseShutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.OwnHost(async () => { enteringShutdown.TrySetResult(); await releaseShutdown.Task; releaseBody.TrySetResult(); });
        var removed = false;
        owner.OwnDependency(() => { removed = true; return ValueTask.CompletedTask; });
        var running = owner.RunAsync(() => releaseBody.Task);
        try
        {
            await enteringShutdown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(running.IsCompleted);
            Assert.False(removed);
            Assert.True(owner.IsRetained);
            Assert.Throws<InvalidOperationException>(() => owner.StartChild(new ControlledChild()));
            releaseShutdown.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.ExpirySettled.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(removed);
            Assert.False(owner.IsRetained);
        }
        finally { releaseShutdown.TrySetResult(); releaseBody.TrySetResult(); await running; await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DisposeAsync().AsTask()); }
    }

    [Theory]
    [InlineData("start")]
    [InlineData("identity")]
    [InlineData("readers")]
    public async Task AcquisitionFault_AfterAllocationRetainsOriginalChildForCleanup(string stage)
    {
        var owner = new BillingProofLifetime();
        var process = new ControlledChild { AcquisitionFailure = stage };
        Assert.Throws<IOException>(() => owner.StartChild(process));
        Assert.True(owner.IsRetained);
        process.AcquisitionFailure = null;
        await owner.DisposeAsync();
        Assert.True(process.Exited);
        Assert.True(process.ReadersClosed);
        Assert.True(process.HandleClosed);
        Assert.False(owner.IsRetained);
        Assert.Equal(1, process.Starts);
    }

    [Theory]
    [InlineData("kill")]
    [InlineData("reap")]
    [InlineData("readers")]
    [InlineData("reader-close")]
    [InlineData("handle-close")]
    public async Task FailedCleanup_RetainsOwnershipAndDependencies_ThenExactRetryCanRelease(string stage)
    {
        var owner = new BillingProofLifetime();
        var process = new ControlledChild();
        owner.StartChild(process);
        var dependencies = 0;
        owner.OwnDependency(() => { dependencies++; return ValueTask.CompletedTask; });
        process.CleanupFailure = stage;
        await Assert.ThrowsAsync<BillingProofQuarantineException>(() => owner.DisposeAsync().AsTask());
        Assert.True(owner.IsRetained);
        Assert.False(process.HandleClosed);
        Assert.Equal(0, dependencies);
        process.CleanupFailure = null;
        await owner.DisposeAsync();
        Assert.Equal(1, dependencies);
        Assert.True(process.HandleClosed);
        Assert.False(owner.IsRetained);
        Assert.Equal(1, process.Starts);
    }

    [Fact]
    public async Task FailedHostDisposal_IsStickyAndCannotBecomeNoOpClearance()
    {
        var owner = new BillingProofLifetime(TimeSpan.FromMilliseconds(100));
        var calls = 0;
        owner.OwnHost(() => { calls++; throw new IOException("Controlled synchronous host fault."); });
        var process = new ControlledChild();
        owner.StartChild(process);
        var dependencies = 0;
        owner.OwnDependency(() => { dependencies++; return ValueTask.CompletedTask; });
        await Assert.ThrowsAsync<BillingProofQuarantineException>(() => owner.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<BillingProofQuarantineException>(() => owner.DisposeAsync().AsTask());
        Assert.Equal(1, calls);
        Assert.Equal(0, process.Kills);
        Assert.Equal(0, dependencies);
        Assert.True(owner.IsRetained);
        Assert.False(process.HandleClosed);
        await owner.ExpirySettled.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(owner.IsRetained);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task InterruptedHostStartup_RefusesHostDisposalAndDependencyRelease()
    {
        var owner = new BillingProofLifetime();
        var disposals = 0;
        var host = owner.OwnHost(() => { disposals++; return ValueTask.CompletedTask; }, startupRequired: true);
        var dependencies = 0;
        owner.OwnDependency(() => { dependencies++; return ValueTask.CompletedTask; });
        await Assert.ThrowsAsync<BillingProofQuarantineException>(() => owner.DisposeAsync().AsTask());
        Assert.Equal(0, disposals);
        Assert.Equal(0, dependencies);
        Assert.True(owner.IsRetained);
        // This pure control supplies a genuine successful-start handshake before retry.
        host.Started();
        await owner.DisposeAsync();
        Assert.Equal(1, disposals);
        Assert.Equal(1, dependencies);
    }

    [Fact]
    public async Task SettledChildReaders_BeforeHandleAndDependencyRelease_AllHostsAttemptedFirst()
    {
        var owner = new BillingProofLifetime();
        var order = new List<string>();
        owner.OwnHost(() => { order.Add("host-one"); return ValueTask.CompletedTask; });
        owner.OwnHost(() => { order.Add("host-two"); return ValueTask.CompletedTask; });
        var process = new ControlledChild { Events = order };
        owner.StartChild(process);
        owner.OwnDependency(() => { order.Add("database"); return ValueTask.CompletedTask; });
        await owner.DisposeAsync();
        Assert.Equal(new[] { "host-two", "host-one", "readers", "handle", "database" }, order);
        Assert.False(owner.IsRetained);
        Assert.Throws<InvalidOperationException>(() => owner.StartChild(new ControlledChild()));
    }

    [Fact]
    public async Task SynchronousBlockedHostRelease_IsBoundedRetainedAndReusesOriginalAttempt()
    {
        var owner = new BillingProofLifetime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var dependencies = 0;
        var process = new ControlledChild();
        owner.StartChild(process);
        owner.OwnHost(() =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            resume.Task.GetAwaiter().GetResult();
            return ValueTask.CompletedTask;
        }, closeTimeout: TimeSpan.FromMilliseconds(100));
        owner.OwnDependency(() => { dependencies++; return ValueTask.CompletedTask; });
        var initial = Task.Run(() => owner.DisposeAsync().AsTask());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<BillingProofQuarantineException>(() => initial.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<BillingProofQuarantineException>(() => owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(owner.IsRetained);
            Assert.False(process.HandleClosed);
            Assert.Equal(0, dependencies);
            Assert.Equal(1, calls);
        }
        finally
        {
            resume.TrySetResult();
            try { await initial.WaitAsync(TimeSpan.FromSeconds(5)); } catch (BillingProofQuarantineException) { }
            await owner.DisposeAsync();
            await owner.ExpirySettled.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(owner.IsRetained);
        Assert.True(process.HandleClosed);
        Assert.Equal(1, dependencies);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SettledReaderFault_ReleasesResourcesButRepeatDisposalStillRejectsProof()
    {
        var owner = new BillingProofLifetime();
        var process = new ControlledChild { OutputFaulted = true };
        owner.StartChild(process);
        var dependencies = 0;
        owner.OwnDependency(() => { dependencies++; return ValueTask.CompletedTask; });

        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DisposeAsync().AsTask());
        Assert.False(owner.IsRetained);
        Assert.True(process.Exited);
        Assert.True(process.ReadersClosed);
        Assert.True(process.HandleClosed);
        Assert.Equal(1, dependencies);

        // Acceptance remembers the original fault even if an adapter later reports no current fault.
        process.OutputFaulted = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DisposeAsync().AsTask());
        await owner.ExpirySettled.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(owner.IsRetained);
        Assert.Equal(1, dependencies);
        Assert.Equal(1, process.Starts);
        Assert.Equal(1, process.Kills);
        Assert.Contains("\"readerFailureRejected\":true", owner.OwnershipReceipt(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("daemon")]
    [InlineData("id")]
    [InlineData("created")]
    [InlineData("owner")]
    [InlineData("envelope")]
    [InlineData("signature")]
    public async Task BackendGenerationChange_RefusesMutationAndAbsenceReceipt(string stage)
    {
        var backend = new ControlledBackend();
        var lease = new BillingBackendLease(backend);
        await lease.StartAsync(CancellationToken.None);
        var original = backend.Current;
        switch (stage)
        {
            case "daemon": backend.Daemon = "other-daemon"; break;
            case "id": backend.Current = original with { Id = new string('c', 64) }; break;
            case "created": backend.Current = original with { CreatedUtc = original.CreatedUtc.AddTicks(1) }; break;
            case "owner": backend.Current = original with { Owner = "foreign-owner" }; break;
            case "envelope": backend.Current = original with { EnvelopeValid = false }; break;
            case "signature": backend.Current = original with { Signature = "changed-envelope" }; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.CloseAsync().AsTask());
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.Removes);
        Assert.False(lease.AbsenceVerified);
        Assert.False(lease.SdkReleased);
        backend.Current = original; backend.Daemon = "original-daemon";
        await lease.CloseAsync(); // Controlled fixture restores the exact original, never adopts the changed generation.
    }

    [Fact]
    public async Task BackendExactOriginalRemoval_IsReobservedAfterSdkDisposal()
    {
        var backend = new ControlledBackend();
        var lease = new BillingBackendLease(backend);
        await lease.StartAsync(CancellationToken.None);
        await lease.CloseAsync();
        Assert.True(lease.CaptureVerified);
        Assert.True(lease.AbsenceVerified);
        Assert.True(lease.SdkReleased);
        Assert.Equal(new[] { "stop", "remove", "sdk-dispose", "observer-dispose" }, backend.Events);
        Assert.Equal(1, backend.Stops);
        Assert.Equal(1, backend.Removes);
        Assert.True(backend.AbsenceInspectionsAfterSdkDisposal >= 2);
        Assert.Null(backend.SdkId);
    }

    [Fact]
    public async Task BackendSdkCreationIdUnavailable_RefusesNameBasedAdoptionAndCleanup()
    {
        var backend = new ControlledBackend { HideSdkId = true };
        var lease = new BillingBackendLease(backend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.StartAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.CloseAsync().AsTask());
        Assert.False(lease.CaptureVerified);
        Assert.False(lease.AbsenceVerified);
        Assert.False(lease.SdkReleased);
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.Removes);
    }

    [Fact]
    public async Task BackendSdkIdentityLostBeforeDisposal_RefusesMutation()
    {
        var backend = new ControlledBackend();
        var lease = new BillingBackendLease(backend);
        await lease.StartAsync(CancellationToken.None);
        backend.HideSdkId = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.CloseAsync().AsTask());
        Assert.Equal(0, backend.Stops);
        Assert.Equal(0, backend.Removes);
        Assert.False(lease.AbsenceVerified);
        Assert.False(lease.SdkReleased);
        backend.HideSdkId = false;
        await lease.CloseAsync();
        Assert.True(lease.AbsenceVerified);
    }

    [Fact]
    public async Task BackendUnsettledRemoval_RefusesAbsenceAndSdkRelease()
    {
        var backend = new ControlledBackend { LeaveAfterRemove = true };
        var lease = new BillingBackendLease(backend);
        await lease.StartAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.CloseAsync().AsTask());
        Assert.False(lease.AbsenceVerified);
        Assert.False(lease.SdkReleased);
        Assert.DoesNotContain("sdk-dispose", backend.Events);
        backend.LeaveAfterRemove = false;
        await lease.CloseAsync();
    }

    private sealed class ControlledBackend : IBillingBackend
    {
        public string Run { get; } = new string('d', 32);
        public string Name => "billing-proof-" + Run + "-postgres";
        public string Database => "profile_contract_" + new string('e', 32);
        public string ExpiresUtc { get; } = DateTimeOffset.UtcNow.AddMinutes(50).ToString("O");
        public DateTime IntentUtc { get; } = DateTime.UtcNow.AddSeconds(-1);
        public string? SdkId => HideSdkId || !started || sdkDisposed ? null : new string('a', 64);
        public Testcontainers.PostgreSql.PostgreSqlContainer? Postgres => null;
        internal string Daemon { get; set; } = "original-daemon";
        internal bool HideSdkId { get; set; }
        internal bool LeaveAfterRemove { get; set; }
        internal int Stops { get; private set; }
        internal int Removes { get; private set; }
        internal int AbsenceInspectionsAfterSdkDisposal { get; private set; }
        internal List<string> Events { get; } = [];
        internal BillingBackendIdentity Current { get; set; }
        private bool started;
        private bool absent;
        private bool sdkDisposed;
        internal ControlledBackend()
        {
            Current = new(new string('a', 64), DateTime.UtcNow, "sha256:" + new string('b', 64), "/" + Name,
                "web-billing-proof", Run, ExpiresUtc, "postgres:18-alpine", true, "original-envelope");
        }
        public Task<string> DaemonAsync(CancellationToken _) => Task.FromResult(Daemon);
        public Task<BillingBackendIdentity?> InspectAsync(string _, CancellationToken token)
        {
            if (sdkDisposed && absent) AbsenceInspectionsAfterSdkDisposal++;
            return Task.FromResult(started && !absent ? Current : null);
        }
        public Task StartAsync(CancellationToken _) { started = true; return Task.CompletedTask; }
        public Task StopAsync(string id, CancellationToken _)
        {
            Assert.Equal(new string('a', 64), id); Stops++; Events.Add("stop"); return Task.CompletedTask;
        }
        public Task RemoveAsync(string id, CancellationToken _)
        {
            Assert.Equal(new string('a', 64), id); Removes++; Events.Add("remove"); absent = !LeaveAfterRemove; return Task.CompletedTask;
        }
        public ValueTask DisposeSdkAsync() { Events.Add("sdk-dispose"); sdkDisposed = true; return ValueTask.CompletedTask; }
        public void Dispose() => Events.Add("observer-dispose");
    }

    private sealed class ControlledChild : IBillingChildProcess
    {
        internal string? ProvenStartState { get; init; }
        private bool refused;
        internal string? AcquisitionFailure { get; set; }
        internal string? CleanupFailure { get; set; }
        internal List<string>? Events { get; init; }
        internal bool Exited { get; private set; }
        internal bool ReadersClosed { get; private set; }
        internal bool HandleClosed { get; private set; }
        internal int Starts { get; private set; }
        internal int Kills { get; private set; }
        public object Receipt => new { Starts, Kills, Exited, ReadersClosed, HandleClosed, controlled = true };
        internal bool OutputFaulted { get; set; }
        public bool ReaderFailed => OutputFaulted;
        public bool StartRefused => refused;
        public bool HasExited => Exited;
        public int ExitCode => 0;
        public void Start() { Starts++; if (ProvenStartState == "refused") { refused = true; throw new IOException("Controlled explicit Start refusal."); } if (ProvenStartState == "exited") Exited = true; FailAcquisition("start"); }
        public bool VerifyNoLiveChild() => refused || Exited;
        public void CaptureIdentity() => FailAcquisition("identity");
        public void StartReaders() => FailAcquisition("readers");
        public Task WaitForExitAsync() => Exited && CleanupFailure != "reap" ? Task.CompletedTask : Task.FromException(new TimeoutException());
        public void RequestGracefulStop() { }
        public void KillExact() { FailCleanup("kill"); Kills++; if (CleanupFailure != "reap") Exited = true; }
        public Task JoinReadersAsync() => CleanupFailure == "readers" ? Task.FromException(new TimeoutException()) : Task.CompletedTask;
        public void CloseReaders() { FailCleanup("reader-close"); ReadersClosed = true; Events?.Add("readers"); }
        public void CloseHandle() { FailCleanup("handle-close"); HandleClosed = true; Events?.Add("handle"); }
        private void FailAcquisition(string stage) { if (AcquisitionFailure == stage) throw new IOException("Controlled acquisition fault."); }
        private void FailCleanup(string stage) { if (CleanupFailure == stage) throw new IOException("Controlled cleanup fault."); }
    }
}
