extern alias CatalogApi;
using CatalogProgram = CatalogApi::Program;
using System.Net;
using Maliev.FxProviderTransport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maliev.FxCatalogHost;

public static class NativeCatalogHost
{
    public static async Task Main()
    {
        var port = int.Parse(Required("MALIEV_FX_CATALOG_PORT"), System.Globalization.CultureInfo.InvariantCulture);
        if (port <= 1024 || port > 65535) throw new InvalidOperationException("Ephemeral loopback port required.");
        await using var factory = new Factory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        factory.StartServer();
        Console.WriteLine("Native Catalog Program ready on UUID-owned loopback graph.");
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) => { args.Cancel = true; stop.Cancel(); };
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Disposable native host input required: " + name);
    private sealed class Factory : WebApplicationFactory<CatalogProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseContentRoot(Required("MALIEV_FX_CATALOG_CONTENT_ROOT"));
            // Explicit test-host reference makes transport assembly resolution part of the built dependency graph.
            new ProviderTransportStartup().Configure(builder);
        }
    }
}
