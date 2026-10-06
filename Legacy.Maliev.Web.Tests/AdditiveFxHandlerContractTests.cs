using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

// The token below is transport-only test data. These tests do not establish Catalog actor permissions.
public sealed class AdditiveFxHandlerContractTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    public static TheoryData<bool, string> RenderersAndCultures => new()
    {
        { true, "en" }, { true, "th" }, { false, "en" }, { false, "th" },
    };

    public static IEnumerable<object[]> FailureCases =>
        from active in new[] { true, false }
        from handler in new[] { "GetEstimate", "GetOrderTotal" }
        from failure in new[] { "missing", "zero", "negative", "malformed", "overflow", "wrong-base", "invalid-json", "unauthorized", "forbidden", "unavailable", "timeout" }
        select new object[] { active, handler, failure };

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    public async Task Estimate_ForeignCurrency_ConvertsDisplayFieldsAndPreservesThb(bool active, string culture)
    {
        using var transport = new CatalogTransport(_ => Rate("0.025"));
        await using var application = Configure(active, transport);
        using var client = CreateClient(application);
        using var thb = await ReadSuccessAsync(client, "GetEstimate", "THB", culture);
        using var usd = await ReadSuccessAsync(client, "GetEstimate", " usd ", culture);

        Assert.Equal("USD", usd.RootElement.GetProperty("currency").GetString());
        foreach (var field in new[] { "unitPrice", "subtotal", "technicalFilamentMinimumPrice", "technicalFilamentMinimumAdjustment" })
        {
            Assert.Equal(Converted(thb.RootElement.GetProperty(field).GetDecimal(), 0.025m), usd.RootElement.GetProperty(field).GetDecimal());
        }
        Assert.Equal(thb.RootElement.GetProperty("subtotalThb").GetDecimal(), usd.RootElement.GetProperty("subtotalThb").GetDecimal());
        foreach (var field in new[] { "weightGrams", "boundingCm3", "printTimeMinutes", "materialPerUnit" })
        {
            Assert.Equal(thb.RootElement.GetProperty(field).GetDecimal(), usd.RootElement.GetProperty(field).GetDecimal());
        }
        var thbTiers = thb.RootElement.GetProperty("tiers").EnumerateArray().ToArray();
        var usdTiers = usd.RootElement.GetProperty("tiers").EnumerateArray().ToArray();
        Assert.Equal(thbTiers.Length, usdTiers.Length);
        for (var index = 0; index < thbTiers.Length; index++)
        {
            Assert.Equal(thbTiers[index].GetProperty("minQuantity").GetInt32(), usdTiers[index].GetProperty("minQuantity").GetInt32());
            Assert.Equal(Converted(thbTiers[index].GetProperty("unitPrice").GetDecimal(), 0.025m), usdTiers[index].GetProperty("unitPrice").GetDecimal());
        }
        AssertCatalogRequest(Assert.Single(transport.Requests), "USD");
        Assert.DoesNotContain("fx-contract-test-token", usd.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    public async Task OrderTotal_ForeignCurrency_ConvertsEveryMonetaryField(bool active, string culture)
    {
        using var transport = new CatalogTransport(_ => Rate("0.025"));
        await using var application = Configure(active, transport);
        using var client = CreateClient(application);
        using var thb = await ReadSuccessAsync(client, "GetOrderTotal", "THB", culture);
        using var usd = await ReadSuccessAsync(client, "GetOrderTotal", "USD", culture);

        Assert.Equal("USD", usd.RootElement.GetProperty("currency").GetString());
        foreach (var field in new[] { "printing", "itemsSubtotal", "minimumOrderPrice", "minimumOrderSurcharge", "shipping", "vat", "priceBeforeVat", "finalOrderPrice" })
        {
            Assert.Equal(Converted(thb.RootElement.GetProperty(field).GetDecimal(), 0.025m), usd.RootElement.GetProperty(field).GetDecimal());
        }
        Assert.Equal(thb.RootElement.GetProperty("shippingState").GetString(), usd.RootElement.GetProperty("shippingState").GetString());
        AssertCatalogRequest(Assert.Single(transport.Requests), "USD");
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task ForeignCurrency_UnusableCatalogObservation_FailsClosed(bool active, string handler, string failure)
    {
        using var transport = new CatalogTransport(_ => Failure(failure));
        await using var application = Configure(active, transport);
        using var client = CreateClient(application);
        using var response = await client.GetAsync(Route(handler, "USD", "en"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("pricing_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("finalOrderPrice", out _));
        Assert.False(json.RootElement.TryGetProperty("unitPrice", out _));
        Assert.NotEmpty(transport.Requests);
        Assert.All(transport.Requests, request => AssertCatalogRequest(request, "USD"));
        Assert.DoesNotContain("fx-contract-test-token", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    public async Task ThaiBahtIdentity_DoesNotCallCatalog(bool active, string culture)
    {
        using var transport = new CatalogTransport(_ => throw new InvalidOperationException("THB identity must not call a provider."));
        await using var application = Configure(active, transport, token: null);
        using var client = CreateClient(application);
        using var estimate = await ReadSuccessAsync(client, "GetEstimate", "", culture);
        using var total = await ReadSuccessAsync(client, "GetOrderTotal", " thb ", culture);

        Assert.Equal("THB", estimate.RootElement.GetProperty("currency").GetString());
        Assert.Equal("THB", total.RootElement.GetProperty("currency").GetString());
        Assert.Equal(3100m, total.RootElement.GetProperty("priceBeforeVat").GetDecimal());
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignCurrency_MissingServerToken_FailsClosedWithoutOutboundCall(bool active)
    {
        using var transport = new CatalogTransport(_ => throw new InvalidOperationException("No token means no provider call."));
        await using var application = Configure(active, transport, token: null);
        using var client = CreateClient(application);
        using var response = await client.GetAsync(Route("GetEstimate", "USD", "en"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("pricing_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.Empty(transport.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Snapshot_SameSession_ReusesAcrossHandlersUntilThirtyMinuteExpiry(bool active)
    {
        var clock = new AdjustableClock();
        using var transport = new CatalogTransport(call => Rate(call == 1 ? "0.025" : "0.030"));
        await using var application = Configure(active, transport, clock);
        using var client = CreateClient(application);
        using var estimate = await ReadSuccessAsync(client, "GetEstimate", "USD", "en");
        clock.Advance(TimeSpan.FromMinutes(29));
        using var cached = await ReadSuccessAsync(client, "GetOrderTotal", "USD", "th");
        Assert.Equal(77.50m, cached.RootElement.GetProperty("priceBeforeVat").GetDecimal());
        Assert.Single(transport.Requests);

        clock.Advance(TimeSpan.FromMinutes(1));
        using var refreshed = await ReadSuccessAsync(client, "GetOrderTotal", "USD", "en");
        Assert.Equal(93m, refreshed.RootElement.GetProperty("priceBeforeVat").GetDecimal());
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Snapshot_ExpiredObservationAndProviderFailure_DoesNotReuseStalePrice(bool active)
    {
        var clock = new AdjustableClock();
        using var transport = new CatalogTransport(call => call == 1 ? Rate("0.025") : new(HttpStatusCode.ServiceUnavailable));
        await using var application = Configure(active, transport, clock);
        using var client = CreateClient(application);
        using var estimate = await ReadSuccessAsync(client, "GetEstimate", "USD", "en");
        clock.Advance(TimeSpan.FromMinutes(30));
        using var response = await client.GetAsync(Route("GetOrderTotal", "USD", "en"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("pricing_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("finalOrderPrice", out _));
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Snapshot_DifferentAnonymousSessions_DoNotShareObservation(bool active)
    {
        using var transport = new CatalogTransport(call => Rate(call == 1 ? "0.025" : "0.030"));
        await using var application = Configure(active, transport);
        using var firstClient = CreateClient(application);
        using var secondClient = CreateClient(application);
        using var first = await ReadSuccessAsync(firstClient, "GetOrderTotal", "USD", "en");
        using var second = await ReadSuccessAsync(secondClient, "GetOrderTotal", "USD", "en");

        Assert.Equal(77.50m, first.RootElement.GetProperty("priceBeforeVat").GetDecimal());
        Assert.Equal(93m, second.RootElement.GetProperty("priceBeforeVat").GetDecimal());
        Assert.Equal(2, transport.Requests.Count);
    }

    private WebApplicationFactory<Program> Configure(
        bool active,
        CatalogTransport transport,
        TimeProvider? clock = null,
        string? token = "fx-contract-test-token") => factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("BlazorRouting:InstantQuotation", active ? "true" : "false");
            builder.UseSetting("Recaptcha:ProjectId", "test-project");
            builder.UseSetting("Recaptcha:SiteKey", "test-site-key");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new CatalogOnlyClientFactory(transport));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new StubTokenProvider(token));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock ?? new AdjustableClock());
                services.RemoveAll<IInstantQuotationSubmissionService>();
                services.AddSingleton<IInstantQuotationSubmissionService, UnusedSubmissionService>();
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> application) => application.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    private static string Route(string handler, string currency, string culture) => QueryHelpers.AddQueryString(
        "/InstantQuotation/3D-Printing",
        new Dictionary<string, string?>
        {
            ["handler"] = handler,
            ["currency"] = currency,
            ["culture"] = culture,
            ["material"] = "PLA",
            ["dimensionZ"] = "30",
            ["volume"] = "20000",
            ["footprint"] = "400",
            ["quantity"] = "1",
            ["processes"] = "fdm,resin",
            ["subtotals"] = "1200,1800",
            ["totalWeightGrams"] = "500",
            ["totalBoundingCm3"] = "2000",
        });

    private static async Task<JsonDocument> ReadSuccessAsync(HttpClient client, string handler, string currency, string culture)
    {
        using var response = await client.GetAsync(Route(handler, currency, culture));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        return json;
    }

    private static decimal Converted(decimal amount, decimal rate) => Math.Round(amount * rate, 2);

    private static void AssertCatalogRequest(RecordedRequest request, string target)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("catalog.test", request.Uri.Host);
        Assert.Equal("/currencies/exchangerates", request.Uri.AbsolutePath.TrimEnd('/'));
        var query = QueryHelpers.ParseQuery(request.Uri.Query);
        Assert.Equal(2, query.Count);
        Assert.Equal("THB", query["baseCurrency"].ToString());
        Assert.Equal(target, query["targetCurrency"].ToString());
        Assert.Equal("Bearer fx-contract-test-token", request.Authorization);
    }

    private static HttpResponseMessage Rate(string rate) => Json("{\"Base\":\"THB\",\"Date\":\"2026-10-04T00:00:00Z\",\"Rates\":{\"USD\":\"" + rate + "\"}}");

    private static HttpResponseMessage Failure(string failure) => failure switch
    {
        "missing" => Json("{\"Base\":\"THB\",\"Date\":\"2026-10-04T00:00:00Z\",\"Rates\":{\"EUR\":\"0.025\"}}"),
        "zero" => Rate("0"),
        "negative" => Rate("-0.025"),
        "malformed" => Rate("not-a-rate"),
        "overflow" => Rate("7922816251426433759354395033500"),
        "wrong-base" => Json("{\"Base\":\"USD\",\"Date\":\"2026-10-04T00:00:00Z\",\"Rates\":{\"USD\":\"1\"}}"),
        "invalid-json" => Json("not-json"),
        "unauthorized" => new(HttpStatusCode.Unauthorized),
        "forbidden" => new(HttpStatusCode.Forbidden),
        "unavailable" => new(HttpStatusCode.ServiceUnavailable),
        "timeout" => throw new Polly.Timeout.TimeoutRejectedException("Controlled Catalog transport timeout."),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization);

    private sealed class CatalogTransport(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString()));
            return Task.FromResult(response(Requests.Count));
        }
    }

    private sealed class CatalogOnlyClientFactory(CatalogTransport transport) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("catalog", name);
            return new HttpClient(transport, disposeHandler: false) { BaseAddress = new Uri("https://catalog.test/") };
        }
    }

    private sealed class StubTokenProvider(string? token) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(token);
        public void Invalidate(string invalidatedToken) { }
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class UnusedSubmissionService : IInstantQuotationSubmissionService
    {
        public Task<InstantQuotationSubmissionResult> SubmitAsync(string sessionId, string? ownerIdentity, InstantQuotationCustomerSubmission customer, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("FX handler reads must not submit an order.");
    }
}
