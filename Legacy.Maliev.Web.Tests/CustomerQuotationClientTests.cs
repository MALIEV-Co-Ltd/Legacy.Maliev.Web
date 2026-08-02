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

    [Fact]
    public async Task Accept_VerifiesOwnershipCreatesInvoiceThenPersistsDecision()
    {
        var quotationHandler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, OwnedQuotationJson)
            : Json(HttpStatusCode.OK, "{\"status\":0,\"completedOrders\":1,\"totalOrders\":1,\"modifiedDate\":\"2026-08-02T00:00:00Z\"}"));
        var accountingHandler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, InvoicePreviewJson)
            : Json(HttpStatusCode.OK, "{\"invoiceId\":23,\"state\":0,\"emailState\":1,\"providerMessageId\":\"message-1\",\"storedFile\":{\"bucket\":\"maliev.com\",\"objectName\":\"invoices/23/invoice_020826-42-9.pdf\"}}"));
        var operationId = Guid.Parse("11fdb79a-3548-4c24-8dbc-1b457c3bc04e");
        var client = CreateClient(quotationHandler, accountingHandler);

        var result = await client.DecideAsync(42, 9, true, operationId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(23, result.InvoiceId);
        Assert.Collection(
            quotationHandler.Requests,
            request => Assert.Equal((HttpMethod.Get, "quotations/9?customerId=42"), (request.Method, request.Path)),
            request =>
            {
                Assert.Equal((HttpMethod.Put, "quotations/9/decision"), (request.Method, request.Path));
                Assert.Contains("\"accepted\":true", request.Body, StringComparison.Ordinal);
            });
        Assert.Collection(
            accountingHandler.Requests,
            request => Assert.Equal((HttpMethod.Get, "invoices/from-quotation/9/preview"), (request.Method, request.Path)),
            request =>
            {
                Assert.Equal((HttpMethod.Post, "invoices/from-quotation/9"), (request.Method, request.Path));
                Assert.Equal(operationId.ToString("D"), request.IdempotencyKey);
                Assert.Contains("\"invoiceNumber\":\"020826-42-9\"", request.Body, StringComparison.Ordinal);
                Assert.Contains("\"sendEmail\":true", request.Body, StringComparison.Ordinal);
                Assert.Contains("\"deductWithholdingTax\":true", request.Body, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Decline_VerifiesOwnershipWithoutCreatingInvoice()
    {
        var quotationHandler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, OwnedQuotationJson)
            : Json(HttpStatusCode.OK, "{\"status\":0,\"completedOrders\":1,\"totalOrders\":1,\"modifiedDate\":\"2026-08-02T00:00:00Z\"}"));
        var accountingHandler = new RecordingHandler(_ => throw new InvalidOperationException("Accounting must not be called for a decline."));
        var client = CreateClient(quotationHandler, accountingHandler);

        var result = await client.DecideAsync(42, 9, false, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.InvoiceId);
        Assert.Empty(accountingHandler.Requests);
        Assert.Contains("\"accepted\":false", quotationHandler.Requests[1].Body, StringComparison.Ordinal);
    }

    private static CustomerQuotationClient CreateClient(
        RecordingHandler handler,
        RecordingTokenProvider? token = null) => CreateClient(handler, null, token);

    private static CustomerQuotationClient CreateClient(
        RecordingHandler quotationHandler,
        RecordingHandler? accountingHandler,
        RecordingTokenProvider? token = null)
    {
        var constructor = typeof(CustomerQuotationClient).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(value => value.GetParameters().Length == 3);
        return (CustomerQuotationClient)constructor.Invoke([
            new NamedClientFactory(
                new HttpClient(quotationHandler) { BaseAddress = new Uri("https://quotations.test/") },
                new HttpClient(accountingHandler ?? new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))) { BaseAddress = new Uri("https://accounting.test/") }),
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
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null,
                request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty));
            return Task.FromResult(response(request));
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Authorization, string? IdempotencyKey, string Body);

    private sealed class NamedClientFactory(HttpClient quotations, HttpClient accounting) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return name switch
            {
                "quotations" => quotations,
                "accounting" => accounting,
                _ => throw new InvalidOperationException($"Unexpected client name: {name}"),
            };
        }
    }

    private sealed class RecordingTokenProvider : IServiceAccessTokenProvider
    {
        public List<string> Invalidated { get; } = [];

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("service-token");

        public void Invalidate(string token) => Invalidated.Add(token);
    }

    private const string OwnedQuotationJson = """
        {"quotation":{"id":9,"customerId":42,"invoiceId":null,"period":30,"expirationDate":"2026-12-31T00:00:00Z","subtotal":100,"vat":7,"total":107,"withholdingTax":3,"quotedAmount":104,"currencyId":1,"comment":null,"fob":"MALIEV","shippedVia":"Courier","terms":"30 days","accepted":null,"createdDate":null,"modifiedDate":null},"orderItems":[{"id":4,"quotationId":9,"orderId":77,"description":"CNC line","quantity":2,"unitPrice":50,"subtotal":100,"createdDate":null,"modifiedDate":null}],"orders":[{"id":5,"quotationId":9,"orderId":77,"createdDate":null,"modifiedDate":null}],"files":[]}
        """;

    private const string InvoicePreviewJson = """
        {"quotationId":9,"customerId":42,"invoiceNumber":"020826-42-9","salesPerson":"MALIEV","currency":"THB","comment":null,"shippedVia":"Courier","fob":"MALIEV","terms":"30 days","billingAddress":{"recipient":"Customer","company":null,"building":null,"line1":"36/1","line2":null,"city":"Pak Kret","state":"Nonthaburi","postalCode":"11120","country":"Thailand","telephone":null},"shippingAddress":{"recipient":"Customer","company":null,"building":null,"line1":"36/1","line2":null,"city":"Pak Kret","state":"Nonthaburi","postalCode":"11120","country":"Thailand","telephone":"0898950690"},"taxIdentification":null,"commercialRegistration":null,"subtotal":100,"vat":7,"total":107,"availableWithholdingTax":3,"outstanding":104,"orderItems":[{"id":4,"quotationId":9,"orderId":77,"description":"CNC line","quantity":2,"unitPrice":50,"subtotal":100}]}
        """;
}
