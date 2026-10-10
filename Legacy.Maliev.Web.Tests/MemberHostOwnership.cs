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
    private sealed class HostLease(IHost host)
    {
        public IHost Host { get; } = host;
        public bool Stopped { get; set; }
        public bool Disposed { get; set; }
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

    internal IHost BuildAndStartHost(IHostBuilder builder, CancellationToken token)
    {
        gate.Wait(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (terminal) throw new ObjectDisposedException(nameof(MemberHostOwnership));
            var host = builder.Build();
            hosts.Add(new HostLease(host)); // Retain before even a partially failing StartAsync.
            host.StartAsync(token).GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            return host;
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
                        await owned.Host.StopAsync(budget.Token);
                        owned.Stopped = true;
                    }
                    owned.Host.Dispose();
                    owned.Disposed = true;
                }
                catch (Exception error) { failures.Add(error); }
            }
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
            if (failures.Count != 0)
                throw new AggregateException("Member exact factory/client release incomplete; unreleased entries retained.", failures);
        }
        finally { gate.Release(); }
    }
}
