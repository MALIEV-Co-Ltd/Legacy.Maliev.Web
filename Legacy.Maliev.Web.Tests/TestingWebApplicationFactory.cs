using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var ownedWebRoots = new HashSet<PhysicalFileProvider>(ReferenceEqualityComparer.Instance);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, configuration) =>
        {
            // Capture this host's default leaves before derived fixtures can add borrowed providers.
            CaptureWebRoots(context.HostingEnvironment.WebRootFileProvider, ownedWebRoots);
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recaptcha:ProjectId"] = "test-project",
                ["Recaptcha:SiteKey"] = "test-site-key",
            });
        });
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(
            _ => new OwnedWebRootFiles(ownedWebRoots.ToArray())));
        if (contentRoot is not null)
        {
            builder.UseContentRoot(contentRoot);
        }
    }

    private static void CaptureWebRoots(IFileProvider provider, ISet<PhysicalFileProvider> owned)
    {
        if (provider is PhysicalFileProvider physical) owned.Add(physical);
        if (provider is CompositeFileProvider composite)
            foreach (var child in composite.FileProviders) CaptureWebRoots(child, owned);
    }

    // The host owns content-root providers, but its non-disposable web-root composite owns no lifetime.
    private sealed class OwnedWebRootFiles(PhysicalFileProvider[] providers) : IStartupFilter, IDisposable
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => next;

        public void Dispose()
        {
            Exception? failure = null;
            foreach (var provider in providers)
            {
                try { provider.Dispose(); }
                catch (Exception exception) { failure ??= exception; }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
