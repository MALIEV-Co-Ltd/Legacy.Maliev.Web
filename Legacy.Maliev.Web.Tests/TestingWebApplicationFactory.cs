using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? contentRoot;

    public TestingWebApplicationFactory()
    {
    }

    internal TestingWebApplicationFactory(string contentRoot)
    {
        this.contentRoot = contentRoot;
    }

    internal IReadOnlyList<string> StartupCleanupFailureTypes { get; private set; } = [];

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var resources = new OwnedWebSurfaceResources();
        var webRoot = resources.Own(new OwnedWebSurfaceWebRoot(null));
        // Register the externally owned instance only for lifetime diagnostics; DI does not dispose it.
        builder.ConfigureServices(services => services.AddSingleton(webRoot));
        try
        {
            // Capture after Build, before base CreateHost applies its Kestrel port policy and starts.
            var host = base.CreateHost(new OwnershipCapturingHostBuilder(builder, built =>
            {
                resources.Own(built);
                webRoot.Capture(built.Services.GetService<IWebHostEnvironment>());
            }));
            return new OwnedWebSurfaceHostLifetime(host, resources);
        }
        catch (Exception original)
        {
            StartupCleanupFailureTypes = resources.ReleasePreserving(original);
            throw;
        }
    }

    private sealed class OwnershipCapturingHostBuilder(IHostBuilder builder, Action<IHost> capture) : IHostBuilder
    {
        public IDictionary<object, object> Properties => builder.Properties;

        public IHost Build()
        {
            var host = builder.Build();
            capture(host);
            return host;
        }

        public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
        {
            builder.ConfigureHostConfiguration(configureDelegate);
            return this;
        }

        public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate)
        {
            builder.ConfigureAppConfiguration(configureDelegate);
            return this;
        }

        public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate)
        {
            builder.ConfigureServices(configureDelegate);
            return this;
        }

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory)
            where TContainerBuilder : notnull
        {
            builder.UseServiceProviderFactory(factory);
            return this;
        }

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(
            Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory) where TContainerBuilder : notnull
        {
            builder.UseServiceProviderFactory(factory);
            return this;
        }

        public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate)
        {
            builder.ConfigureContainer(configureDelegate);
            return this;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Recaptcha:ProjectId"] = "test-project",
                ["Recaptcha:SiteKey"] = "test-site-key",
            }));
        if (contentRoot is not null)
        {
            builder.UseContentRoot(contentRoot);
        }
    }
}
