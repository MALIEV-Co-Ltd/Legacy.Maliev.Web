using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerProfileClientTests
{
    [Fact]
    public async Task ProvisionInstantQuotation_UsesAtomicProfileContractWithSeparateShipping()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/customers/instant-quotation-profile", request.RequestUri?.AbsolutePath);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-token"), request.Headers.Authorization);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(
                ["billing", "company", "email", "firstName", "lastName", "mobile", "shipToBillingAddress", "shipping", "taxNumber", "telephone"],
                json.RootElement.EnumerateObject()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            Assert.False(json.RootElement.GetProperty("shipToBillingAddress").GetBoolean());
            Assert.Equal(218, json.RootElement.GetProperty("billing").GetProperty("countryId").GetInt32());
            Assert.Equal("Billing Road", json.RootElement.GetProperty("billing").GetProperty("addressLine1").GetString());
            Assert.Equal(219, json.RootElement.GetProperty("shipping").GetProperty("countryId").GetInt32());
            Assert.Equal("Shipping Road", json.RootElement.GetProperty("shipping").GetProperty("addressLine1").GetString());
            Assert.False(json.RootElement.TryGetProperty("description", out _));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"customerId\":42,\"customerCreated\":true}", Encoding.UTF8, "application/json")
            };
        });
        var client = new CustomerProfileClient(
            new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://customers/") }),
            new StubTokenProvider(),
            NullLogger<CustomerProfileClient>.Instance);

        var result = await client.ProvisionInstantQuotationAsync(
            new InstantQuotationCustomerSubmission(
                "Ada",
                "Lovelace",
                "ada@example.com",
                "+66 2 000 0000",
                "TH",
                "Analytical Engines",
                "0105559123456 (สำนักงานใหญ่)",
                "not part of the customer profile contract",
                "+66 89 000 0000",
                "88",
                "Billing Road",
                null,
                "Bangkok",
                "Bangkok",
                "10110",
                ShipToBillingAddress: false,
                ShippingBuilding: "99",
                ShippingAddressLine1: "Shipping Road",
                ShippingAddressLine2: null,
                ShippingCity: "Bangkok",
                ShippingProvince: "Bangkok",
                ShippingPostalCode: "10200",
                ShippingCountry: "SG"),
            218,
            219,
            CancellationToken.None);

        Assert.Equal(42, result.CustomerId);
        Assert.True(result.CustomerCreated);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
    }

    [Fact]
    public async Task ProvisionInstantQuotation_ServerFailureDoesNotExposePrivateResponse()
    {
        const string privateMarker = "private-response-canary@example.test";
        var handler = new RecordingHandler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(privateMarker, Encoding.UTF8, "text/plain")
        }));
        var logger = new RecordingLogger();
        var client = new CustomerProfileClient(
            new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://customers/") }),
            new StubTokenProvider(), logger);
        var customer = new InstantQuotationCustomerSubmission(
            "Synthetic", "Customer", "customer@example.test", "000", "TH", null, null, null,
            "000", null, "Synthetic Road", null, "City", "Province", "00000");

        var result = await client.ProvisionInstantQuotationAsync(customer, 218, 218, CancellationToken.None);

        Assert.Equal(new InstantQuotationCustomerProvisionResult(null, false, false, true), result);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(privateMarker, StringComparison.Ordinal)
            || message.Contains(customer.Email, StringComparison.Ordinal));
    }

    private sealed class RecordingLogger : ILogger<CustomerProfileClient>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception) + exception);
    }

    private sealed class StubTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class NamedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("customers", name);
            return client;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
