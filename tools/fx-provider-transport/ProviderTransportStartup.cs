using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

[assembly: HostingStartup(typeof(Maliev.FxProviderTransport.ProviderTransportStartup))]

namespace Maliev.FxProviderTransport;

// Test assembly loaded only into the disposable native Catalog process.
// Normal Catalog Program, client, resilience, bearer validation and permission handler remain intact.
public sealed class ProviderTransportStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        var run = Environment.GetEnvironmentVariable("MALIEV_FX_RUN_ID");
        var configured = Environment.GetEnvironmentVariable("MALIEV_FX_PROVIDER_ORIGIN");
        if (!Guid.TryParse(run, out _) || !Uri.TryCreate(configured, UriKind.Absolute, out var fixture)
            || fixture.Scheme != "http" || fixture.Host != "127.0.0.1" || fixture.Port <= 1024
            || fixture.AbsolutePath != "/" || fixture.UserInfo.Length != 0
            || fixture.Query.Length != 0 || fixture.Fragment.Length != 0)
            throw new InvalidOperationException("UUID-owned disposable loopback provider required.");
        builder.ConfigureServices(services => services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(handler =>
            {
                if (handler.Name == "IExchangeRateClient")
                    handler.PrimaryHandler = new ProviderRoute(fixture, run!);
            })));
    }

    private sealed class ProviderRoute(Uri fixture, string run) : DelegatingHandler(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
    })
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var original = request.RequestUri;
            if (request.Method != HttpMethod.Get || original is null
                || original.Scheme != "https" || original.Host != "api.frankfurter.app"
                || original.AbsolutePath != "/latest" || request.Headers.Authorization is not null
                || original.UserInfo.Length != 0)
                throw new InvalidOperationException("Unexpected FX upstream request.");
            request.RequestUri = new Uri(fixture, "runs/" + run + original.PathAndQuery);
            // This marker is a provider-fixture isolation key, never a service authorization grant.
            request.Headers.Add("X-Fx-Fixture-Run", run);
            try { return await base.SendAsync(request, cancellationToken); }
            finally
            {
                // Preserve the original request for the unchanged outer resilience retry pipeline.
                request.RequestUri = original;
                request.Headers.Remove("X-Fx-Fixture-Run");
            }
        }
    }
}
