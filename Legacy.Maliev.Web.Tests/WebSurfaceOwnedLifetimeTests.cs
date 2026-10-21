using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

public sealed class WebSurfaceOwnedLifetimeTests
{
    [Fact]
    public void FailedStartReleasesRealWebRootEvenWhenHostedServiceDisposalThrows()
    {
        var original = new InvalidOperationException("private-startup");
        using var root = new WebRootProbe(AppContext.BaseDirectory);
        var environment = new WebRootEnvironment(root);
        using var parent = new OwnedWebSurfaceParent();
        var builder = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(environment);
            services.AddSingleton<IHostedService>(_ => new FailingStartAndDispose(root, original));
        });
        var actual = Assert.Throws<InvalidOperationException>(() => parent.StartForOwnershipControl(builder));
        Assert.Same(original, actual);
        Assert.Equal(1, root.DisposalAttempts);
        Assert.Equal(new[] { nameof(ApplicationException) }, parent.StartupCleanupFailureTypes);
        var failures = Assert.IsType<string[]>(original.Data["WebSurfaceCleanupFailureTypes"]);
        Assert.Equal(new[] { nameof(ApplicationException) }, failures);
        Assert.DoesNotContain("private", string.Join(",", failures), StringComparison.Ordinal);
    }

    private sealed class FailingStartAndDispose(
        Microsoft.Extensions.FileProviders.PhysicalFileProvider root, Exception original) : IHostedService, IDisposable
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Assert.True(root.Watch("owned-startup-control.css").ActiveChangeCallbacks);
            return Task.FromException(original);
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => throw new ApplicationException("private-disposal");
    }

    [Fact]
    public void WebRootLeaseDeduplicatesOwnedLeavesAndPreservesBorrowedProvidersAfterCleanupFailure()
    {
        using var first = new WebRootProbe(AppContext.BaseDirectory, fail: true);
        using var second = new WebRootProbe(AppContext.BaseDirectory);
        using var borrowed = new WebRootProbe(Path.GetTempPath());
        using var unknown = new BorrowedFileProvider();
        var environment = new WebRootEnvironment(new Microsoft.Extensions.FileProviders.CompositeFileProvider(
            first, new Microsoft.Extensions.FileProviders.CompositeFileProvider(first, second, borrowed, unknown)));
        using var lease = new OwnedWebSurfaceWebRoot(environment);
        Assert.Throws<AggregateException>(() => lease.Dispose());
        Assert.Equal(1, first.DisposalAttempts);
        Assert.Equal(1, second.DisposalAttempts);
        Assert.Equal(0, borrowed.DisposalAttempts);
        Assert.Equal(0, unknown.DisposalAttempts);
        lease.Dispose();
        Assert.Equal(1, first.DisposalAttempts);
        Assert.Equal(1, second.DisposalAttempts);
    }

    private sealed class WebRootEnvironment(Microsoft.Extensions.FileProviders.IFileProvider webRoot)
        : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ownership-control";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = webRoot;
    }

    private sealed class WebRootProbe(string root, bool fail = false)
        : Microsoft.Extensions.FileProviders.PhysicalFileProvider(root)
    {
        internal int DisposalAttempts { get; private set; }
        protected override void Dispose(bool disposing)
        {
            DisposalAttempts++;
            base.Dispose(disposing);
            if (fail && DisposalAttempts == 1) throw new ApplicationException("Synthetic web-root cleanup failure.");
        }
    }

    private sealed class BorrowedFileProvider : Microsoft.Extensions.FileProviders.IFileProvider, IDisposable
    {
        internal int DisposalAttempts { get; private set; }
        public Microsoft.Extensions.FileProviders.IFileInfo GetFileInfo(string path) =>
            new Microsoft.Extensions.FileProviders.NotFoundFileInfo(path);
        public Microsoft.Extensions.FileProviders.IDirectoryContents GetDirectoryContents(string path) =>
            Microsoft.Extensions.FileProviders.NotFoundDirectoryContents.Singleton;
        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) =>
            Microsoft.Extensions.FileProviders.NullChangeToken.Singleton;
        public void Dispose() => DisposalAttempts++;
    }

    [Fact]
    public async Task DerivedHostDisposalReleasesItsWebRootWatchersWithoutAffectingAnotherHost()
    {
        await using var parent = new TestingWebApplicationFactory();
        await using var first = parent.WithWebHostBuilder(_ => { });
        await using var second = parent.WithWebHostBuilder(_ => { });
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        var firstRoot = PhysicalWebRoot(first);
        var secondRoot = PhysicalWebRoot(second);
        Assert.NotSame(firstRoot, secondRoot);
        foreach (var host in new[] { first, second })
        {
            var configuration = Assert.IsAssignableFrom<IConfigurationRoot>(
                host.Services.GetRequiredService<IConfiguration>());
            var json = configuration.Providers.OfType<Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider>().ToArray();
            Assert.NotEmpty(json);
            Assert.All(json, provider => Assert.True(provider.Source.ReloadOnChange));
            var versions = host.Services.GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.IFileVersionProvider>();
            Assert.Contains("?v=", versions.AddFileVersionToPath(
                Microsoft.AspNetCore.Http.PathString.Empty, "/dist/site.min.css"), StringComparison.Ordinal);
        }
        var firstLease = first.Services.GetRequiredService<OwnedWebSurfaceWebRoot>();
        var secondLease = second.Services.GetRequiredService<OwnedWebSurfaceWebRoot>();
        Assert.Equal(1, firstLease.RemainingOwnedProviders);
        Assert.Equal(1, secondLease.RemainingOwnedProviders);
        Assert.True(firstRoot.Watch("dist/site.min.css").ActiveChangeCallbacks);
        Assert.True(secondRoot.Watch("dist/site.min.css").ActiveChangeCallbacks);
        await first.DisposeAsync();
        Assert.Equal(0, firstLease.RemainingOwnedProviders);
        Assert.Equal(1, secondLease.RemainingOwnedProviders);
        Assert.True(secondRoot.Watch("dist/still-live.css").ActiveChangeCallbacks);
        await second.DisposeAsync();
        Assert.Equal(0, secondLease.RemainingOwnedProviders);
    }

    [Fact]
    public async Task PlainFactoryPreservesRequestedKestrelLoopbackPortAndReleasesItsWebRoot()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        int port;
        try
        {
            listener.Start();
            port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally { listener.Stop(); }

        var origin = $"http://127.0.0.1:{port}";
        var factory = new TestingWebApplicationFactory();
        try
        {
            factory.UseKestrel(port);
            using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri(origin),
                AllowAutoRedirect = false,
            });
            var server = factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var addresses = Assert.IsAssignableFrom<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>(
                server.Features[typeof(Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature)]);
            Assert.Equal(origin, Assert.Single(addresses.Addresses));
            var lease = factory.Services.GetRequiredService<OwnedWebSurfaceWebRoot>();
            Assert.Equal(1, lease.RemainingOwnedProviders);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await client.GetAsync("/dist/site.min.css", deadline.Token);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            await factory.DisposeAsync();
            Assert.Equal(0, lease.RemainingOwnedProviders);
        }
        finally { await factory.DisposeAsync(); }
    }

    private static Microsoft.Extensions.FileProviders.PhysicalFileProvider PhysicalWebRoot(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var environment = host.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        IEnumerable<Microsoft.Extensions.FileProviders.PhysicalFileProvider> Leaves(Microsoft.Extensions.FileProviders.IFileProvider provider) =>
            provider is Microsoft.Extensions.FileProviders.CompositeFileProvider composite
                ? composite.FileProviders.SelectMany(Leaves)
                : provider is Microsoft.Extensions.FileProviders.PhysicalFileProvider physical ? [physical] : [];
        return Assert.Single(Leaves(environment.WebRootFileProvider), provider =>
            Path.TrimEndingDirectorySeparator(provider.Root) == Path.TrimEndingDirectorySeparator(environment.WebRootPath));
    }

    [Fact]
    public void ActualCasesOwnIndependentParentsWithOneDerivedFactoryEach()
    {
        using var first = new WebSurfaceTests();
        using var second = new WebSurfaceTests();
        Assert.NotSame(first.LifetimeOwner.Parent, second.LifetimeOwner.Parent);
        Assert.Single(first.LifetimeOwner.Parent.Factories);
        Assert.Single(second.LifetimeOwner.Parent.Factories);
        first.Dispose();
        first.Dispose();
        Assert.Equal(0, first.LifetimeOwner.RemainingOwnedResources);
        Assert.Throws<ObjectDisposedException>(() => first.LifetimeOwner.Parent);
        Assert.Throws<ObjectDisposedException>(() => first.LifetimeOwner.Factory);
        Assert.Throws<ObjectDisposedException>(() => first.LifetimeOwner.Client);
        Assert.Equal(3, second.LifetimeOwner.RemainingOwnedResources);
    }

    [Fact]
    public void PinnedSharedParentPatternRetainsBothDisposedChildren()
    {
        using var parent = new TestingWebApplicationFactory();
        var first = parent.WithWebHostBuilder(_ => { });
        var second = parent.WithWebHostBuilder(_ => { });
        first.Dispose();
        second.Dispose();
        Assert.Equal(2, parent.Factories.Count);
        Assert.Same(first, parent.Factories[0]);
        Assert.Same(second, parent.Factories[1]);
    }

    [Fact]
    public void ReverseCleanupAttemptsAllResourcesAndReportsFailureAfterward()
    {
        var order = new List<string>();
        var resources = new OwnedWebSurfaceResources();
        resources.Own(new DisposalProbe("parent", order));
        resources.Own(new DisposalProbe("child", order, fail: true));
        resources.Own(new DisposalProbe("client", order));
        Assert.Throws<AggregateException>(() => resources.Dispose());
        Assert.Equal(new[] { "client", "child", "parent" }, order);
        Assert.Equal(0, resources.Count);
        resources.Dispose();
        Assert.Equal(3, order.Count);
    }

    [Fact]
    public void PartialConstructionCleanupPreservesOriginalFailureAndRecordsCleanupTypes()
    {
        var original = new InvalidOperationException("Synthetic startup failure.");
        var order = new List<string>();
        var resources = new OwnedWebSurfaceResources();
        resources.Own(new DisposalProbe("parent", order));
        resources.Own(new DisposalProbe("child", order, fail: true));
        resources.ReleasePreserving(original);
        Assert.Equal(new[] { "child", "parent" }, order);
        Assert.Equal(0, resources.Count);
        Assert.Equal(new[] { nameof(ApplicationException) },
            Assert.IsType<string[]>(original.Data["WebSurfaceCleanupFailureTypes"]));
        var actual = Assert.Throws<InvalidOperationException>(() => OwnedWebSurfaceHost.Create(_ => throw original));
        Assert.Same(original, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuiltHostIsDisposedWhenHostedServiceStartFails(bool cleanupThrows)
    {
        var original = new InvalidOperationException("Synthetic hosted-service start failure.");
        var resource = new HostedResource(cleanupThrows);
        using var parent = new OwnedWebSurfaceParent();
        var builder = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<HostedResource>(_ => resource);
            services.AddSingleton<IHostedService>(provider =>
                new FailingStart(provider.GetRequiredService<HostedResource>(), original));
        });
        var actual = Assert.Throws<InvalidOperationException>(() => parent.StartForOwnershipControl(builder));
        Assert.Same(original, actual);
        Assert.Equal(1, resource.DisposalAttempts);
        if (cleanupThrows)
        {
            Assert.Single(parent.StartupCleanupFailureTypes);
            Assert.Single(Assert.IsType<string[]>(original.Data["WebSurfaceCleanupFailureTypes"]));
        }
        else Assert.Empty(parent.StartupCleanupFailureTypes);
    }

    [Fact]
    public void UnavailableExceptionMetadataDoesNotMaskPrimaryFailure()
    {
        var order = new List<string>();
        var resources = new OwnedWebSurfaceResources();
        resources.Own(new DisposalProbe("child", order, fail: true));
        resources.ReleasePreserving(new UnavailableMetadataException());
        Assert.Equal(new[] { "child" }, order);
        Assert.Equal(0, resources.Count);
    }

    private sealed class UnavailableMetadataException : Exception
    {
        public override IDictionary Data => throw new NotSupportedException("Synthetic inaccessible metadata.");
    }

    private sealed class HostedResource(bool cleanupThrows) : IDisposable
    {
        internal int DisposalAttempts { get; private set; }
        public void Dispose()
        {
            DisposalAttempts++;
            if (cleanupThrows) throw new ApplicationException("Synthetic host cleanup failure.");
        }
    }

    private sealed class FailingStart(HostedResource resource, Exception original) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = resource;
            return Task.FromException(original);
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class DisposalProbe(string name, List<string> order, bool fail = false) : IDisposable
    {
        public void Dispose()
        {
            order.Add(name);
            if (fail) throw new ApplicationException("Synthetic cleanup failure.");
        }
    }
}
