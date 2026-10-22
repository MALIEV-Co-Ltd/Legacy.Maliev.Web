using System.Collections;
using System.Net;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

public sealed class WebSurfaceOwnedLifetimeTests
{
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAndConfiguredFactoriesDisposeOnlyTheirCreatedTestingLease(bool configured)
    {
        using var parent = new TestingWebApplicationFactory();
        using var sibling = new TestingWebApplicationFactory();
        using var application = configured ? parent.WithWebHostBuilder(_ => { }) : parent;
        var first = application.Services.GetRequiredService<TestingWebRootLease>();
        var second = sibling.Services.GetRequiredService<TestingWebRootLease>();
        Assert.NotSame(first, second);
        Assert.NotSame(first.Provider, second.Provider);
        Assert.NotNull(first.Provider.Watch("created-first-before.css"));
        Assert.NotNull(second.Provider.Watch("created-second-before.css"));
        application.Dispose();
        application.Dispose();
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.NotNull(second.Provider.Watch("created-second-still-live.css"));
        sibling.Dispose();
        Assert.True(second.IsDisposed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedStartReleasesCreatedLeaseAndPreservesPrimary(bool sharedFactory, bool cleanupThrows)
    {
        var directory = Path.Combine(Path.GetTempPath(), "created-root-654-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var environment = new WebRootEnvironment { WebRootPath = directory };
            using var lease = Assert.IsType<TestingWebRootLease>(TestingWebRootLease.Create(environment));
            Assert.Same(lease.Provider, environment.WebRootFileProvider);
            Assert.NotNull(lease.Provider.Watch("created-before-start.css"));
            var original = new InvalidOperationException("Synthetic created web-root startup failure.");
            var resource = new HostedResource(cleanupThrows);
            var builder = new HostBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<IWebHostEnvironment>(environment);
                services.AddSingleton(lease);
                services.AddSingleton<HostedResource>(_ => resource);
                services.AddSingleton<IHostedService>(serviceProvider =>
                    new FailingStart(serviceProvider.GetRequiredService<HostedResource>(), original));
            });
            using var sharedParent = new FactoryProbe();
            using var specializedParent = new OwnedWebSurfaceParent();
            var actual = Assert.Throws<InvalidOperationException>(() =>
            {
                if (sharedFactory) sharedParent.Start(builder);
                else specializedParent.StartForOwnershipControl(builder);
            });
            Assert.Same(original, actual);
            Assert.Equal(1, resource.DisposalAttempts);
            Assert.True(lease.IsDisposed);
            lease.Dispose();
            Assert.True(lease.IsDisposed);
            if (cleanupThrows)
            {
                Assert.Equal(new[] { nameof(AggregateException) },
                    Assert.IsType<string[]>(original.Data["WebSurfaceCleanupFailureTypes"]));
            }
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactBorrowedProviderSurvivesBothHostDisposalsIncludingContentRoot(bool contentRoot)
    {
        var directory = Path.Combine(Path.GetTempPath(), "borrowed-root-654-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var provider = new PhysicalWebRootProbe(directory);
        try
        {
            using var parent = new FactoryProbe();
            using var first = parent.Start(Builder());
            using var second = parent.Start(Builder());
            Assert.NotNull(provider.Watch("borrowed-before.css"));
            first.Dispose();
            Assert.Equal(0, provider.DisposalAttempts);
            Assert.NotNull(provider.Watch("borrowed-after-first.css"));
            second.Dispose();
            Assert.Equal(0, provider.DisposalAttempts);
            Assert.NotNull(provider.Watch("borrowed-after-both.css"));

            IHostBuilder Builder() => new HostBuilder().ConfigureServices(services =>
                services.AddSingleton<IWebHostEnvironment>(new WebRootEnvironment
                {
                    WebRootFileProvider = new CompositeFileProvider(provider, provider),
                    ContentRootFileProvider = contentRoot ? provider : new NullFileProvider(),
                }));
        }
        finally
        {
            provider.Dispose();
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void DiOwnedPhysicalProviderIsDisposedOnceByDiRatherThanCapturedAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "di-root-654-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var provider = new PhysicalWebRootProbe(directory);
        try
        {
            using var parent = new FactoryProbe();
            using var host = parent.Start(new HostBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<PhysicalFileProvider>(_ => provider);
                services.AddSingleton<IWebHostEnvironment>(serviceProvider => new WebRootEnvironment
                {
                    WebRootFileProvider = serviceProvider.GetRequiredService<PhysicalFileProvider>(),
                });
            }));
            Assert.Same(provider, host.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider);
            Assert.NotNull(provider.Watch("di-before.css"));
            host.Dispose();
            host.Dispose();
            Assert.Equal(1, provider.DisposalAttempts);
        }
        finally
        {
            provider.Dispose();
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", true)]
    [InlineData("Testing", false)]
    public void LeaseCreationDoesNotReplaceProvidersOutsideItsExplicitScope(string environmentName, bool hasWebRoot)
    {
        var original = new NullFileProvider();
        var environment = new WebRootEnvironment
        {
            EnvironmentName = environmentName,
            WebRootPath = hasWebRoot ? Path.Combine(Path.GetTempPath(), "not-created-654-" + Guid.NewGuid().ToString("N")) : string.Empty,
            WebRootFileProvider = original,
        };
        Assert.Null(TestingWebRootLease.Create(environment));
        Assert.Same(original, environment.WebRootFileProvider);
    }

    [Fact]
    public void LeaseResolutionFailureDisposesBuiltHostAndPreservesOriginal()
    {
        var original = new InvalidOperationException("Synthetic lease resolution failure.");
        var host = new UnresolvableHost(original);
        var actual = Assert.Throws<InvalidOperationException>(() => new OwnedWebSurfaceBuiltHost(host));
        Assert.Same(original, actual);
        Assert.Equal(1, host.DisposalAttempts);
    }

    [Fact]
    public async Task SharedFactoryPreservesKestrelDynamicLoopbackPortAndStaticResponse()
    {
        await using var factory = new TestingWebApplicationFactory();
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var features = factory.Services.GetRequiredService<IServer>().Features;
        var addresses = Assert.IsAssignableFrom<IServerAddressesFeature>(features.Get<IServerAddressesFeature>());
        var address = new Uri(Assert.Single(addresses.Addresses));
        Assert.True(address.IsLoopback);
        Assert.True(address.Port > 0);
        Assert.Equal(address.Port, client.BaseAddress?.Port);
        using var response = await client.GetAsync("/dist/route-about.css", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(timeout.Token));
    }

    private sealed class FactoryProbe : TestingWebApplicationFactory
    {
        internal IHost Start(IHostBuilder builder) => CreateHost(builder);
    }

    private sealed class PhysicalWebRootProbe(string root) : PhysicalFileProvider(root)
    {
        internal int DisposalAttempts { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposalAttempts++;
            base.Dispose(disposing);
        }
    }

    private sealed class WebRootEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = nameof(WebSurfaceOwnedLifetimeTests);
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class UnresolvableHost(Exception original) : IHost
    {
        internal int DisposalAttempts { get; private set; }
        public IServiceProvider Services => throw original;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => DisposalAttempts++;
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
