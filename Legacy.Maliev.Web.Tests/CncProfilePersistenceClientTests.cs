using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncProfilePersistenceClientTests
{
    [Fact]
    public async Task CompleteAsync_CreatesCompanyAndBillingThenLinksCustomerLast()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/customers/companies" => Created("/customers/Companies/21", new { Id = 21, Name = "MALIEV", TaxNumber = "010", Registrar = (string?)null }),
            "/countries/" => Json(HttpStatusCode.OK, new[] { new { Id = 138, Name = "Thailand" } }),
            "/customers/7/addresses" => Created("/customers/7/addresses/31", new { Id = 31, Building = "9", AddressLine1 = "Road", AddressLine2 = (string?)null, City = "Bangkok", State = "Bangkok", PostalCode = "10210", CountryId = 138 }),
            "/customers/7" => new(HttpStatusCode.NoContent),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath),
        });

        var result = await Client(handler).CompleteAsync(Request(shipToBilling: true), CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Completed, result.Outcome);
        Assert.Equal(42, result.QuotationRequestId);
        Assert.Equal(21, result.CreatedCompanyId);
        Assert.Equal(31, result.CreatedBillingAddressId);
        Assert.True(result.HasConfirmedChanges);
        Assert.Equal(["POST /customers/companies", "GET /countries/", "POST /customers/7/addresses", "PUT /customers/7"], handler.Calls.Select(x => $"{x.Method} {x.Path}"));
        using var customer = JsonDocument.Parse(handler.Calls[^1].Body!);
        Assert.Equal("fax-kept", customer.RootElement.GetProperty("Fax").GetString());
        Assert.Equal(21, customer.RootElement.GetProperty("CompanyId").GetInt32());
        Assert.Equal(31, customer.RootElement.GetProperty("BillingAddressId").GetInt32());
        Assert.Equal(31, customer.RootElement.GetProperty("ShippingAddressId").GetInt32());
        Assert.True(customer.RootElement.TryGetProperty("FirstName", out _));
        Assert.False(customer.RootElement.TryGetProperty("firstName", out _));
    }

    [Fact]
    public async Task CompleteAsync_ExistingCompleteProfile_PerformsCountryReadButNoWrites()
    {
        var existing = Request(shipToBilling: true) with
        {
            PreparedCustomer = ExistingCustomer(),
        };
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, new[] { new { Id = 138, Name = "Thailand" } }));

        var result = await Client(handler).CompleteAsync(existing, CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Completed, result.Outcome);
        Assert.False(result.HasConfirmedChanges);
        Assert.Single(handler.Calls);
        Assert.Equal("GET /countries/", $"{handler.Calls[0].Method} {handler.Calls[0].Path}");
    }

    [Fact]
    public async Task CompleteAsync_UnknownPostOutcome_IsTerminalAndRetainsQuotationReference()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("synthetic"));

        var result = await Client(handler).CompleteAsync(Request(true), CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Unknown, result.Outcome);
        Assert.Equal(CncProfilePersistenceStage.Company, result.Stage);
        Assert.Equal(42, result.QuotationRequestId);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task CompleteAsync_DefiniteWriteRejection_StopsWithoutCompensation()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));

        var result = await Client(handler).CompleteAsync(Request(true), CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Failed, result.Outcome);
        Assert.Equal(422, result.HttpStatusCode);
        Assert.Single(handler.Calls);
        Assert.DoesNotContain(handler.Calls, call => call.Method == HttpMethod.Delete.Method);
    }

    [Fact]
    public async Task CompleteAsync_MissingDistinctShippingCountry_StopsAfterConfirmedWrites()
    {
        var countryReads = 0;
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/customers/companies" => Created("/customers/Companies/21", new { Id = 21, Name = "MALIEV", TaxNumber = "010", Registrar = (string?)null }),
            "/countries/" when countryReads++ == 0 => Json(HttpStatusCode.OK, new[] { new { Id = 138, Name = "Thailand" } }),
            "/countries/" => Json(HttpStatusCode.OK, new[] { new { Id = 138, Name = "Thailand" } }),
            "/customers/7/addresses" => Created("/customers/7/addresses/31", new { Id = 31, Building = "9", AddressLine1 = "Road", AddressLine2 = (string?)null, City = "Bangkok", State = "Bangkok", PostalCode = "10210", CountryId = 138 }),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath),
        });

        var result = await Client(handler).CompleteAsync(Request(shipToBilling: false), CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Failed, result.Outcome);
        Assert.Equal(CncProfilePersistenceStage.ShippingCountry, result.Stage);
        Assert.True(result.HasConfirmedChanges);
        Assert.Equal(21, result.CreatedCompanyId);
        Assert.Equal(31, result.CreatedBillingAddressId);
        Assert.Equal(4, handler.Calls.Count);
    }

    [Fact]
    public async Task CompleteAsync_FillsExistingRecordsWithoutReplacingAuthoritativeValues()
    {
        var billing = new CustomerAddress(31, null, "Stored Road", null, null, "Stored State", null, 0, null, null);
        var shipping = new CustomerAddress(32, "Stored Building", "Shipping Road", null, null, null, null, 138, null, null);
        var company = new CustomerCompany(21, "Stored Company", null, "registrar-kept", null, null);
        var stored = new CustomerAccountDetails(7, "Stored", "Person", "Stored Person", null, null, "fax-kept", "stored@example.test",
            new DateTime(1990, 1, 2), 21, 31, 32, null, null, billing, company, shipping);
        var request = Request(false) with { PreparedCustomer = stored, Completion = Request(false).Completion with { ShippingCountry = "Thailand" } };
        var handler = new RecordingHandler(message => message.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, new[] { new { Id = 138, Name = "Thailand" } })
            : new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await Client(handler).CompleteAsync(request, CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Completed, result.Outcome);
        Assert.Equal(["PUT /customers/companies/21", "GET /countries/", "PUT /customers/addresses/31", "GET /countries/", "PUT /customers/addresses/32", "PUT /customers/7"],
            handler.Calls.Select(call => $"{call.Method} {call.Path}"));
        using var companyBody = JsonDocument.Parse(handler.Calls[0].Body!);
        Assert.Equal("Stored Company", companyBody.RootElement.GetProperty("Name").GetString());
        Assert.Equal("registrar-kept", companyBody.RootElement.GetProperty("Registrar").GetString());
        using var customerBody = JsonDocument.Parse(handler.Calls[^1].Body!);
        Assert.Equal("Stored", customerBody.RootElement.GetProperty("FirstName").GetString());
        Assert.Equal("stored@example.test", customerBody.RootElement.GetProperty("Email").GetString());
        Assert.Equal("fax-kept", customerBody.RootElement.GetProperty("Fax").GetString());
    }

    [Fact]
    public async Task CompleteAsync_DistinctShipping_CreatesBothAddressesInSourceOrder()
    {
        var address = 0;
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/customers/companies" => Created("https://customers.example/customers/Companies/21", new { Id = 21, Name = "MALIEV", TaxNumber = "010", Registrar = (string?)null }),
            "/countries/" => Json(HttpStatusCode.OK, new[] { new { Id = 138, Name = address == 0 ? "Thailand" : "Missing" } }),
            "/customers/7/addresses" when address++ == 0 => Created("https://customers.example/customers/7/addresses/31", new { Id = 31, Building = "9", AddressLine1 = "Road", AddressLine2 = (string?)null, City = "Bangkok", State = "Bangkok", PostalCode = "10210", CountryId = 138 }),
            "/customers/7/addresses" => Created("https://customers.example/customers/7/addresses/32", new { Id = 32, Building = (string?)null, AddressLine1 = "Other", AddressLine2 = (string?)null, City = "Bangkok", State = "Bangkok", PostalCode = "10220", CountryId = 138 }),
            "/customers/7" => new(HttpStatusCode.NoContent),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath),
        });

        var result = await Client(handler).CompleteAsync(Request(false), CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Completed, result.Outcome);
        Assert.Equal(31, result.CreatedBillingAddressId);
        Assert.Equal(32, result.CreatedShippingAddressId);
        Assert.Equal(["POST /customers/companies", "GET /countries/", "POST /customers/7/addresses", "GET /countries/", "POST /customers/7/addresses", "PUT /customers/7"],
            handler.Calls.Select(call => $"{call.Method} {call.Path}"));
    }

    [Fact]
    public async Task CompleteAsync_MalformedCreatedResponse_IsUnknownAndNeverContinues()
    {
        var response = Created("/customers/Companies/21", new { Id = 22, Name = "MALIEV", TaxNumber = "010" });
        var handler = new RecordingHandler(_ => response);

        var result = await Client(handler).CompleteAsync(Request(true), CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Unknown, result.Outcome);
        Assert.Equal(CncProfilePersistenceStage.Company, result.Stage);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task CompleteAsync_UnauthorizedWrite_InvalidatesTokenAndStops()
    {
        var tokens = new Tokens();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await Client(handler, tokens).CompleteAsync(Request(true), CancellationToken.None);

        Assert.Equal(CncProfilePersistenceOutcome.Failed, result.Outcome);
        Assert.True(tokens.Invalidated);
        Assert.Single(handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompleteAsync_PreflightFailure_PerformsNoNetworkCalls(bool cancelled)
    {
        var tokens = new Tokens { Token = cancelled ? "test-token" : null };
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not send"));
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();

        var result = await Client(handler, tokens).CompleteAsync(Request(true), cancellation.Token);

        Assert.Equal(CncProfilePersistenceOutcome.Failed, result.Outcome);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void AddInfrastructure_RegistersDedicatedPersistenceClient()
    {
        var services = new ServiceCollection();
        services.AddLegacyServiceClients(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ICncProfilePersistenceClient)
            && descriptor.ImplementationType == typeof(CncProfilePersistenceClient)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private static CncProfilePersistenceClient Client(RecordingHandler handler, Tokens? tokens = null) => new(new Factory(handler), tokens ?? new Tokens());

    private static CncProfilePersistenceRequest Request(bool shipToBilling) => new(42, 7,
        new CustomerAccountDetails(7, "", "", "", null, null, "fax-kept", "", new DateTime(1990, 1, 2), null, null, null, null, null, null, null, null),
        new CncProfileCompletion("Nat", "V", "n@example.test", "0800000000", "020000000", "MALIEV", "010",
            new CncProfileAddressFields("9", "Road", null, "Bangkok", "Bangkok", "10210"), "Thailand", shipToBilling,
            shipToBilling ? null : new CncProfileAddressFields(null, "Other", null, "Bangkok", "Bangkok", "10220"),
            shipToBilling ? null : "Missing"));

    private static CustomerAccountDetails ExistingCustomer()
    {
        var address = new CustomerAddress(31, "9", "Road", null, "Bangkok", "Bangkok", "10210", 138, null, null);
        var company = new CustomerCompany(21, "MALIEV", "010", "registrar-kept", null, null);
        return new(7, "Nat", "V", "Nat V", "020000000", "0800000000", "fax-kept", "n@example.test", new DateTime(1990, 1, 2), 21, 31, 31, null, null, address, company, address);
    }

    private static HttpResponseMessage Created(string location, object value)
    {
        var response = Json(HttpStatusCode.Created, value);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"),
    };

    private sealed record Call(string Method, string Path, string? Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new(request.Method.Method, request.RequestUri!.AbsolutePath,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class Factory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri($"https://{name}.example/") };
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        internal string? Token = "test-token";
        internal bool Invalidated;
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Token);
        public void Invalidate(string token) => Invalidated = true;
    }
}
