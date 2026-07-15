using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLegacyServiceClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ServiceEndpoints>()
            .Bind(configuration.GetSection("Services"))
            .ValidateOnStart();

        AddClient(services, "auth", static endpoints => endpoints.Auth);
        AddClient(services, "careers", static endpoints => endpoints.Career);
        AddClient(services, "catalog", static endpoints => endpoints.Catalog);
        AddClient(services, "contacts", static endpoints => endpoints.Contact);
        AddClient(services, "countries", static endpoints => endpoints.Country);
        AddClient(services, "customers", static endpoints => endpoints.Customer);
        AddClient(services, "documents", static endpoints => endpoints.Document);
        AddClient(services, "files", static endpoints => endpoints.File);
        AddClient(services, "orders", static endpoints => endpoints.Order);
        AddClient(services, "quotations", static endpoints => endpoints.Quotation);
        return services;
    }

    private static void AddClient(
        IServiceCollection services,
        string name,
        Func<ServiceEndpoints, Uri> resolveBaseAddress)
    {
        services.AddHttpClient(name, (provider, client) =>
        {
            client.BaseAddress = resolveBaseAddress(provider.GetRequiredService<IOptions<ServiceEndpoints>>().Value);
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddStandardResilienceHandler();
    }
}
