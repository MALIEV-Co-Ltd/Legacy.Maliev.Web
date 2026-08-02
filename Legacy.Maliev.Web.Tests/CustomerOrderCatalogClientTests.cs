using System.Net;
using System.Reflection;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerOrderCatalogClientTests
{
    [Theory]
    [InlineData(CustomerOrderKind.Additive, "orders/processes/additive", "materials/printable")]
    [InlineData(CustomerOrderKind.Machining, "orders/processes/machining", "materials/machinable")]
    public async Task Get_ManufacturingFlow_LoadsExactOrderAndCatalogRoutes(
        CustomerOrderKind kind,
        string processRoute,
        string materialRoute)
    {
        var orders = new RecordingHandler(request => request.Path == processRoute
            ? Json("[{\"id\":3,\"categoryId\":1,\"name\":\"FDM\"}]")
            : Json("[{\"id\":2,\"name\":\"STEP\",\"extension\":\".step\"}]"));
        var catalog = new RecordingHandler(request => request.Path == materialRoute
            ? Json("[{\"id\":5,\"materialGroupId\":8,\"machinable\":true,\"printable\":true,\"name\":\"ABS\"}]")
            : Json("[{\"id\":8,\"name\":\"Polymer\"}]"));
        var client = CreateClient(orders, catalog);

        var result = await client.GetAsync(kind, CancellationToken.None);

        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.Equal("FDM", Assert.Single(result.Catalog!.Processes).Name);
        Assert.Equal("ABS", Assert.Single(result.Catalog.Materials).Name);
        Assert.Equal("Polymer", Assert.Single(result.Catalog.MaterialGroups).Name);
        Assert.Equal(".step", Assert.Single(result.Catalog.FileFormats).Extension);
        Assert.Equal([processRoute, "orders/fileformats"], orders.Requests.Select(request => request.Path));
        Assert.Equal([materialRoute, "materials/materialgroups"], catalog.Requests.Select(request => request.Path));
        Assert.All(orders.Requests.Concat(catalog.Requests), request => Assert.Equal("Bearer service-token", request.Authorization));
    }

    [Fact]
    public async Task Get_ScanningFlow_DoesNotRequestUnusedMaterialCatalogs()
    {
        var orders = new RecordingHandler(request => request.Path == "orders/processes/scanning"
            ? Json("[{\"id\":4,\"categoryId\":3,\"name\":\"Structured light\"}]")
            : Json("[{\"id\":1,\"name\":\"STL\",\"extension\":\".stl\"}]"));
        var catalog = new RecordingHandler(_ => throw new InvalidOperationException("Scanning must not load material catalogs."));
        var client = CreateClient(orders, catalog);

        var result = await client.GetAsync(CustomerOrderKind.Scanning, CancellationToken.None);

        Assert.True(result.ServiceAvailable);
        Assert.Empty(result.Catalog!.Materials);
        Assert.Empty(result.Catalog.MaterialGroups);
        Assert.Empty(catalog.Requests);
    }

    [Fact]
    public async Task GetMaterialOptions_LoadsOnlyOwnedMaterialAssociations()
    {
        var catalog = new RecordingHandler(request => request.Path.EndsWith("/colors", StringComparison.Ordinal)
            ? Json("[{\"id\":9,\"name\":\"Black\"}]")
            : Json("[{\"id\":7,\"name\":\"As printed\"}]"));
        var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), catalog);

        var result = await client.GetMaterialOptionsAsync(5, CancellationToken.None);

        Assert.True(result.ServiceAvailable);
        Assert.Equal("Black", Assert.Single(result.Options!.Colors).Name);
        Assert.Equal("As printed", Assert.Single(result.Options.SurfaceFinishes).Name);
        Assert.Equal(["materials/5/colors", "materials/5/surfacefinishes"], catalog.Requests.Select(request => request.Path));
    }

    private static CustomerOrderCatalogClient CreateClient(RecordingHandler orders, RecordingHandler catalog)
    {
        var clients = new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            ["orders"] = new(orders) { BaseAddress = new Uri("https://orders.test/") },
            ["catalog"] = new(catalog) { BaseAddress = new Uri("https://catalog.test/") },
        };
        var constructor = typeof(CustomerOrderCatalogClient).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        return (CustomerOrderCatalogClient)constructor.Invoke([
            new NamedClientFactory(clients),
            new StubTokenProvider(),
            NullLogger<CustomerOrderCatalogClient>.Instance,
        ]);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class NamedClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class StubTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("service-token");
        public void Invalidate(string token) { }
    }

    private sealed class RecordingHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(
                request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty);
            Requests.Add(recorded);
            return Task.FromResult(respond(recorded));
        }
    }

    private sealed record RecordedRequest(string Path, string Authorization);
}
