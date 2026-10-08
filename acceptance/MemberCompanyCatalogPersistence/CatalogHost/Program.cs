extern alias Catalog;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using CatalogProgram = Catalog::Program;

if (args.SequenceEqual(new[] { "--transport-controls" }))
{
    await TransportControls.Run();
    return;
}

var origin = new Uri(Environment.GetEnvironmentVariable("MALIEV_COMPANY_CATALOG_ORIGIN") ?? throw new InvalidOperationException("Owned Catalog origin required."));
var upstream = new Uri(Environment.GetEnvironmentVariable("MALIEV_COMPANY_UPSTREAM_ORIGIN") ?? throw new InvalidOperationException("Owned synthetic upstream required."));
if (origin.Scheme != "http" || origin.Host != "127.0.0.1" || origin.Port <= 1024 || origin.AbsolutePath != "/") throw new InvalidOperationException("Owned Catalog loopback required.");
await using var factory = new CatalogFactory(upstream);
factory.UseKestrel(options => options.Listen(IPAddress.Loopback, origin.Port));
factory.StartServer();
await Task.Delay(Timeout.InfiniteTimeSpan);

internal sealed class CatalogFactory(Uri upstream) : WebApplicationFactory<CatalogProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Environment.GetEnvironmentVariable("MALIEV_COMPANY_CATALOG_CONTENT_ROOT") ?? throw new InvalidOperationException("Pinned Catalog content root required."));
        builder.UseEnvironment("Development");
        builder.UseSetting("Creden:Enabled", "true");
        builder.UseSetting("Creden:BaseUrl", "https://data.creden.co/");
        builder.UseSetting("Creden:AccessReviewReference", "Synthetic disposable acceptance transport only; no live provider enablement");
        builder.ConfigureServices(services => services.AddHttpClient("catalog-creden").ConfigurePrimaryHttpMessageHandler(() => new OwnedSyntheticTransport(upstream)));
    }
}

/// <summary>Preserves the actual adapter logical HTTPS request, but its only physical transport is a bounded owned synthetic socket.</summary>
internal sealed class OwnedSyntheticTransport : HttpMessageHandler
{
    private readonly Uri upstream;
    private readonly HttpClient transport;
    internal OwnedSyntheticTransport(Uri upstream, HttpMessageHandler? countingSink = null)
    {
        if (upstream.Scheme != "http" || upstream.Host != "127.0.0.1" || upstream.Port <= 1024 || upstream.UserInfo.Length != 0 || upstream.AbsolutePath != "/" || upstream.Query.Length != 0 || upstream.Fragment.Length != 0)
            throw new InvalidOperationException("Only exact owned loopback upstream transport is permitted.");
        this.upstream = upstream;
        transport = new HttpClient(countingSink ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri != "https://data.creden.co/sapi/search/get_suggestion" || request.Headers.Authorization is not null || request.Content is null)
            throw new InvalidOperationException("Original provider URI/method/body contract rejected before transport.");
        var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 4096) throw new InvalidOperationException("Synthetic request exceeds bound.");
        using var body = JsonDocument.Parse(bytes);
        var root = body.RootElement;
        if (root.EnumerateObject().Count() != 3 || root.GetProperty("type_search").GetString() != "prefix" || root.GetProperty("lang").GetString() is not ("th" or "en") || root.GetProperty("text").GetString() is not { Length: >= 2 and <= 128 })
            throw new InvalidOperationException("Original provider JSON contract rejected before transport.");
        using var forwarded = new HttpRequestMessage(HttpMethod.Post, new Uri(upstream, "sapi/search/get_suggestion")) { Content = new ByteArrayContent(bytes) };
        forwarded.Content.Headers.ContentType = request.Content.Headers.ContentType;
        forwarded.Headers.Add("X-Synthetic-Logical-Origin", "https://data.creden.co/sapi/search/get_suggestion");
        return await transport.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
    protected override void Dispose(bool disposing) { if (disposing) transport.Dispose(); base.Dispose(disposing); }
}

internal static class TransportControls
{
    internal static async Task Run()
    {
        var rejected = new List<string>();
        foreach (var uri in new[] { "https://data.creden.co/", "http://localhost:13001/", "http://127.0.0.1:13001/other", "http://127.0.0.1:13001/?q=extra" })
        {
            try { using var invalid = new OwnedSyntheticTransport(new Uri(uri)); throw new Exception("Invalid physical origin accepted."); }
            catch (InvalidOperationException) { rejected.Add("physical-origin-" + rejected.Count); }
        }
        using var sink = new CountingSink();
        using var http = new HttpClient(new OwnedSyntheticTransport(new Uri("http://127.0.0.1:13001/"), sink));
        var cases = new[] { "logical-http", "logical-other-host", "logical-other-path", "logical-query", "method", "body", "authorization" };
        foreach (var name in cases)
        {
            using var request = Request();
            switch (name)
            {
                case "logical-http": request.RequestUri = new Uri("http://data.creden.co/sapi/search/get_suggestion"); break;
                case "logical-other-host": request.RequestUri = new Uri("https://example.test/sapi/search/get_suggestion"); break;
                case "logical-other-path": request.RequestUri = new Uri("https://data.creden.co/other"); break;
                case "logical-query": request.RequestUri = new Uri("https://data.creden.co/sapi/search/get_suggestion?q=extra"); break;
                case "method": request.Method = HttpMethod.Get; break;
                case "body": request.Content = new StringContent("{\"type_search\":\"exact\",\"text\":\"Synthetic en\",\"lang\":\"en\"}"); break;
                case "authorization": request.Headers.Authorization = new("Bearer", "synthetic-forbidden-header"); break;
            }
            try { using var response = await http.SendAsync(request); throw new Exception("Invalid logical request accepted."); }
            catch (InvalidOperationException) { rejected.Add(name); }
            if (sink.Attempts != 0) throw new Exception("Rejected request reached physical transport.");
        }
        using var valid = Request();
        using var accepted = await http.SendAsync(valid);
        if (accepted.StatusCode != HttpStatusCode.OK || sink.Attempts != 1 || rejected.Count != 11) throw new Exception("Real transport positive/negative control failed.");
        Directory.CreateDirectory("member-company-control-results");
        await File.WriteAllTextAsync("member-company-control-results/transport-controls.json", JsonSerializer.Serialize(new { nativeTransportControls = true, rejectedCases = rejected, rejectedPhysicalAttempts = 0, validPhysicalAttempts = sink.Attempts, realNetworkAllocated = false, validLoopbackUriVerified = true }));
    }
    private static HttpRequestMessage Request() => new(HttpMethod.Post, "https://data.creden.co/sapi/search/get_suggestion")
    {
        Content = new StringContent("{\"type_search\":\"prefix\",\"text\":\"Synthetic en\",\"lang\":\"en\"}")
    };
    private sealed class CountingSink : HttpMessageHandler
    {
        internal int Attempts { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (request.RequestUri?.AbsoluteUri != "http://127.0.0.1:13001/sapi/search/get_suggestion" || request.Method != HttpMethod.Post || request.Headers.GetValues("X-Synthetic-Logical-Origin").Single() != "https://data.creden.co/sapi/search/get_suggestion" || request.Content is null)
                throw new Exception("Positive physical transport contract differs.");
            using var body = JsonDocument.Parse(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            if (body.RootElement.GetProperty("text").GetString() != "Synthetic en") throw new Exception("Real provider body was replaced.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
