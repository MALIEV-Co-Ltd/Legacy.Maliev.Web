using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberRedisConnectionOwnershipTests
{
    [Fact]
    public async Task ExactHostLease_ClosesExternalInstanceOnceAfterActualDisposal()
    {
        var resource = new Connection();
        var hosts = new MemberHostOwnership();
        var connections = new MemberRedisConnectionOwnership();
        var returned = hosts.BuildAndStartHost(new HostBuilder().ConfigureServices(services =>
            services.AddSingleton(resource)), default, host => { connections.Capture(host.Services.GetRequiredService<Connection>()); return connections.Release; });
        try
        {
            await returned.StopAsync();
            returned.Dispose();
            Assert.Equal(1, resource.Disposals);
            connections.Release();
            connections.Release();
            await hosts.ReleaseAsync();
            Assert.Equal(1, resource.Disposals);
        }
        finally { await hosts.ReleaseAsync(); connections.Release(); }
    }

    [Fact]
    public async Task StartupFailure_UncachedFactoryDisposalPreservesCompanionUntilExactHostCleanup()
    {
        var resource = new Connection();
        var hosts = new MemberHostOwnership();
        var connections = new MemberRedisConnectionOwnership();
        var service = new FailingStart();
        resource.BeforeDispose = () => { Assert.Equal(1, service.Stops); Assert.Equal(1, service.Disposals); };
        await using var factory = new UncachedFactory(hosts, connections);
        try
        {
            Assert.Throws<InvalidOperationException>(() => factory.Start(new HostBuilder()
                .ConfigureServices(services => { services.AddSingleton(resource); services.AddSingleton<IHostedService>(_ => service); })));
            await factory.DisposeAsync(); // WAF has no cached host when CreateHost throws.
            Assert.Equal(0, resource.Disposals);
            Assert.Equal(0, service.Stops); Assert.Equal(0, service.Disposals);
            await hosts.ReleaseAsync();
            connections.Release();
            Assert.Equal(1, resource.Disposals);
        }
        finally { await hosts.ReleaseAsync(); connections.Release(); }
    }

    [Fact]
    public async Task CompanionFailure_RetryDoesNotRepeatCompletedHostDisposal()
    {
        var resource = new Connection { FailOnce = true };
        var service = new CountingService();
        var hosts = new MemberHostOwnership();
        var connections = new MemberRedisConnectionOwnership();
        var returned = hosts.BuildAndStartHost(new HostBuilder().ConfigureServices(services =>
            services.AddSingleton<IHostedService>(_ => service)), default,
            _ => { connections.Capture(resource); return connections.Release; });
        try
        {
            await returned.StopAsync();
            Assert.Throws<AggregateException>(() => returned.Dispose());
            Assert.Equal(1, service.Stops); Assert.Equal(1, service.Disposals);
            await hosts.ReleaseAsync();
            Assert.Equal(1, service.Stops); Assert.Equal(1, service.Disposals);
            Assert.Equal(2, resource.Disposals);
        }
        finally { await hosts.ReleaseAsync(); connections.Release(); }
    }

    [Fact]
    public void FailedDisposal_RetainsSameInstanceAndSkipsAlreadyReleasedCompanionsOnRetry()
    {
        var first = new Connection();
        var retry = new Connection { FailOnce = true };
        var connections = new MemberRedisConnectionOwnership();
        connections.Capture(first); connections.Capture(retry);
        try
        {
            Assert.Throws<AggregateException>(() => connections.Release());
            Assert.Equal(1, first.Disposals); Assert.Equal(1, retry.Disposals);
            connections.Release(); connections.Release();
            Assert.Equal(1, first.Disposals); Assert.Equal(2, retry.Disposals);
        }
        finally { connections.Release(); }
    }

    [Fact]
    public async Task ConcurrentRelease_ClosesOnlyEachCapturedInstanceOnce()
    {
        var resource = new Connection();
        var connections = new MemberRedisConnectionOwnership();
        connections.Capture(resource); connections.Capture(resource);
        try
        {
            await Task.WhenAll(Task.Run(connections.Release), Task.Run(connections.Release));
            Assert.Equal(1, resource.Disposals);
            Assert.Throws<ObjectDisposedException>(() => connections.Capture(resource));
        }
        finally { connections.Release(); }
    }

    private sealed class UncachedFactory(MemberHostOwnership hosts, MemberRedisConnectionOwnership connections)
        : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder) => hosts.BuildAndStartHost(builder, default,
            host => { connections.Capture(host.Services.GetRequiredService<Connection>()); return connections.Release; });
        internal IHost Start(IHostBuilder builder) => CreateHost(builder);
    }

    private sealed class CountingService : IHostedService, IDisposable
    {
        public int Stops;
        public int Disposals;
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) { Stops++; return Task.CompletedTask; }
        public void Dispose() => Disposals++;
    }

    private sealed class Connection : IDisposable
    {
        public int Disposals;
        public bool FailOnce;
        public Action? BeforeDispose;
        public void Dispose()
        {
            BeforeDispose?.Invoke();
            Interlocked.Increment(ref Disposals);
            if (FailOnce) { FailOnce = false; throw new InvalidOperationException("Controlled client disposal failure."); }
        }
    }

    private sealed class FailingStart : IHostedService, IDisposable
    {
        public int Stops;
        public int Disposals;
        public Task StartAsync(CancellationToken token) => throw new InvalidOperationException("Controlled startup failure.");
        public Task StopAsync(CancellationToken token) { Stops++; return Task.CompletedTask; }
        public void Dispose() => Disposals++;
    }
}
