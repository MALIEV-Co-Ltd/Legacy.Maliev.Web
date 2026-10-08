using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

internal sealed class SyntheticProvider
{
    internal Uri Origin { get; private set; }
    internal ConcurrentQueue<object> Observations { get; } = new();
    private SyntheticProvider(Uri origin) => Origin = origin;
    internal static Task<SyntheticProvider> Start(BillingProofLifetime resources)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var host = resources.AcquireHost(builder.Build, value => value.DisposeAsync(), out var hostLease, startupRequired: true);
        // Startup and disposal are admitted and joined by the unchanged lifetime owner.

        var result = new SyntheticProvider(new Uri("http://127.0.0.1:1025"));
        host.MapPost("/sapi/search/get_suggestion", async context =>
        {
            if (context.Request.ContentLength is null or > 4096 || context.Request.Headers["X-Synthetic-Logical-Origin"] != "https://data.creden.co/sapi/search/get_suggestion")
            { context.Response.StatusCode = 400; return; }
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var root = body.RootElement;
            var query = root.GetProperty("text").GetString()!;
            var language = root.GetProperty("lang").GetString();
            if (root.GetProperty("type_search").GetString() != "prefix" || language is not ("en" or "th") || query.Length is < 2 or > 128)
            { context.Response.StatusCode = 400; return; }
            var status = query.StartsWith("RateLimited", StringComparison.Ordinal) ? 429 : query.StartsWith("Unavailable", StringComparison.Ordinal) ? 503 : 200;
            result.Observations.Enqueue(new { logicalUri = "https://data.creden.co/sapi/search/get_suggestion", physicalLoopback = true, method = "POST", typeSearch = "prefix", query, language, status });
            context.Response.StatusCode = status;
            if (status == 429) context.Response.Headers.RetryAfter = "1";
            if (status == 200) await context.Response.WriteAsJsonAsync(new { success = true, data = new { result = new[] { new { company_name = new { th = "บริษัทสังเคราะห์ จำกัด", en = "Synthetic Company Limited" }, id = "0123456789012" } } } });
        });
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        resources.StartHost(hostLease, () => host.StartAsync(startup.Token).GetAwaiter().GetResult());
        result.Origin = new Uri(host.Urls.Single());
        return Task.FromResult(result);
    }
}
