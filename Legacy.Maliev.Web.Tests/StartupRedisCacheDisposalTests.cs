using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class StartupRedisCacheDisposalTests
{
    [Fact]
    public async Task ConcurrentProviderDisposal_JoinsActualCacheBeforeCreationOwnedCleanup()
    {
        var events = new ConcurrentQueue<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = ConfiguredServices();
        services.AddSingleton(provider => new BlockingCache(
            provider.GetRequiredService<IOptions<RedisCacheOptions>>(), entered, release, events));
        var disposal = new StartupRedisCacheDisposal();
        disposal.Register(services);
        var provider = services.BuildServiceProvider();
        var resources = new OwnedWebSurfaceResources();
        resources.Own(new CallbackDisposable(() => events.Enqueue("certificate")));
        resources.Own(new CallbackDisposable(() => events.Enqueue("redis")));
        Task? firstDispose = null;
        Task? cleanupTask = null;
        Exception? primary = null;
        try
        {
            _ = provider.GetRequiredService<BlockingCache>();
            Assert.Equal(1, disposal.CapturedCount);
            Assert.False(disposal.AllCapturedCompleted);
            firstDispose = Task.Run(provider.Dispose);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var secondReturned = false;
            var hostDisposals = 0;
            cleanupTask = disposal.ReleaseAsync(() =>
            {
                provider.Dispose();
                hostDisposals++;
                secondReturned = true;
                return ValueTask.CompletedTask;
            }, resources).AsTask();
            Assert.True(secondReturned);
            var concurrent = disposal.ReleaseAsync(() =>
            {
                hostDisposals++;
                return ValueTask.CompletedTask;
            }, resources).AsTask();
            Assert.Same(cleanupTask, concurrent);
            Assert.False(cleanupTask.IsCompleted);
            Assert.Empty(events);
            Assert.Equal(2, resources.Count);
            release.TrySetResult();
            await firstDispose.WaitAsync(TimeSpan.FromSeconds(5));
            await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(disposal.AllCapturedCompleted);
            Assert.Equal(new[] { "cache", "redis", "certificate" }, events.ToArray());
            Assert.Equal(0, resources.Count);
            Assert.Equal(1, hostDisposals);
        }
        catch (Exception original)
        {
            primary = original;
            throw;
        }
        finally
        {
            release.TrySetResult();
            var failures = new List<Exception>();
            try { if (firstDispose is not null) await firstDispose.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception failure) { failures.Add(failure); }
            try { if (cleanupTask is not null) await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception failure) { failures.Add(failure); }
            try { provider.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
            if (cleanupTask is null || cleanupTask.IsCompleted)
            {
                try { resources.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            else failures.Add(new InvalidOperationException("Shared cleanup is still owned; concurrent fallback disposal refused."));
            if (primary is not null) OwnedWebSurfaceResources.RecordFailureTypes(primary, failures);
            else if (failures.Count > 0) throw new AggregateException("Synthetic cache cleanup failed.", failures);
        }
    }

    [Fact]
    public async Task Registration_RetainsOriginalConfigIdentityOrderLifetimeAndCacheDescriptor()
    {
        var configured = 0;
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            configured++;
            options.InstanceName = "original:";
        });
        var config = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<RedisCacheOptions>));
        var cache = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        var index = services.IndexOf(config);
        var original = Assert.IsType<ConfigureNamedOptions<RedisCacheOptions>>(config.ImplementationInstance);
        Assert.Equal(Options.DefaultName, original.Name);
        var disposal = new StartupRedisCacheDisposal();
        disposal.Register(services);
        Assert.Equal(config.Lifetime, services[index].Lifetime);
        Assert.Same(cache, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache)));
        using (var provider = services.BuildServiceProvider())
        {
            Assert.Same(original, provider.GetRequiredService<IConfigureOptions<RedisCacheOptions>>());
            Assert.Equal("original:", provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.InstanceName);
            Assert.Equal(1, configured);
            Assert.Equal(1, disposal.CapturedCount);
            Assert.False(disposal.AllCapturedCompleted);
        }
        await disposal.JoinAsync();
        Assert.True(disposal.AllCapturedCompleted);
    }

    [Fact]
    public async Task IndependentProviders_FirstCompletedWitnessCannotFenceSecondProvider()
    {
        var services = ConfiguredServices();
        var disposal = new StartupRedisCacheDisposal();
        disposal.Register(services);
        var first = services.BuildServiceProvider();
        var second = services.BuildServiceProvider();
        try
        {
            _ = first.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
            _ = second.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
            Assert.Equal(2, disposal.CapturedCount);
            first.Dispose();
            Assert.False(disposal.AllCapturedCompleted);
            var join = disposal.JoinAsync();
            Assert.False(join.IsCompleted);
            second.Dispose();
            await join.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(disposal.AllCapturedCompleted);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public async Task UnusedProvider_DoesNotConstructOptionsCacheOrWitnessDuringCleanup()
    {
        var configured = 0;
        var services = new ServiceCollection();
        services.Configure<RedisCacheOptions>(_ => configured++);
        var disposal = new StartupRedisCacheDisposal();
        disposal.Register(services);
        using var provider = services.BuildServiceProvider();
        using var resources = new OwnedWebSurfaceResources();
        var released = false;
        resources.Own(new CallbackDisposable(() => released = true));
        await disposal.ReleaseAsync(() => { provider.Dispose(); return ValueTask.CompletedTask; }, resources);
        Assert.True(released);
        Assert.Equal(0, configured);
        Assert.Equal(0, disposal.CapturedCount);
        Assert.False(disposal.AllCapturedCompleted);
        Assert.Equal(0, resources.Count);
    }

    [Fact]
    public async Task SealedRegistry_RejectsAndRecordsLateProviderCapture()
    {
        var services = ConfiguredServices();
        var disposal = new StartupRedisCacheDisposal();
        disposal.Register(services);
        using var first = services.BuildServiceProvider();
        _ = first.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        first.Dispose();
        await disposal.JoinAsync();
        using var late = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => late.GetRequiredService<IOptions<RedisCacheOptions>>().Value);
        Assert.Equal(1, disposal.CapturedCount);
        Assert.Equal(1, disposal.LateCaptureCount);
        Assert.False(disposal.AllCapturedCompleted);
        var unmodified = ConfiguredServices();
        var descriptors = unmodified.ToArray();
        Assert.Throws<InvalidOperationException>(() => disposal.Register(unmodified));
        Assert.Equal(descriptors, unmodified.ToArray());
    }

    [Fact]
    public async Task PartialStartupFailure_PreservesPrimaryAndAttemptsEveryOwnedCleanup()
    {
        var services = ConfiguredServices();
        var disposal = new StartupRedisCacheDisposal();
        disposal.Register(services);
        using var provider = services.BuildServiceProvider();
        using var resources = new OwnedWebSurfaceResources();
        var released = new List<int>();
        resources.Own(new CallbackDisposable(() => released.Add(1)));
        resources.Own(new CallbackDisposable(() => throw new IOException("synthetic cleanup failure")));
        resources.Own(new CallbackDisposable(() => released.Add(3)));
        var primary = new InvalidOperationException("synthetic startup failure");
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            disposal.ReleaseAsync(() => ValueTask.FromException(primary), resources).AsTask());
        Assert.Same(primary, observed);
        Assert.Equal(new[] { 3, 1 }, released);
        Assert.Contains(nameof(IOException), Assert.IsType<string[]>(primary.Data["WebSurfaceCleanupFailureTypes"]));
        Assert.Equal(0, resources.Count);
        Assert.Equal(0, disposal.CapturedCount);
    }

    [Fact]
    public async Task JoinTimeout_PreservesHostFailureAndStillAttemptsEveryOwnedCleanup()
    {
        var services = ConfiguredServices();
        var disposal = new StartupRedisCacheDisposal();
        disposal.Register(services);
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        using var resources = new OwnedWebSurfaceResources();
        var released = false;
        resources.Own(new CallbackDisposable(() => released = true));
        resources.Own(new CallbackDisposable(() => throw new IOException("synthetic cleanup failure")));
        var primary = new InvalidOperationException("synthetic host cleanup failure");
        try
        {
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                disposal.ReleaseAsync(() => ValueTask.FromException(primary), resources).AsTask());
            Assert.Same(primary, observed);
            var failures = Assert.IsType<string[]>(primary.Data["WebSurfaceCleanupFailureTypes"]);
            Assert.Contains(nameof(TimeoutException), failures);
            Assert.Contains(nameof(IOException), failures);
            Assert.True(released);
            Assert.Equal(0, resources.Count);
            Assert.False(disposal.AllCapturedCompleted);
        }
        finally
        {
            provider.Dispose();
            resources.Dispose();
        }
        await disposal.JoinAsync();
        Assert.True(disposal.AllCapturedCompleted);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("scoped")]
    [InlineData("factory")]
    [InlineData("named")]
    [InlineData("disposable")]
    [InlineData("unsupported")]
    [InlineData("keyed")]
    public void UnsupportedRegistration_IsRefusedBeforeMutatingDescriptors(string defect)
    {
        var services = ConfiguredServices();
        var config = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<RedisCacheOptions>));
        var index = services.IndexOf(config);
        var original = Assert.IsType<ConfigureNamedOptions<RedisCacheOptions>>(config.ImplementationInstance);
        switch (defect)
        {
            case "missing": services.Remove(config); break;
            case "duplicate": services.Configure<RedisCacheOptions>(_ => { }); break;
            case "scoped": services[index] = ServiceDescriptor.Scoped<IConfigureOptions<RedisCacheOptions>>(_ => original); break;
            case "factory": services[index] = ServiceDescriptor.Singleton<IConfigureOptions<RedisCacheOptions>>(_ => original); break;
            case "named": services[index] = ServiceDescriptor.Singleton<IConfigureOptions<RedisCacheOptions>>(new ConfigureNamedOptions<RedisCacheOptions>("other", _ => { })); break;
            case "disposable": services[index] = ServiceDescriptor.Singleton<IConfigureOptions<RedisCacheOptions>>(new DisposableConfiguration()); break;
            case "unsupported": services[index] = ServiceDescriptor.Singleton<IConfigureOptions<RedisCacheOptions>>(new OtherConfiguration()); break;
            case "keyed": services[index] = ServiceDescriptor.KeyedSingleton<IConfigureOptions<RedisCacheOptions>>("synthetic", original); break;
        }
        var before = services.ToArray();
        var disposal = new StartupRedisCacheDisposal();
        Assert.Throws<InvalidOperationException>(() => disposal.Register(services));
        Assert.Equal(before, services.ToArray());
        Assert.Equal(0, disposal.CapturedCount);
    }

    private static ServiceCollection ConfiguredServices()
    {
        var services = new ServiceCollection();
        services.Configure<RedisCacheOptions>(options => options.InstanceName = "original:");
        return services;
    }

    private sealed class BlockingCache : IDisposable
    {
        private readonly TaskCompletionSource entered;
        private readonly TaskCompletionSource release;
        private readonly ConcurrentQueue<string> events;

        internal BlockingCache(IOptions<RedisCacheOptions> options, TaskCompletionSource entered,
            TaskCompletionSource release, ConcurrentQueue<string> events)
        {
            Assert.Equal("original:", options.Value.InstanceName);
            this.entered = entered;
            this.release = release;
            this.events = events;
        }

        public void Dispose()
        {
            entered.TrySetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            events.Enqueue("cache");
        }
    }

    private sealed class CallbackDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private sealed class DisposableConfiguration() : ConfigureNamedOptions<RedisCacheOptions>(Options.DefaultName, _ => { }), IDisposable
    {
        public void Dispose() { }
    }

    private sealed class OtherConfiguration : IConfigureOptions<RedisCacheOptions>
    {
        public void Configure(RedisCacheOptions options) { }
    }
}
