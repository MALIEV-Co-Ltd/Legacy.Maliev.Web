using System.Net;
using System.Reflection;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerQuotationClientTests
{
    [Fact]
    public async Task List_UsesOwnedCustomerRouteAndOpaqueServiceBearer()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"items":[{"id":9,"customerId":42,"invoiceId":null,"period":30,"expirationDate":"2026-12-31T00:00:00Z","subtotal":100,"vat":7,"total":107,"withholdingTax":3,"quotedAmount":104,"currencyId":1,"comment":null,"fob":"MALIEV","shippedVia":"Courier","terms":"30 days","accepted":null,"createdDate":"2026-07-15T00:00:00Z","modifiedDate":"2026-07-15T00:00:00Z"}],"pageIndex":2,"totalPages":3,"totalRecords":51}
            """));
        var client = CreateClient(handler);

        var result = await client.ListAsync(
            42,
            "QuotationCreatedDate_Descending",
            "CNC parts",
            2,
            25,
            CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.True(result.ServiceAvailable);
        Assert.Equal(9, result.Page?.Items.Single().Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer service-token", request.Authorization);
        Assert.Equal(
            "quotations/customers/42?sort=QuotationCreatedDate_Descending&search=CNC%20parts&index=2&size=25",
            request.Path);
    }

    [Fact]
    public async Task Get_ComposesOnlyOwnershipScopedDetailRequest()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"quotation":{"id":9,"customerId":42,"invoiceId":null,"period":30,"expirationDate":"2026-12-31T00:00:00Z","subtotal":100,"vat":7,"total":107,"withholdingTax":3,"quotedAmount":104,"currencyId":1,"comment":null,"fob":"MALIEV","shippedVia":"Courier","terms":"30 days","accepted":null,"createdDate":null,"modifiedDate":null},"orderItems":[{"id":4,"quotationId":9,"orderId":77,"description":"CNC line","quantity":2,"unitPrice":50,"subtotal":100,"createdDate":null,"modifiedDate":null}],"orders":[{"id":5,"quotationId":9,"orderId":77,"createdDate":null,"modifiedDate":null}],"files":[{"id":6,"quotationId":9,"bucket":"legacy-quotes","objectName":"quotes/9.pdf","createdDate":null,"modifiedDate":null}]}
            """));
        var client = CreateClient(handler);

        var result = await client.GetAsync(42, 9, CancellationToken.None);

        Assert.Equal("CNC line", result.Details?.OrderItems.Single().Description);
        Assert.Equal(77, result.Details?.Orders.Single().OrderId);
        Assert.Equal("quotes/9.pdf", result.Details?.Files.Single().ObjectName);
        Assert.Equal("quotations/9?customerId=42", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task UnauthorizedDetail_InvalidatesServiceToken()
    {
        var token = new RecordingTokenProvider();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler, token);

        var result = await client.GetAsync(42, 9, CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Equal("service-token", Assert.Single(token.Invalidated));
    }

    private static CustomerQuotationClient CreateClient(
        RecordingHandler handler,
        RecordingTokenProvider? token = null)
    {
        var constructor = typeof(CustomerQuotationClient).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 3);
        return (CustomerQuotationClient)constructor.Invoke([
            new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://quotations.test/") }),
            token ?? new RecordingTokenProvider(),
            NullLogger<CustomerQuotationClient>.Instance,
        ]);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty));
            return Task.FromResult(response(request));
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Authorization);

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("quotations", name);
            return client;
        }
    }

    private sealed class RecordingTokenProvider : IServiceAccessTokenProvider
    {
        public List<string> Invalidated { get; } = [];

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("service-token");

        public void Invalidate(string token) => Invalidated.Add(token);
    }
}
