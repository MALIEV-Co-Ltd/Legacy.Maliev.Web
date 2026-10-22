using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Owns one independent parent, configured child and client for one test case.</summary>
internal sealed class OwnedWebSurfaceHost : IDisposable
{
    private readonly OwnedWebSurfaceResources resources;
    private TestingWebApplicationFactory? parent;
    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;
    internal TestingWebApplicationFactory Parent => parent ?? throw new ObjectDisposedException(nameof(OwnedWebSurfaceHost));
    internal WebApplicationFactory<Program> Factory => factory ?? throw new ObjectDisposedException(nameof(OwnedWebSurfaceHost));
    internal HttpClient Client => client ?? throw new ObjectDisposedException(nameof(OwnedWebSurfaceHost));
    internal int RemainingOwnedResources => resources.Count;

    private OwnedWebSurfaceHost(OwnedWebSurfaceResources resources, TestingWebApplicationFactory parent,
        WebApplicationFactory<Program> factory, HttpClient client)
    {
        this.resources = resources;
        this.parent = parent;
        this.factory = factory;
        this.client = client;
    }

    internal static OwnedWebSurfaceHost Create(Action<IWebHostBuilder> configure)
    {
        var resources = new OwnedWebSurfaceResources();
        try
        {
            var parent = resources.Own(new OwnedWebSurfaceParent());
            var factory = resources.Own(parent.WithWebHostBuilder(configure));
            var client = resources.Own(factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            }));
            return new(resources, parent, factory, client);
        }
        catch (Exception original)
        {
            resources.ReleasePreserving(original);
            throw;
        }
    }

    public void Dispose()
    {
        try { resources.Dispose(); }
        finally { parent = null; factory = null; client = null; }
    }
}

/// <summary>Exposes the shared factory's host-start boundary for ownership controls.</summary>
internal sealed class OwnedWebSurfaceParent : TestingWebApplicationFactory
{
    internal IHost StartForOwnershipControl(IHostBuilder builder) => CreateHost(builder);
}

/// <summary>Keeps web-root cleanup outside DI, which can abort when a service disposal throws.</summary>
internal sealed class OwnedWebSurfaceHostLifetime(IHost host, OwnedWebSurfaceResources resources) : IHost
{
    public IServiceProvider Services => host.Services;
    public Task StartAsync(CancellationToken cancellationToken = default) => host.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => host.StopAsync(cancellationToken);
    public void Dispose() => resources.Dispose();
}

/// <summary>Releases only explicit application-created Testing leases, never inferred provider ownership.</summary>
internal sealed class OwnedWebSurfaceWebRoot : IDisposable
{
    private readonly HashSet<TestingWebRootLease> leases = new(ReferenceEqualityComparer.Instance);
    internal int RemainingOwnedProviders => leases.Count;

    internal OwnedWebSurfaceWebRoot(TestingWebRootLease? lease) => Capture(lease);

    internal void Capture(TestingWebRootLease? lease)
    {
        if (lease is not null) leases.Add(lease);
    }

    public void Dispose()
    {
        var owned = new OwnedWebSurfaceResources();
        foreach (var lease in leases) owned.Own(lease);
        leases.Clear();
        owned.Dispose();
    }
}

/// <summary>Attempts every owned disposal in reverse order, also after partial initialization.</summary>
internal sealed class OwnedWebSurfaceResources : IDisposable
{
    private readonly List<IDisposable> owned = [];
    internal int Count => owned.Count;

    internal T Own<T>(T resource) where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    private List<Exception> Release()
    {
        var failures = new List<Exception>();
        for (var index = owned.Count - 1; index >= 0; index--)
        {
            try { owned[index].Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        owned.Clear();
        return failures;
    }

    internal static void RecordFailureTypes(Exception original, IEnumerable<Exception> failures)
    {
        try
        {
            var types = failures.Select(failure => failure.GetType().Name).ToArray();
            if (types.Length > 0) original.Data["WebSurfaceCleanupFailureTypes"] = types;
        }
        catch (Exception) { /* Unwritable metadata must not replace the primary startup exception. */ }
    }

    internal IReadOnlyList<string> ReleasePreserving(Exception original)
    {
        var failures = Release();
        RecordFailureTypes(original, failures);
        return failures.Select(failure => failure.GetType().Name).ToArray();
    }

    public void Dispose()
    {
        var failures = Release();
        if (failures.Count > 0) throw new AggregateException("Owned WebSurface cleanup failed.", failures);
    }
}
