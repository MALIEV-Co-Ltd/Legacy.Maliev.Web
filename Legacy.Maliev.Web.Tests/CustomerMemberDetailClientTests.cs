using System.Net;
using System.Reflection;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerMemberDetailClientTests
{
    [Fact]
    public async Task OrderSupplement_UsesExistingCatalogAndSignedUrlContracts()
    {
        var factory = new RecordingClientFactory((name, path) => (name, path) switch
        {
            ("catalog", "materials/12") => Json("""
                {"id":12,"materialGroupId":4,"name":"Aluminium 6061","materialGroup":{"id":4,"name":"Metal"}}
                """),
            ("catalog", "materials/surfacefinishes/8") => Json("""{"id":8,"name":"Bead blasted"}"""),
            ("catalog", "materials/colors/9") => Json("""{"id":9,"name":"Natural"}"""),
            ("catalog", "currencies/1") => Json("""{"id":1,"shortName":"THB","longName":"Thai Baht"}"""),
            ("files", var value) when value.StartsWith("uploads/signedurl?", StringComparison.Ordinal) =>
                Json("\"https://storage.test/orders/part.step?signature=opaque\""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(factory);
        var order = new CustomerOrder(
            7, 42, "Part", null, 3, 2, 0, 2, 100, 0, 200, 5, null, null, null,
            true, false, null, DateTime.UnixEpoch, DateTime.UnixEpoch)
        {
            MaterialId = 12,
            SurfaceFinishId = 8,
            ColorId = 9,
            CurrencyId = 1,
        };
        var details = new CustomerOrderDetails(
            order,
            new CustomerOrderProcess(3, 1, "CNC"),
            [],
            [new CustomerOrderFile(4, 7, "legacy-orders", "orders/part.step", DateTime.UnixEpoch, null)]);

        var result = await client.GetOrderSupplementAsync(details, default);

        Assert.Equal("Metal", result.MaterialGroupName);
        Assert.Equal("Aluminium 6061", result.MaterialName);
        Assert.Equal("Bead blasted", result.SurfaceFinishName);
        Assert.Equal("Natural", result.ColorName);
        Assert.Equal("THB", result.CurrencyName);
        var file = Assert.Single(result.Files);
        Assert.Equal("part.step", file.FileName);
        Assert.Equal("https://storage.test/orders/part.step?signature=opaque", file.Href.AbsoluteUri);
        Assert.Empty(result.Warnings);
        Assert.All(factory.Requests, request => Assert.Equal("Bearer service-token", request.Authorization));
        Assert.Contains(factory.Requests, request => request.Name == "files"
            && request.Path.Contains("bucket=legacy-orders", StringComparison.Ordinal)
            && request.Path.Contains("objectName=orders%2Fpart.step", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QuotationSupplement_ComposesOnlyOwnedAccountingAndCustomerData()
    {
        var factory = new RecordingClientFactory((name, path) => (name, path) switch
        {
            ("customers", "customers/42") => Json("""
                {"id":42,"firstName":"Nina","lastName":"Test","fullName":"Nina Test","telephone":"0200","mobile":"0690","fax":null,"email":"nina@example.test","billingAddressId":1,"shippingAddressId":2,"billingAddress":{"id":1,"building":"HQ","addressLine1":"1 Main","addressLine2":null,"city":"Bangkok","state":null,"postalCode":"10110","countryId":764},"shippingAddress":{"id":2,"building":null,"addressLine1":"2 Factory","addressLine2":null,"city":"Nonthaburi","state":null,"postalCode":"11120","countryId":764}}
                """),
            ("countries", "countries") => Json("""[{"id":764,"name":"Thailand"}]"""),
            ("catalog", "currencies/764") => Json("""{"id":764,"shortName":"THB","longName":"Thai Baht"}"""),
            ("accounting", "invoices/81") => Json("""
                {"id":81,"number":"INV-81","customerId":42,"currency":"THB","isPaid":false,"receiptId":91,"paymentDate":null,"outstanding":107}
                """),
            ("accounting", "invoices/81/files") => Json("""[{"id":1,"bucket":"invoices","objectName":"81.pdf","createdDate":"2026-07-15T00:00:00Z"}]"""),
            ("accounting", "receipts/91/files") => Json("""[{"id":2,"bucket":"receipts","objectName":"91.pdf","createdDate":"2026-07-16T00:00:00Z"}]"""),
            ("accounting", "payments/accounts") => Json("""[{"id":1,"bank":"Example Bank","accountNumber":"1234","swift":"EXTHBK","branch":"Bangkok"}]"""),
            ("files", var value) when value.Contains("bucket=quotations", StringComparison.Ordinal) => Json("\"https://storage.test/quote.pdf\""),
            ("files", var value) when value.Contains("bucket=invoices", StringComparison.Ordinal) => Json("\"https://storage.test/invoice.pdf\""),
            ("files", var value) when value.Contains("bucket=receipts", StringComparison.Ordinal) => Json("\"https://storage.test/receipt.pdf\""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(factory);
        var quotation = new CustomerQuotation(
            15, 42, 81, 30, new DateTime(2026, 8, 15), 100, 7, 107, 3, 104,
            764, null, null, null, null, null, DateTime.UnixEpoch, DateTime.UnixEpoch);
        var details = new CustomerQuotationDetails(
            quotation,
            [],
            [],
            [new CustomerQuotationFile(1, 15, "quotations", "15.pdf", DateTime.UnixEpoch, null)]);

        var result = await client.GetQuotationSupplementAsync(42, details, default);

        Assert.Equal("THB", result.CurrencyName);
        Assert.Equal("Nina Test", result.Customer?.FullName);
        Assert.Equal("Thailand", result.Customer?.BillingAddress?.Country);
        Assert.Equal("INV-81", result.Invoice?.Number);
        Assert.Equal("https://storage.test/quote.pdf", result.QuotationDocument?.AbsoluteUri);
        Assert.Equal("https://storage.test/invoice.pdf", result.InvoiceDocument?.AbsoluteUri);
        Assert.Equal("https://storage.test/receipt.pdf", result.ReceiptDocument?.AbsoluteUri);
        Assert.Equal("1234", Assert.Single(result.BankAccounts).AccountNumber);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task QuotationSupplement_MismatchedInvoiceOwnershipFailsClosed()
    {
        var factory = new RecordingClientFactory((name, path) => (name, path) switch
        {
            ("accounting", "invoices/81") => Json("""
                {"id":81,"number":"INV-81","customerId":999,"currency":"THB","isPaid":false,"receiptId":91,"paymentDate":null,"outstanding":107}
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = CreateClient(factory);
        var quotation = new CustomerQuotation(
            15, 42, 81, 30, new DateTime(2026, 8, 15), 100, 7, 107, null, null,
            764, null, null, null, null, null, null, null);

        var result = await client.GetQuotationSupplementAsync(
            42,
            new CustomerQuotationDetails(quotation, [], [], []),
            default);

        Assert.Null(result.Invoice);
        Assert.Null(result.InvoiceDocument);
        Assert.Null(result.ReceiptDocument);
        Assert.Empty(result.BankAccounts);
        Assert.Contains("Invoice details are temporarily unavailable.", result.Warnings);
        Assert.DoesNotContain(factory.Requests, request => request.Path.StartsWith("invoices/81/files", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Requests, request => request.Path.StartsWith("receipts/", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Requests, request => request.Path == "payments/accounts");
    }

    private static ICustomerMemberDetailClient CreateClient(IHttpClientFactory factory)
    {
        var constructor = typeof(CustomerMemberDetailClient).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();
        return (ICustomerMemberDetailClient)constructor.Invoke([
            factory,
            new RecordingTokenProvider(),
            NullLogger<CustomerMemberDetailClient>.Instance,
        ]);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingClientFactory(Func<string, string, HttpResponseMessage> response) : IHttpClientFactory
    {
        public List<RecordedRequest> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(new Handler(request =>
        {
            var path = request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty;
            Requests.Add(new(name, path, request.Headers.Authorization?.ToString() ?? string.Empty));
            return response(name, path);
        }))
        {
            BaseAddress = new Uri($"https://{name}.test/"),
        };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed record RecordedRequest(string Name, string Path, string Authorization);

    private sealed class RecordingTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("service-token");

        public void Invalidate(string token)
        {
        }
    }
}
