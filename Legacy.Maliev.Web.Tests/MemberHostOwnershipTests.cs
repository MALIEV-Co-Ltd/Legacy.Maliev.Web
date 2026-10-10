using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberHostOwnershipTests
{
    [Fact]
    public async Task ExpiredLease_RejectsStartupBeforeDelegateRuns()
    {
        var ownership = new MemberHostOwnership();
        using var expired = new CancellationTokenSource();
        expired.Cancel();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => ownership.StartHost(() => { calls++; return new Host(); }, expired.Token));
        Assert.Equal(0, calls);
        await ownership.ReleaseAsync();
    }

    [Fact]
    public async Task Cleanup_WaitsForInFlightStartupAndClosesTheActualReturnedHandle()
    {
        var ownership = new MemberHostOwnership();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new Host();
        var starting = Task.Run(() => ownership.StartHost(() =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Controlled host startup not released.");
            return host;
        }, default));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cleanup = ownership.ReleaseAsync();
            Assert.False(cleanup.IsCompleted);
            release.Set();
            Assert.Same(host, await starting.WaitAsync(TimeSpan.FromSeconds(5)));
            await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, host.Stops);
            Assert.Equal(1, host.Disposals);
        }
        finally { release.Set(); await starting.WaitAsync(TimeSpan.FromSeconds(5)); await ownership.ReleaseAsync(); }
    }

    [Fact]
    public async Task FailedHostStop_PreservesFactoryAndRetryDoesNotRepeatCompletedHostRelease()
    {
        var ownership = new MemberHostOwnership();
        var first = new Host { FailOnce = true };
        var second = new Host();
        var factory = new Factory();
        ownership.RegisterFactory(() => factory, default);
        ownership.StartHost(() => first, default);
        ownership.StartHost(() => second, default);
        await Assert.ThrowsAsync<AggregateException>(() => ownership.ReleaseAsync());
        Assert.Equal(0, factory.Disposals);
        Assert.Equal(1, second.Disposals);
        await ownership.ReleaseAsync();
        Assert.Equal(2, first.Stops);
        Assert.Equal(1, second.Stops);
        Assert.Equal(1, second.Disposals);
        Assert.Equal(1, factory.Disposals);
    }

    [Fact]
    public async Task TerminalOwnership_RejectsLateFactoryBeforeAllocation()
    {
        var ownership = new MemberHostOwnership();
        await ownership.ReleaseAsync();
        var calls = 0;
        Assert.Throws<ObjectDisposedException>(() => ownership.RegisterFactory(() => { calls++; return new Factory(); }, default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task PartiallyFailingHostStart_RemainsOwnedAndIsStoppedDuringCleanup()
    {
        var ownership = new MemberHostOwnership();
        var service = new FailingHostedService();
        var builder = new HostBuilder().ConfigureServices(services => services.AddSingleton<IHostedService>(service));
        Assert.Throws<InvalidOperationException>(() => ownership.BuildAndStartHost(builder, default));
        await ownership.ReleaseAsync();
        Assert.Equal(1, service.Stops);
    }

    [Fact]
    public async Task TransientAsyncFactoryDisposalFailure_RetriesExactFactoryAndPreservesTerminalOwnership()
    {
        var ownership = new MemberHostOwnership();
        var host = new Host();
        var factory = new TransientAsyncDisposalFactory();
        ownership.RegisterFactory(() => factory, default);
        ownership.StartHost(() => host, default);
        try
        {
            await Assert.ThrowsAsync<AggregateException>(() => ownership.ReleaseAsync());
            Assert.Equal(1, factory.Disposals);
            Assert.Equal(1, host.Stops);
            Assert.Equal(1, host.Disposals);
            var lateAllocations = 0;
            Assert.Throws<ObjectDisposedException>(() => ownership.RegisterFactory(() =>
            {
                lateAllocations++;
                return new Factory();
            }, default));
            Assert.Equal(0, lateAllocations);
            await ownership.ReleaseAsync();
            Assert.Equal(2, factory.Disposals);
            Assert.Equal(1, host.Stops);
            Assert.Equal(1, host.Disposals);
            await ownership.ReleaseAsync();
            Assert.Equal(2, factory.Disposals);
        }
        finally { await ownership.ReleaseAsync(); }
    }

    private sealed class TransientAsyncDisposalFactory : WebApplicationFactory<Program>
    {
        public int Disposals;
        public override async ValueTask DisposeAsync()
        {
            Disposals++;
            await Task.Yield();
            if (Disposals == 1)
                throw new InvalidOperationException("Controlled transient asynchronous factory-disposal failure.");
        }
    }

    private sealed class FailingHostedService : IHostedService
    {
        public int Stops;
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Controlled host-start failure.");
        public Task StopAsync(CancellationToken cancellationToken) { Stops++; return Task.CompletedTask; }
    }

    private sealed class Host : IHost
    {
        public int Stops;
        public int Disposals;
        public bool FailOnce;
        public IServiceProvider Services => new EmptyServices();
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Stops++;
            if (FailOnce) { FailOnce = false; throw new InvalidOperationException("Controlled stop failure."); }
            return Task.CompletedTask;
        }
        public void Dispose() => Disposals++;
    }
    private sealed class EmptyServices : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        public int Disposals;
        public override ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
