using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var ownedBuilder = new OwnedTestWebHostBuilder(builder);
        try
        {
            // Keep the framework's normal TestServer/Kestrel port setup and startup sequence.
            return base.CreateHost(ownedBuilder);
        }
        catch (Exception original)
        {
            try { ownedBuilder.BuiltHost?.Dispose(); }
            catch (Exception cleanup) { OwnedWebSurfaceResources.RecordFailureTypes(original, [cleanup]); }
            throw;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.TryAddSingleton<TestResponseProbeRegistry>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, TestResponseProbeStartupFilter>());
        });
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
