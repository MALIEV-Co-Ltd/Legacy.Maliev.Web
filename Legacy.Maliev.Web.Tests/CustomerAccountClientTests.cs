using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerAccountClientTests
{
    [Fact]
    public async Task GetAddressProfile_UsesServiceBearerAndOwnedCustomerPath()
    {
        var handler = new RecordingHandler(request => Json(
            HttpStatusCode.OK,
            CustomerJson(7, 11)));
        var client = CreateClient(handler);

        var result = await client.GetAddressProfileAsync(42, default);

        Assert.True(result.Authorized);
        Assert.True(result.ServiceAvailable);
        Assert.Equal(42, result.Profile?.Customer.Id);
        Assert.Equal("Bearer service-token", handler.Requests.Single().Authorization);
        Assert.Equal("customers/42", handler.Requests.Single().Path);
    }

    [Fact]
    public async Task UpdateExistingAddresses_UsesServerLoadedIdsAndPreservedWireShape()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.PathAndQuery switch
        {
            "/customers/42" when request.Method == HttpMethod.Get =>
                Json(HttpStatusCode.OK, CustomerJson(7, 11)),
            "/customers/addresses/7" when request.Method == HttpMethod.Put =>
                new HttpResponseMessage(HttpStatusCode.NoContent),
            "/customers/addresses/11" when request.Method == HttpMethod.Put =>
                new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(handler);

        var result = await client.UpdateAddressesAsync(
            42,
            new CustomerAddressUpdate(
                new CustomerAddressInput("B", "1 Billing Rd", null, "Bangkok", null, "10110", 764),
                new CustomerAddressInput(null, "2 Shipping Rd", "Suite 3", "Bangkok", null, "10200", 764)),
            default);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["customers/42", "customers/addresses/7", "customers/addresses/11"],
            handler.Requests.Select(request => request.Path));
        Assert.All(handler.Requests, request => Assert.Equal("Bearer service-token", request.Authorization));
        Assert.Contains("\"addressLine1\":\"1 Billing Rd\"", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"countryId\":764", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("customerId", handler.Requests[1].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAddresses_LinksReturnedIdsUsingServerLoadedCustomerProfile()
    {
        var nextAddressId = 90;
        var handler = new RecordingHandler(request => request.RequestUri!.PathAndQuery switch
        {
            "/customers/42" when request.Method == HttpMethod.Get =>
                Json(HttpStatusCode.OK, CustomerJson(null, null)),
            "/customers/42/addresses" when request.Method == HttpMethod.Post =>
                Json(HttpStatusCode.Created, AddressJson(nextAddressId++)),
            "/customers/42" when request.Method == HttpMethod.Put =>
                new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(handler);

        var result = await client.UpdateAddressesAsync(
            42,
            new CustomerAddressUpdate(
                new CustomerAddressInput(null, "1 Billing Rd", null, "Bangkok", null, "10110", 764),
                new CustomerAddressInput(null, "2 Shipping Rd", null, "Bangkok", null, "10200", 764)),
            default);

        Assert.True(result.Succeeded);
        Assert.Equal(4, handler.Requests.Count);
        var customerUpdate = handler.Requests[^1];
        Assert.Equal("customers/42", customerUpdate.Path);
        Assert.Contains("\"billingAddressId\":90", customerUpdate.Body, StringComparison.Ordinal);
        Assert.Contains("\"shippingAddressId\":91", customerUpdate.Body, StringComparison.Ordinal);
        Assert.Contains("\"email\":\"customer@example.com\"", customerUpdate.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateEmail_ReloadsOwnedCustomerAndPreservesServerHeldRelationships()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.PathAndQuery switch
        {
            "/customers/42" when request.Method == HttpMethod.Get =>
                Json(HttpStatusCode.OK, CustomerJson(7, 11)),
            "/customers/42" when request.Method == HttpMethod.Put =>
                new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(handler);

        var result = await client.UpdateEmailAsync(42, "new@example.com", default);

        Assert.True(result.Succeeded);
        Assert.Equal(["customers/42", "customers/42"], handler.Requests.Select(request => request.Path));
        Assert.Contains("\"email\":\"new@example.com\"", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"billingAddressId\":7", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"shippingAddressId\":11", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProfile_CreatesCompanyAndLinksOnlyTheServerReturnedId()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.PathAndQuery switch
        {
            "/customers/42" when request.Method == HttpMethod.Get =>
                Json(HttpStatusCode.OK, CustomerJson(7, 11)),
            "/customers/companies" when request.Method == HttpMethod.Post =>
                Json(HttpStatusCode.Created, """{"id":88,"name":"Analytical Engines","taxNumber":"TAX","registrar":"DBD","createdDate":null,"modifiedDate":null}"""),
            "/customers/42" when request.Method == HttpMethod.Put =>
                new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(handler);

        var result = await client.UpdateProfileAsync(
            42,
            new CustomerProfileUpdate(
                "Ada", "Lovelace", "02", "08", null, null,
                "Analytical Engines", "TAX", "DBD"),
            default);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["customers/42", "customers/companies", "customers/42"],
            handler.Requests.Select(request => request.Path));
        Assert.Contains("\"companyId\":88", handler.Requests[^1].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("companyId", handler.Requests[1].Body, StringComparison.OrdinalIgnoreCase);
    }

    private static CustomerAccountClient CreateClient(RecordingHandler handler) => new(
        new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://customers.test/") }),
        new StubServiceTokenProvider(),
        NullLogger<CustomerAccountClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string CustomerJson(int? billingAddressId, int? shippingAddressId) => $$"""
        {
          "id":42,"firstName":"Ada","lastName":"Lovelace","fullName":"Ada Lovelace",
          "telephone":null,"mobile":null,"fax":null,"email":"customer@example.com",
          "dateOfBirth":null,"companyId":null,
          "billingAddressId":{{JsonId(billingAddressId)}},"shippingAddressId":{{JsonId(shippingAddressId)}},
          "createdDate":null,"modifiedDate":null,
          "billingAddress":{{(billingAddressId is null ? "null" : AddressJson(billingAddressId.Value))}},
          "company":null,
          "shippingAddress":{{(shippingAddressId is null ? "null" : AddressJson(shippingAddressId.Value))}}
        }
        """;

    private static string AddressJson(int id) =>
        $$"""{"id":{{id}},"building":null,"addressLine1":"Existing","addressLine2":null,"city":"Bangkok","state":null,"postalCode":"10110","countryId":764,"createdDate":null,"modifiedDate":null}""";

    private static string JsonId(int? value) => value?.ToString() ?? "null";

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return response(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Authorization, string Body);

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("service-token");

        public void Invalidate(string token)
        {
        }
    }
}
