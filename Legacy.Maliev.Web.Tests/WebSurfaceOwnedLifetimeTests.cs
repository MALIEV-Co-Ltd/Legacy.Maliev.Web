using System.Collections;
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
