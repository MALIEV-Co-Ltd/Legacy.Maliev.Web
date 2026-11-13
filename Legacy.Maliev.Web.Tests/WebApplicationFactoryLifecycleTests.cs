using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class WebApplicationFactoryLifecycleTests
{
    private static readonly System.Text.RegularExpressions.Regex ConstructorOwnedFactoryPattern = new(
        @"public\s+\w+\s*\([^)]*factory[^)]*\)\s*\{(?:(?!\n\s*\[).)*WithWebHostBuilder",
        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex DeterministicFactoryOwnerPattern = new(
        @"class\s+\w+[^\n]*\bIDisposable\b(?s:.*)\b(?:factory|configuredFactory)\.Dispose\(\);",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    [Fact]
    public void NonInstantQuotationWebHostTests_UseAnXunitOwnedConfiguredFactory()
    {
        var testDirectory = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web.Tests");
        var violations = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("InstantQuotation", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals(
                nameof(WebApplicationFactoryLifecycleTests) + ".cs",
                StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "IClassFixture<WebApplicationFactory<Program>>",
                StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Each configured WebApplicationFactory must be the xUnit-owned class fixture so its host is disposed after the class. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void NonInstantQuotationWebHostTests_DoNotCreateAnUndisposedFactoryInTheirConstructor()
    {
        var testDirectory = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web.Tests");
        var violations = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).StartsWith("InstantQuotation", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals(
                nameof(WebApplicationFactoryLifecycleTests) + ".cs",
                StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return ConstructorOwnedFactoryPattern.IsMatch(source)
                    && !DeterministicFactoryOwnerPattern.IsMatch(source);
            })
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Do not clone a WebApplicationFactory in a per-test constructor. Put the configuration on the xUnit-owned fixture instead. Violations: {string.Join(", ", violations)}");
    }

    [Theory]
    [InlineData("interactive-route")]
    [InlineData("analytics-integration")]
    [InlineData("runtime-integration")]
    [InlineData("static-ssr-route")]
    public async Task InstantQuotationConstructorOwner_DisposesItsHostAndClientsBeforeParentFixture(string ownerKind)
    {
        await using var parent = new TestingWebApplicationFactory();
        object owner = ownerKind switch
        {
            "interactive-route" => new InstantQuotationInteractiveRouteTests(parent),
            "analytics-integration" => new InstantQuotationAnalyticsIntegrationTests(parent),
            "runtime-integration" => new InstantQuotationRuntimeIntegrationTests(parent),
            "static-ssr-route" => new InstantQuotationStaticSsrRouteTests(parent),
            _ => throw new ArgumentOutOfRangeException(nameof(ownerKind)),
        };
        IAsyncLifetime? lifetimeOwner = null;
        try
        {
            lifetimeOwner = Assert.IsAssignableFrom<IAsyncLifetime>(owner);
            await lifetimeOwner.InitializeAsync();
            var child = Assert.Single(parent.Factories);
            using var client = child.CreateClient();
            var lifetime = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(child.Services);
            Assert.False(lifetime.ApplicationStopped.IsCancellationRequested);

            await lifetimeOwner.DisposeAsync();

            Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetAsync("https://localhost/"));
        }
        finally
        {
            if (lifetimeOwner is not null) await lifetimeOwner.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenderedChild_DisposesOwnedWebRootWatcherWithoutDisposingParent(bool active)
    {
        using var parent = new WatcherModeFactory(polling: false);
        var cleanup = new HashSet<Microsoft.Extensions.FileProviders.PhysicalFileProvider>(ReferenceEqualityComparer.Instance);
        try
        {
            using var parentClient = parent.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            using var parentResponse = await parentClient.GetAsync("/?culture=en");
            Assert.Equal(System.Net.HttpStatusCode.OK, parentResponse.StatusCode);
            var parentEnvironment = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(parent.Services);
            var parentLease = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<TestingWebRootLease>(parent.Services);
            var parentLeaf = parentLease.Provider;
            Assert.Contains(parentLeaf, PhysicalLeaves(parentEnvironment.WebRootFileProvider));
            cleanup.Add(parentLeaf);
            Assert.True(parentLeaf.Watch("dist/site.min.css").ActiveChangeCallbacks);
            var parentWatcher = ActiveWatcher(parentLeaf);

            await using var child = parent.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("BlazorRouting:Home", active.ToString());
                builder.ConfigureServices((context, services) =>
                {
                    // Borrow the live parent twice after Program created this child's explicit lease.
                    var childLease = Assert.IsType<TestingWebRootLease>(Assert.Single(services,
                        descriptor => descriptor.ServiceType == typeof(TestingWebRootLease)).ImplementationInstance);
                    context.HostingEnvironment.WebRootFileProvider =
                        new Microsoft.Extensions.FileProviders.CompositeFileProvider(
                            childLease.Provider,
                            parentEnvironment.WebRootFileProvider, parentEnvironment.WebRootFileProvider);
                });
            });
            using var childClient = child.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            using var childResponse = await childClient.GetAsync("/?culture=en");
            Assert.Equal(System.Net.HttpStatusCode.OK, childResponse.StatusCode);
            var childEnvironment = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(child.Services);
            Assert.False(childEnvironment.WebRootFileProvider is IDisposable);
            var childLease = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<TestingWebRootLease>(child.Services);
            var childLeaf = Assert.Single(PhysicalLeaves(childEnvironment.WebRootFileProvider),
                provider => ReferenceEquals(provider, childLease.Provider));
            cleanup.Add(childLeaf);
            Assert.NotSame(parentLeaf, childLeaf);
            Assert.Equal(parentLeaf.Root, childLeaf.Root);
            Assert.True(childLeaf.Watch("dist/site.min.css").ActiveChangeCallbacks);
            var childWatcher = ActiveWatcher(childLeaf);
            var versioner = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.IFileVersionProvider>(child.Services);
            Assert.Contains("?v=", versioner.AddFileVersionToPath(default, "/dist/site.min.css"), StringComparison.Ordinal);
            Assert.True(childWatcher.EnableRaisingEvents);
            Assert.False(IsDisposed(childLeaf));

            await child.DisposeAsync();

            Assert.True(IsDisposed(childLeaf));
            Assert.True(childLease.IsDisposed);
            Assert.False(childWatcher.EnableRaisingEvents);
            Assert.False(IsDisposed(parentLeaf));
            Assert.False(parentLease.IsDisposed);
            Assert.True(parentWatcher.EnableRaisingEvents);
            using var stillHealthy = await parentClient.GetAsync("/?culture=th");
            Assert.Equal(System.Net.HttpStatusCode.OK, stillHealthy.StatusCode);
        }
        finally
        {
            try { await parent.DisposeAsync(); }
            finally { foreach (var provider in cleanup) provider.Dispose(); }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RenderedPollingChild_DisposesOwnedProviderWithoutDisposingParent(bool active)
    {
        await using var parent = new WatcherModeFactory(polling: true);
        using var parentClient = parent.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var parentResponse = await parentClient.GetAsync("/?culture=en");
        Assert.Equal(System.Net.HttpStatusCode.OK, parentResponse.StatusCode);
        var parentEnvironment = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(parent.Services);
        var parentLease = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<TestingWebRootLease>(parent.Services);
        AssertPollingWatcher(parentLease.Provider);
        Assert.True(parentLease.Provider.Watch("dist/site.min.css").ActiveChangeCallbacks);

        await using var child = parent.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Home", active.ToString());
            builder.ConfigureServices((context, services) =>
            {
                var owned = Assert.IsType<TestingWebRootLease>(Assert.Single(services,
                    descriptor => descriptor.ServiceType == typeof(TestingWebRootLease)).ImplementationInstance);
                context.HostingEnvironment.WebRootFileProvider =
                    new Microsoft.Extensions.FileProviders.CompositeFileProvider(
                        owned.Provider, parentEnvironment.WebRootFileProvider, parentEnvironment.WebRootFileProvider);
            });
        });
        using var childClient = child.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var childResponse = await childClient.GetAsync("/?culture=en");
        Assert.Equal(System.Net.HttpStatusCode.OK, childResponse.StatusCode);
        var childLease = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<TestingWebRootLease>(child.Services);
        Assert.NotSame(parentLease.Provider, childLease.Provider);
        Assert.Equal(parentLease.Provider.Root, childLease.Provider.Root);
        AssertPollingWatcher(childLease.Provider);
        Assert.True(childLease.Provider.Watch("dist/site.min.css").ActiveChangeCallbacks);
        var versioner = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.AspNetCore.Mvc.ViewFeatures.IFileVersionProvider>(child.Services);
        Assert.Contains("?v=", versioner.AddFileVersionToPath(default, "/dist/site.min.css"), StringComparison.Ordinal);

        await child.DisposeAsync();

        Assert.True(childLease.IsDisposed);
        Assert.True(IsDisposed(childLease.Provider));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => childClient.GetAsync("/?culture=th"));
        Assert.False(parentLease.IsDisposed);
        Assert.False(IsDisposed(parentLease.Provider));
        Assert.True(parentLease.Provider.Watch("dist/site.min.css").ActiveChangeCallbacks);
        using var stillHealthy = await parentClient.GetAsync("/?culture=th");
        Assert.Equal(System.Net.HttpStatusCode.OK, stillHealthy.StatusCode);
    }

    private sealed class WatcherModeFactory(bool polling) : TestingWebApplicationFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices((_, services) =>
            {
                // Select this test's owned provider mode before its first Watch or rendered request.
                var owned = Assert.IsType<TestingWebRootLease>(Assert.Single(services,
                    descriptor => descriptor.ServiceType == typeof(TestingWebRootLease)).ImplementationInstance);
                owned.Provider.UsePollingFileWatcher = polling;
                owned.Provider.UseActivePolling = polling;
                Assert.Equal(polling, owned.Provider.UsePollingFileWatcher);
                Assert.Equal(polling, owned.Provider.UseActivePolling);
            });
        }
    }

    private static IEnumerable<Microsoft.Extensions.FileProviders.PhysicalFileProvider> PhysicalLeaves(
        Microsoft.Extensions.FileProviders.IFileProvider provider)
    {
        if (provider is Microsoft.Extensions.FileProviders.PhysicalFileProvider physical) yield return physical;
        if (provider is Microsoft.Extensions.FileProviders.CompositeFileProvider composite)
            foreach (var child in composite.FileProviders)
                foreach (var leaf in PhysicalLeaves(child)) yield return leaf;
    }

    private static bool IsDisposed(Microsoft.Extensions.FileProviders.PhysicalFileProvider provider) =>
        (bool)typeof(Microsoft.Extensions.FileProviders.PhysicalFileProvider).GetField("_disposed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(provider)!;

    private static void AssertPollingWatcher(Microsoft.Extensions.FileProviders.PhysicalFileProvider provider)
    {
        var physicalWatcher = typeof(Microsoft.Extensions.FileProviders.PhysicalFileProvider).GetField("_fileWatcher",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(provider);
        Assert.NotNull(physicalWatcher);
        Assert.Null(physicalWatcher.GetType().GetField("_fileWatcher",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(physicalWatcher));
    }

    private static FileSystemWatcher ActiveWatcher(Microsoft.Extensions.FileProviders.PhysicalFileProvider provider)
    {
        var physicalWatcher = typeof(Microsoft.Extensions.FileProviders.PhysicalFileProvider).GetField("_fileWatcher",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(provider);
        Assert.NotNull(physicalWatcher);
        var watcher = Assert.IsType<FileSystemWatcher>(physicalWatcher.GetType().GetField("_fileWatcher",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(physicalWatcher));
        Assert.True(watcher.EnableRaisingEvents);
        return watcher;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
