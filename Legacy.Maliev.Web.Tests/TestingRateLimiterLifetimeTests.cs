using System.Reflection;
using System.Runtime.CompilerServices;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Threading.RateLimiting;
using System.Net;

namespace Legacy.Maliev.Web.Tests;

public sealed class TestingRateLimiterLifetimeTests
{
    private static object? retainedControl;

    [Fact]
    public void FrameworkEndpointIsOwnedAndBorrowedGlobalIsNotDisposed()
    {
        using var global = new ProbeLimiter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiter(options => options.GlobalLimiter = global);
        services.AddSingleton(_ => new TestingRateLimiterLifetime());
        using var provider = services.BuildServiceProvider();
        var owner = provider.GetRequiredService<TestingRateLimiterLifetime>();
        var application = new ApplicationBuilder(provider);
        owner.Use(application);
        application.Run(_ => Task.CompletedTask);
        var pipeline = application.Build();
        Assert.Equal("Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware", pipeline.Target!.GetType().FullName);
        var field = TestingRateLimiterLifetime.ValidateMiddleware(pipeline.Target.GetType());
        var endpoint = (PartitionedRateLimiter<HttpContext>)field.GetValue(pipeline.Target)!;
        Assert.False(TimerTask(endpoint).IsCompleted);
        provider.Dispose();
        Assert.True(TimerTask(endpoint).IsCompleted);
        Assert.Equal(0, global.Attempts);
        owner.Dispose();
        Assert.Equal(0, global.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReentryAndFailureReleaseEndpointExactlyOnce(bool fail)
    {
        using var owner = new TestingRateLimiterLifetime();
        var primary = new InvalidOperationException("owned disposal control");
        var endpoint = new ProbeLimiter { OnDispose = () => { owner.Dispose(); if (fail) throw primary; } };
        owner.Attach(endpoint);
        if (fail) Assert.Same(primary, Assert.Throws<InvalidOperationException>(owner.Dispose));
        else owner.Dispose();
        owner.Dispose();
        Assert.Equal(1, endpoint.Attempts);
    }

    [Fact]
    public void ConcurrentDisposeWaitsForEndpointCompletion()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        using var owner = new TestingRateLimiterLifetime();
        var endpoint = new ProbeLimiter { OnDispose = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); } };
        owner.Attach(endpoint);
        Exception? firstFailure = null;
        Exception? secondFailure = null;
        var first = new Thread(() => { try { owner.Dispose(); } catch (Exception error) { firstFailure = error; } });
        var second = new Thread(() => { try { owner.Dispose(); } catch (Exception error) { secondFailure = error; } finally { returned.Set(); } });
        var secondStarted = false;
        try
        {
            first.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            second.Start();
            secondStarted = true;
            Assert.True(SpinWait.SpinUntil(() => returned.IsSet || (second.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            Assert.False(returned.IsSet);
        }
        finally
        {
            release.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)));
            if (secondStarted) Assert.True(second.Join(TimeSpan.FromSeconds(5)));
        }
        Assert.True(returned.IsSet);
        Assert.Null(firstFailure);
        Assert.Null(secondFailure);
        Assert.Equal(1, endpoint.Attempts);
    }

    [Fact]
    public void PipelineStartupFailureStillDisposesCapturedEndpoint()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiter(_ => { });
        services.AddSingleton(_ => new TestingRateLimiterLifetime());
        using var provider = services.BuildServiceProvider();
        var owner = provider.GetRequiredService<TestingRateLimiterLifetime>();
        var application = new ApplicationBuilder(provider);
        var primary = new InvalidOperationException("pipeline construction control");
        application.Use(_ => throw primary);
        owner.Use(application);
        application.Run(_ => Task.CompletedTask);
        Assert.Same(primary, Assert.Throws<InvalidOperationException>(() => application.Build()));
        var endpoint = (PartitionedRateLimiter<HttpContext>)typeof(TestingRateLimiterLifetime)
            .GetField("endpoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        Assert.False(TimerTask(endpoint).IsCompleted);
        provider.Dispose();
        Assert.True(TimerTask(endpoint).IsCompleted);
    }

    [Fact]
    public void RejectedCreatedDelegateReleasesItsExactEndpoint()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiter(_ => { });
        using var provider = services.BuildServiceProvider();
        var original = new ApplicationBuilder(provider);
        original.UseRateLimiter();
        original.Run(_ => Task.CompletedTask);
        var pipeline = original.Build();
        var middleware = pipeline.Target!.GetType();
        var field = TestingRateLimiterLifetime.ValidateMiddleware(middleware);
        var endpoint = (PartitionedRateLimiter<HttpContext>)field.GetValue(pipeline.Target)!;
        using var owner = new TestingRateLimiterLifetime();
        try
        {
            var proxyType = typeof(TestingRateLimiterLifetime).GetNestedType("CreationSite", BindingFlags.NonPublic)!;
            var proxy = (IApplicationBuilder)Activator.CreateInstance(proxyType,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                [new ApplicationBuilder(provider), owner, middleware, field], null)!;
            proxy.Use(_ => pipeline + pipeline);
            Assert.Throws<InvalidOperationException>(() => proxy.Build());
            Assert.True(TimerTask(endpoint).IsCompleted);
        }
        finally { endpoint.Dispose(); }
    }

    [Fact]
    public void RejectedAdoptionCleansOnlyNewEndpointAndPreservesPrimaryFailure()
    {
        using var owner = new TestingRateLimiterLifetime();
        var original = new ProbeLimiter();
        owner.Attach(original);
        Assert.Throws<InvalidOperationException>(() => owner.Attach(original));
        Assert.Equal(0, original.Attempts);
        var rejected = new ProbeLimiter { OnDispose = () => throw new IOException("cleanup control") };
        var primary = Assert.Throws<InvalidOperationException>(() => owner.Attach(rejected));
        Assert.Equal(typeof(IOException).FullName, primary.Data["EndpointCleanupFailureType"]);
        Assert.Equal(1, rejected.Attempts);
        owner.Dispose();
        var late = new ProbeLimiter();
        Assert.Throws<InvalidOperationException>(() => owner.Attach(late));
        Assert.Equal(1, late.Attempts);
        Assert.Equal(1, original.Attempts);
    }

    [Fact]
    public void UnsupportedShapeFailsBeforeAnyEndpointExists()
    {
        Assert.Throws<InvalidOperationException>(() => TestingRateLimiterLifetime.ValidateMiddleware(typeof(ProbeLimiter)));
    }

    [Fact]
    public void DiDisposesLaterHostConsumersBeforeEndpoint()
    {
        var endpoint = new ProbeLimiter();
        var consumerDisposed = false;
        var services = new ServiceCollection();
        services.AddSingleton(_ => new TestingRateLimiterLifetime());
        services.AddSingleton(_ => new DisposalConsumer(() => { Assert.Equal(0, endpoint.Attempts); consumerDisposed = true; }));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TestingRateLimiterLifetime>().Attach(endpoint);
        _ = provider.GetRequiredService<DisposalConsumer>();
        provider.Dispose();
        Assert.True(consumerDisposed);
        Assert.Equal(1, endpoint.Attempts);
    }

    [Fact]
    public async Task ActualAccountPolicyRetainsThirtyRequestIpPartitionAndRejection()
    {
        using var owner = OwnedWebSurfaceHost.Create(_ => { });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<int> Send(string path, string address)
        {
            var result = await owner.Factory.Server.SendAsync(context =>
            {
                context.Request.Method = HttpMethods.Get;
                context.Request.Scheme = "https";
                context.Request.Host = new HostString("localhost");
                context.Request.Path = path;
                context.Request.QueryString = new QueryString("?culture=en");
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            }, deadline.Token);
            return result.Response.StatusCode;
        }
        // Missing confirmation parameters are rejected by the page without any account-service call.
        for (var index = 0; index < 30; index++) Assert.Equal(400, await Send("/Account/EmailConfirmation", "192.0.2.1"));
        Assert.Equal(429, await Send("/Account/ChangeEmailConfirmation", "192.0.2.1"));
        Assert.Equal(400, await Send("/Account/EmailConfirmation", "192.0.2.2"));
        Assert.Equal(200, await Send("/about", "192.0.2.1"));
    }

    private static Task TimerTask(PartitionedRateLimiter<HttpContext> endpoint) =>
        (Task)endpoint.GetType().GetField("_timerTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(endpoint)!;

    private sealed class DisposalConsumer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class ProbeLimiter : PartitionedRateLimiter<HttpContext>
    {
        internal int Attempts { get; private set; }
        internal Action? OnDispose { get; init; }
        public override RateLimiterStatistics? GetStatistics(HttpContext resource) => null;
        protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount) => throw new NotSupportedException();
        protected override ValueTask<RateLimitLease> AcquireAsyncCore(HttpContext resource, int permitCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { Attempts++; OnDispose?.Invoke(); } }
    }

    [Fact]
    public void DisposedIndependentApplicationsReleaseAllOwnedHostTargets()
    {
        // Run this regression alone in a fresh test process: isolate the separate first-use SDK worker root.
        if (ExecutionContext.IsFlowSuppressed()) RuntimeHelpers.RunClassConstructor(typeof(OpenTelemetry.Sdk).TypeHandle);
        else
        {
            using (ExecutionContext.SuppressFlow()) RuntimeHelpers.RunClassConstructor(typeof(OpenTelemetry.Sdk).TypeHandle);
        }
        var controls = Controls();
        Collect();
        Assert.False(controls[0].IsAlive);
        Assert.True(controls[1].IsAlive);
        ReleaseControl();
        Collect();
        Assert.False(controls[1].IsAlive);

        var cohorts = Enumerable.Range(0, 3).Select(_ => Task.Run(RenderAndDispose).GetAwaiter().GetResult()).ToArray();
        for (var round = 0; round < 3; round++) Collect();
        Assert.All(cohorts, cohort => Assert.All(cohort, target => Assert.False(target.IsAlive)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RenderAndDispose()
    {
        using var owner = OwnedWebSurfaceHost.Create(_ => { });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = owner.Client.GetAsync("/about?culture=en", deadline.Token).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var parent = owner.Parent;
        var child = owner.Factory;
        var host = typeof(WebApplicationFactory<Program>).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(child)!;
        var services = child.Services;
        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        var lease = services.GetRequiredService<TestingWebRootLease>();
        var targets = new object[] { parent, child, host, services, lease, lease.Provider };
        var weak = targets.Select(target => new WeakReference(target)).ToArray();
        owner.Dispose();
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
        Assert.True(lease.IsDisposed);
        Assert.Equal(0, owner.RemainingOwnedResources);
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] Controls()
    {
        retainedControl = new object();
        return [new(new object()), new(retainedControl)];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleaseControl() => retainedControl = null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }
}
