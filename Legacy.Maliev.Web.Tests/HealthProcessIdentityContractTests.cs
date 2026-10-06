using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>HTTP consumer regression for source 2f60 health process identity.</summary>
public sealed class HealthProcessIdentityContractTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Header = "X-Maliev-Health-Instance";

    /// <summary>Actual registered Web health responses identify the serving process and remain uncached.</summary>
    [Theory]
    [InlineData("/web/liveness")]
    [InlineData("/web/readiness")]
    public async Task RegisteredHealth_IdentifiesProcessAndRemainsUncached(string path)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertMarker(response);
    }

    /// <summary>Both health operations share one process marker across repeated requests.</summary>
    [Fact]
    public async Task HealthMarker_IsStableAcrossHealthOperationsAndRequests()
    {
        using var client = Client(factory);
        using var first = await client.GetAsync("/web/liveness");
        using var readiness = await client.GetAsync("/web/readiness");
        using var repeated = await client.GetAsync("/web/liveness");
        Assert.Equal(AssertMarker(first), AssertMarker(readiness));
        Assert.Equal(AssertMarker(first), AssertMarker(repeated));
    }

    /// <summary>Independent hosts cannot share a process identity merely because their build identity matches.</summary>
    [Fact]
    public async Task IndependentHosts_HaveDifferentProcessMarkers()
    {
        using var other = new TestingWebApplicationFactory();
        using var firstClient = Client(factory);
        using var otherClient = Client(other);
        using var first = await firstClient.GetAsync("/web/liveness");
        using var second = await otherClient.GetAsync("/web/liveness");
        Assert.NotEqual(AssertMarker(first), AssertMarker(second));
    }

    /// <summary>Business paths, Aspire orchestration and unsupported methods do not gain the health identity header.</summary>
    [Theory]
    [InlineData("GET", "/InstantQuotation/3D-Printing?culture=en")]
    [InlineData("GET", "/web/aspire-liveness")]
    [InlineData("POST", "/web/liveness")]
    public async Task OtherPathsAndMethods_DoNotReceiveHealthMarker(string method, string path)
    {
        using var client = Client(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains(Header));
    }

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static string AssertMarker(HttpResponseMessage response)
    {
        Assert.True(response.Headers.Contains(Header));
        var marker = Assert.Single(response.Headers.GetValues(Header));
        Assert.Matches("^[a-f0-9]{32}$", marker);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return marker;
    }
}
