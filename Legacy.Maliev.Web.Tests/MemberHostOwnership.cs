using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Tracks exact member factories and actual host handles across startup/expiry races.</summary>
internal sealed class MemberHostOwnership
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<HostLease> hosts = [];
    private readonly List<FactoryLease> factories = [];
    private bool terminal;
    private sealed class HostLease(IHost host) : IHost
    {
        private readonly SemaphoreSlim lifecycle = new(1, 1);
        private int stopped;
        private int disposed;
        private int actualHostDisposed;
        internal Action? ReleaseCompanions { get; set; }
        public IHost Host { get; } = host;
        public IServiceProvider Services => Host.Services;
        public bool Stopped => Volatile.Read(ref stopped) != 0;
        public bool Disposed => Volatile.Read(ref disposed) != 0;
        public Task StartAsync(CancellationToken cancellationToken = default) => Host.StartAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await lifecycle.WaitAsync(cancellationToken);
            try
            {
                if (Disposed || Stopped) return;
                await Host.StopAsync(cancellationToken);
                Volatile.Write(ref stopped, 1);
            }
            finally { lifecycle.Release(); }
        }

        public void Dispose()
        {
            lifecycle.Wait();
            try
            {
                if (Disposed) return;
                if (!Stopped)
                {
                    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    Host.StopAsync(budget.Token).GetAwaiter().GetResult();
                    Volatile.Write(ref stopped, 1);
                }
                if (Volatile.Read(ref actualHostDisposed) == 0)
                {
                    Host.Dispose();
                    Volatile.Write(ref actualHostDisposed, 1);
                }
                ReleaseCompanions?.Invoke();
                Volatile.Write(ref disposed, 1);
            }
            finally { lifecycle.Release(); }
        }
    }
    private sealed class FactoryLease(WebApplicationFactory<Program> factory)
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;
        public Task? Disposal { get; set; }
        public bool Released { get; set; }
    }

    internal WebApplicationFactory<Program> RegisterFactory(Func<WebApplicationFactory<Program>> create, CancellationToken token)
    {
        gate.Wait(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (terminal) throw new ObjectDisposedException(nameof(MemberHostOwnership));
            var factory = create();
            factories.Add(new FactoryLease(factory));
            return factory;
        }
        finally { gate.Release(); }
    }

    internal IHost StartHost(Func<IHost> start, CancellationToken token)
    {
        gate.Wait(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (terminal) throw new ObjectDisposedException(nameof(MemberHostOwnership));
            var host = start();
            // Retain the actual handle before returning to WAF's cache assignment.
            hosts.Add(new HostLease(host));
            token.ThrowIfCancellationRequested();
            return host;
        }
        finally { gate.Release(); }
    }

    internal IHost BuildAndStartHost(IHostBuilder builder, CancellationToken token, Func<IHost, Action?>? captureCompanions = null)
    {
        gate.Wait(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (terminal) throw new ObjectDisposedException(nameof(MemberHostOwnership));
            var host = builder.Build();
            var owned = new HostLease(host);
            hosts.Add(owned); // Retain the actual handle before even a partially failing StartAsync.
            owned.ReleaseCompanions = captureCompanions?.Invoke(host); // Retain companions before startup can fail.
            host.StartAsync(token).GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            // Normal WAF disposal and fixture expiry must update the same exact-host ownership state.
            return owned;
        }
        finally { gate.Release(); }
    }

    internal async Task ReleaseAsync()
    {
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("Member host startup has not settled; children/containers must remain owned.");
        try
        {
            terminal = true;
            var failures = new List<Exception>();
            foreach (var owned in hosts.Where(host => !host.Disposed))
            {
                try
                {
                    if (!owned.Stopped)
                    {
                        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await owned.StopAsync(budget.Token);
                    }
                    owned.Dispose();
                }
                catch (Exception error) { failures.Add(error); }
            }
            // A completed lease must not keep its disposed service graph alive while
            // this terminal owner remains reachable. Failed leases stay available for retry.
            hosts.RemoveAll(host => host.Disposed);
            if (hosts.Any(host => !host.Disposed))
                throw new AggregateException("Member host quiescence is unverified; factories/children/containers preserved.", failures);
            foreach (var owned in factories.Where(factory => !factory.Released))
            {
                try
                {
                    // WAF disposes derived factories and created clients. The actual hosts above
                    // are independently retained so late framework cache assignment cannot escape.
                    // Reuse in-flight disposal after a wait timeout. A terminal failure,
                    // however, must permit another attempt on this same retained factory.
                    if (owned.Disposal is { IsFaulted: true } or { IsCanceled: true })
                        owned.Disposal = null;
                    owned.Disposal ??= owned.Factory.DisposeAsync().AsTask();
                    await owned.Disposal.WaitAsync(TimeSpan.FromSeconds(30));
                    owned.Released = true;
                }
                catch (Exception error) { failures.Add(error); }
            }
            factories.RemoveAll(factory => factory.Released);
            if (failures.Count != 0)
                throw new AggregateException("Member exact factory/client release incomplete; unreleased entries retained.", failures);
        }
        finally { gate.Release(); }
    }
}
