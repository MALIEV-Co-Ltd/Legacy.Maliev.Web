using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Captures the built test host while preserving the framework's normal host startup.</summary>
internal sealed class OwnedTestWebHostBuilder(IHostBuilder inner) : IHostBuilder
{
    internal IHost? BuiltHost { get; private set; }
    public IDictionary<object, object> Properties => inner.Properties;

    public IHost Build() => BuiltHost = new OwnedWebSurfaceBuiltHost(inner.Build());

    public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
    {
        inner.ConfigureHostConfiguration(configureDelegate);
        return this;
    }

    public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate)
    {
        inner.ConfigureAppConfiguration(configureDelegate);
        return this;
    }

    public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate)
    {
        inner.ConfigureServices(configureDelegate);
        return this;
    }

    public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory)
        where TContainerBuilder : notnull
    {
        inner.UseServiceProviderFactory(factory);
        return this;
    }

    public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(
        Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory)
        where TContainerBuilder : notnull
    {
        inner.UseServiceProviderFactory(factory);
        return this;
    }

    public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate)
    {
        inner.ConfigureContainer(configureDelegate);
        return this;
    }
}
