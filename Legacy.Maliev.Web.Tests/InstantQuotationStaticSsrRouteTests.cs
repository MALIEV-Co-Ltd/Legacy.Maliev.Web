using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public InstantQuotationStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public async Task BlazorRoute_OwnsDocumentGet_WhileLegacyEstimateQueryReturnsExactJsonContract()
    {
        using var client = CreateClient(factory);

        var page = await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en");
        using var response = await client.GetAsync(
            "/InstantQuotation/3D-Printing?handler=GetEstimate&material=PLA&dimensionZ=30&volume=20000&footprint=400&quantity=1&currency=USD");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("fdm", json.RootElement.GetProperty("process").GetString());
        Assert.Equal("THB", json.RootElement.GetProperty("currency").GetString());
        Assert.True(json.RootElement.TryGetProperty("tiers", out _));
    }

    [Fact]
    public async Task LegacyOrderTotalQuery_ReturnsExactJsonContract()
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync(
            "/InstantQuotation/3D-Printing?handler=GetOrderTotal&processes=fdm%2Cresin&subtotals=1200%2C1800&totalWeightGrams=500&totalBoundingCm3=2000&currency=USD");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(3_000, json.RootElement.GetProperty("printing").GetDouble(), 2);
        Assert.Equal("THB", json.RootElement.GetProperty("currency").GetString());
        Assert.True(json.RootElement.TryGetProperty("finalOrderPrice", out _));
    }

    [Fact]
    public async Task DisabledRoute_UsesRetainedRazorPageAndHandlers()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(
            builder => builder.UseSetting("BlazorRouting:InstantQuotation", "false"));
        using var client = CreateClient(fallbackFactory);

        var page = await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en");
        using var response = await client.GetAsync(
            "/InstantQuotation/3D-Printing?handler=GetEstimate&material=PLA&dimensionZ=30&volume=20000&footprint=400&quantity=1&currency=THB");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", page, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", page, StringComparison.Ordinal);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
}
