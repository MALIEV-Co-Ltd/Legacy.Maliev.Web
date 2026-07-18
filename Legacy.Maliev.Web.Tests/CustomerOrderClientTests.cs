using System.Net;
using System.Reflection;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerOrderClientTests
{
    [Fact]
    public async Task List_UsesOwnedCustomerRouteAndServiceBearer()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"items":[{"id":7,"customerId":42,"name":"Part","description":"CNC part","processId":3,"quantity":2,"manufactured":0,"remaining":2,"unitPrice":100,"discountPercent":0,"subtotal":200,"leadTime":5,"promisedDate":null,"finishedDate":null,"comment":null,"allowCancellation":true,"allowPayment":false,"trackingNumber":null,"createdDate":"2026-07-15T00:00:00Z","modifiedDate":"2026-07-15T00:00:00Z"}],"pageIndex":2,"totalPages":3,"totalRecords":51}
            """));
        var client = CreateClient(handler);

        var result = await client.ListAsync(42, "OrderCreatedDate_Descending", "CNC part", 2, 25, default);

        Assert.True(result.Authorized);
        Assert.True(result.ServiceAvailable);
        Assert.Equal(7, result.Page?.Items.Single().Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer service-token", request.Authorization);
        Assert.Equal(
            "orders/customers/42?sort=OrderCreatedDate_Descending&search=CNC%20part&index=2&size=25",
            request.Path);
    }

    [Fact]
    public async Task Get_ComposesOnlyTheCustomerScopedDetailRoute()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """
            {"order":{"id":7,"customerId":42,"name":"Part","description":null,"processId":3,"quantity":2,"manufactured":0,"remaining":2,"unitPrice":100,"discountPercent":0,"subtotal":200,"leadTime":5,"promisedDate":null,"finishedDate":null,"comment":null,"allowCancellation":true,"allowPayment":false,"trackingNumber":null,"createdDate":null,"modifiedDate":null},"process":{"id":3,"categoryId":1,"name":"CNC"},"history":[{"id":9,"orderId":7,"orderStatusId":2,"name":"Reviewing","description":null,"createdDate":null,"modifiedDate":null}],"files":[{"id":4,"orderId":7,"bucket":"legacy-orders","objectName":"orders/part.step","createdDate":null,"modifiedDate":null}]}
            """));
        var client = CreateClient(handler);

        var result = await client.GetAsync(42, 7, default);

        Assert.Equal("CNC", result.Details?.Process?.Name);
        Assert.Equal("Reviewing", result.Details?.History.Single().Name);
        Assert.Equal("orders/part.step", result.Details?.Files.Single().ObjectName);
        Assert.Equal("orders/customers/42/7", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task Cancel_PostsToOwnedRouteAndMapsConflictWithoutLeakingToken()
    {
        var token = new RecordingTokenProvider();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        var client = CreateClient(handler, token);

        var result = await client.CancelAsync(42, 7, default);

        Assert.False(result.Succeeded);
        Assert.True(result.Conflict);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("orders/customers/42/7/cancel", request.Path);
        Assert.Equal("Bearer service-token", request.Authorization);
        Assert.Empty(token.Invalidated);
    }

    private static CustomerOrderClient CreateClient(
        RecordingHandler handler,
        RecordingTokenProvider? token = null)
    {
        var constructor = typeof(CustomerOrderClient).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(value => value.GetParameters().Length == 3);
        Assert.NotNull(constructor);
        return (CustomerOrderClient)constructor.Invoke([
            new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://orders.test/") }),
            token ?? new RecordingTokenProvider(),
            NullLogger<CustomerOrderClient>.Instance,
        ]);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

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

    private sealed class RecordingTokenProvider : IServiceAccessTokenProvider
    {
        public List<string> Invalidated { get; } = [];

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("service-token");

        public void Invalidate(string token) => Invalidated.Add(token);
    }
}
