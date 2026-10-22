using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

public sealed class TestingWebApplicationFactoryOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAndConfiguredFactoriesReleaseOnlyTheirOwnWebRootWatchers(bool configured)
    {
        using var parent = new TestingWebApplicationFactory();
        using var sibling = new TestingWebApplicationFactory();
        using var application = configured ? parent.WithWebHostBuilder(_ => { }) : parent;
        var first = PhysicalRoots(application);
        var second = PhysicalRoots(sibling);
        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.All(first, provider => Assert.DoesNotContain(provider, second));
        Activate(first);
        Activate(second);
        application.Dispose();
        AssertDisposed(first);
        Activate(second);
        sibling.Dispose();
        AssertDisposed(second);
    }

    [Theory]
    [InlineData("analytics")]
    [InlineData("runtime")]
    [InlineData("static")]
    [InlineData("interactive")]
    public void QuotationTestCasesReleaseTheirConfiguredHostBeforeTheParentFixture(string kind)
    {
        using var parent = new TestingWebApplicationFactory();
        using var testCase = kind switch
        {
            "analytics" => (IDisposable)new InstantQuotationAnalyticsIntegrationTests(parent),
            "runtime" => new InstantQuotationRuntimeIntegrationTests(parent),
            "static" => new InstantQuotationStaticSsrRouteTests(parent),
            "interactive" => new InstantQuotationInteractiveRouteTests(parent),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var child = Assert.Single(parent.Factories);
        var providers = PhysicalRoots(child);
        Assert.NotEmpty(providers);
        Activate(providers);
        testCase.Dispose();
        AssertDisposed(providers);
        Assert.Single(parent.Factories); // Parent still exists: class-end cleanup cannot satisfy this assertion.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedFactoryFailedStartPreservesOriginalAndReleasesWebRoot(bool cleanupThrows)
    {
        var directory = Path.Combine(Path.GetTempPath(), "shared-web-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var root = new PhysicalRootProbe(directory);
        try
        {
            var original = new InvalidOperationException("Synthetic shared-factory start failure.");
            var resource = new HostCleanupProbe(cleanupThrows);
            var environment = new WebRootEnvironment
            {
                WebRootFileProvider = new CompositeFileProvider(root, new CompositeFileProvider(root)),
            };
            var builder = new HostBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<IWebHostEnvironment>(environment);
                services.AddSingleton<HostCleanupProbe>(_ => resource);
                services.AddSingleton<IHostedService>(sp =>
                    new FailingStart(sp.GetRequiredService<HostCleanupProbe>(), original));
            });
            using var parent = new FactoryProbe();
            var actual = Assert.Throws<InvalidOperationException>(() => parent.Start(builder));
            Assert.Same(original, actual);
            Assert.Equal(1, resource.DisposalAttempts);
            Assert.Equal(1, root.DisposalAttempts);
        }
        finally
        {
            root.Dispose();
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void WrapperConstructionFailureDisposesBuiltHostAndPreservesOriginal()
    {
        var original = new InvalidOperationException("Synthetic environment resolution failure.");
        var host = new UnresolvableHost(original);
        var actual = Assert.Throws<InvalidOperationException>(() => new OwnedWebSurfaceBuiltHost(host));
        Assert.Same(original, actual);
        Assert.Equal(1, host.DisposalAttempts);
    }

    [Fact]
    public async Task KestrelDynamicPortAndActualStaticFileResponseRemainAvailable()
    {
        await using var factory = new TestingWebApplicationFactory();
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var features = factory.Services.GetRequiredService<IServer>().Features;
        var addresses = Assert.IsAssignableFrom<IServerAddressesFeature>(features.Get<IServerAddressesFeature>());
        var address = new Uri(Assert.Single(addresses.Addresses));
        Assert.True(address.IsLoopback);
        Assert.True(address.Port > 0);
        Assert.Equal(address.Port, client.BaseAddress?.Port);
        using var response = await client.GetAsync("/dist/route-about.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    private static PhysicalFileProvider[] PhysicalRoots(WebApplicationFactory<Program> factory)
    {
        var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        return Leaves(environment.WebRootFileProvider).OfType<PhysicalFileProvider>()
            .Where(provider => !ReferenceEquals(provider, environment.ContentRootFileProvider))
            .Distinct().ToArray();

        static IEnumerable<IFileProvider> Leaves(IFileProvider provider) =>
            provider is CompositeFileProvider composite
                ? composite.FileProviders.SelectMany(Leaves)
                : [provider];
    }

    private static void Activate(IEnumerable<PhysicalFileProvider> providers) =>
        Assert.All(providers, provider => Assert.NotNull(provider.Watch("shared-owner-probe.css")));

    private static void AssertDisposed(IEnumerable<PhysicalFileProvider> providers) =>
        Assert.All(providers, provider =>
            Assert.Throws<ObjectDisposedException>(() => provider.Watch("shared-owner-after.css")));

    private sealed class FactoryProbe : TestingWebApplicationFactory
    {
        internal IHost Start(IHostBuilder builder) => CreateHost(builder);
    }

    private sealed class PhysicalRootProbe(string root) : PhysicalFileProvider(root)
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
        public string ApplicationName { get; set; } = nameof(TestingWebApplicationFactoryOwnershipTests);
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class HostCleanupProbe(bool cleanupThrows) : IDisposable
    {
        internal int DisposalAttempts { get; private set; }
        public void Dispose()
        {
            DisposalAttempts++;
            if (cleanupThrows) throw new ApplicationException("Synthetic host disposal failure.");
        }
    }

    private sealed class FailingStart(HostCleanupProbe resource, Exception original) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = resource;
            return Task.FromException(original);
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnresolvableHost(Exception original) : IHost
    {
        internal int DisposalAttempts { get; private set; }
        public IServiceProvider Services => throw original;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => DisposalAttempts++;
    }
}
