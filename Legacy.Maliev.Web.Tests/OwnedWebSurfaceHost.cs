using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

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

/// <summary>Owns the built local TestServer host even when Start throws before the factory can assign it.</summary>
internal sealed class OwnedWebSurfaceParent : TestingWebApplicationFactory
{
    internal IReadOnlyList<string> StartupCleanupFailureTypes { get; private set; } = [];

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // WebSurface uses default TestServer only; no Kestrel port/address configuration is changed.
        var host = new OwnedWebSurfaceBuiltHost(builder.Build());
        try
        {
            host.Start();
            return host;
        }
        catch (Exception original)
        {
            try { host.Dispose(); }
            catch (Exception cleanup)
            {
                StartupCleanupFailureTypes = [cleanup.GetType().Name];
                OwnedWebSurfaceResources.RecordFailureTypes(original, [cleanup]);
            }
            throw;
        }
    }

    internal IHost StartForOwnershipControl(IHostBuilder builder) => CreateHost(builder);
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

    internal void ReleasePreserving(Exception original) => RecordFailureTypes(original, Release());

    public void Dispose()
    {
        var failures = Release();
        if (failures.Count > 0) throw new AggregateException("Owned WebSurface cleanup failed.", failures);
    }
}

/// <summary>Releases physical web-root watchers after the host using them has stopped and disposed.</summary>
internal sealed class OwnedWebSurfaceBuiltHost : IHost
{
    private readonly IHost host;
    private readonly OwnedWebSurfaceResources resources = new();

    internal OwnedWebSurfaceBuiltHost(IHost host)
    {
        this.host = host;
        try
        {
            if (host.Services.GetService<IWebHostEnvironment>() is { } environment)
            {
                var seen = new HashSet<IFileProvider>(ReferenceEqualityComparer.Instance);
                Capture(environment.WebRootFileProvider, environment.ContentRootFileProvider, seen);
            }
        }
        catch (Exception original)
        {
            resources.Own(host);
            resources.ReleasePreserving(original);
            throw;
        }
        // Reverse disposal stops the host's consumers before releasing their web-root providers.
        resources.Own(host);
    }

    private void Capture(IFileProvider provider, IFileProvider contentRoot, HashSet<IFileProvider> seen)
    {
        if (!seen.Add(provider) || ReferenceEquals(provider, contentRoot)) return;
        if (provider is CompositeFileProvider composite)
        {
            foreach (var child in composite.FileProviders) Capture(child, contentRoot, seen);
        }
        else if (provider is PhysicalFileProvider physical)
        {
            // Static-assets manifests return NullChangeToken themselves. Their opaque internals
            // are not inspected; only this independent host's public physical web-root leaves are owned.
            resources.Own(physical);
        }
    }

    public IServiceProvider Services => host.Services;
    public Task StartAsync(CancellationToken cancellationToken = default) => host.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => host.StopAsync(cancellationToken);
    public void Dispose() => resources.Dispose();
}
