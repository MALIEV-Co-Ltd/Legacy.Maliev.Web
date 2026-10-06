using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class ThaiLookupClientTests
{
    [Fact]
    public async Task PostcodeFirstPreservesAllAndConstraintsAndStringCodes()
    {
        using var handler = new Handler(_ => Json("""
            {"datasetVersion":"fixture-1","items":[{"province":{"code":"01","nameTh":"จังหวัด","nameEn":null},
            "district":{"code":"0101","provinceCode":"01","nameTh":"อำเภอ","nameEn":null},
            "subdistrict":{"code":"010101","districtCode":"0101","nameTh":"ตำบล","nameEn":null},"postcode":"10100"}],
            "hasMore":true,"nextCursor":"next + page"}
            """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var client = new ThaiLookupClient(new Factory(http), new Tokens());
        var result = await client.SearchAddressAsync(new("", new("01", "0101", null, "๑๐๑๐๐"), Cursor: "next + page"), default);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("01", Assert.Single(result.Value!.Items).Province.Code);
        Assert.True(result.Value.HasMore);
        Assert.Contains("provinceCode=01", handler.Path, StringComparison.Ordinal);
        Assert.Contains("districtCode=0101", handler.Path, StringComparison.Ordinal);
        Assert.Contains("postcode=10100", handler.Path, StringComparison.Ordinal);
        Assert.Contains("cursor=next%20%2B%20page", handler.Path, StringComparison.Ordinal);
        Assert.Equal("Bearer fixture-token", handler.Authorization);
    }

    [Theory]
    [InlineData(400, 400)]
    [InlineData(422, 422)]
    [InlineData(429, 429)]
    [InlineData(401, 503)]
    [InlineData(403, 503)]
    [InlineData(500, 503)]
    [InlineData(503, 503)]
    public async Task ProviderFailureIsNotAnEmptySuccess(int downstream, int expected)
    {
        using var handler = new Handler(_ => new HttpResponseMessage((HttpStatusCode)downstream));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var tokens = new Tokens();
        var result = await new ThaiLookupClient(new Factory(http), tokens).SearchCompanyAsync(new("example"), default);
        Assert.Equal(expected, result.StatusCode);
        Assert.Null(result.Value);
        Assert.Equal(downstream is 401 or 403, tokens.Invalidated);
    }

    [Fact]
    public async Task SuggestionDoesNotInventMissingCompanyFacts()
    {
        using var handler = new Handler(_ => Json("""
            {"outcome":"matches","provider":"creden","capability":"suggestion","items":[{"nameTh":"ตัวอย่าง"}],"hasMore":false}
            """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var result = await new ThaiLookupClient(new Factory(http), new Tokens()).SearchCompanyAsync(new("ตัวอย่าง"), default);
        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.TaxId); Assert.Null(item.Status); Assert.Null(item.RegisteredAddress);
        Assert.Null(item.CompanyType); Assert.Null(item.Objectives); Assert.Null(item.SourceUrl);
    }

    [Fact]
    public async Task ResolveUsesPostAndRetainsOriginalDetailAndConflict()
    {
        using var handler = new Handler(_ => Json("""
            {"datasetVersion":"fixture","originalText":"36/1 road","normalizedText":"36/1 road","outcome":"conflict",
            "candidates":[],"hasMore":false,"uniqueFields":{},"detailText":"36/1 road","extractedSpans":[],
            "conflicts":["province"]}
            """));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var result = await new ThaiLookupClient(new Factory(http), new Tokens()).ResolveAddressAsync(new("36/1 road"), default);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/v1/thai-addresses/resolve", handler.Path);
        Assert.Equal("conflict", result.Value!.Outcome);
        Assert.Equal("36/1 road", result.Value.DetailText);
        Assert.Equal("province", Assert.Single(result.Value.Conflicts));
    }

    [Fact]
    public async Task InvalidBoundsNeverCallCatalog()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Invalid request reached Catalog."));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var client = new ThaiLookupClient(new Factory(http), new Tokens());
        Assert.Equal(400, (await client.SearchAddressAsync(new(new string('a', 129)), default)).StatusCode);
        Assert.Equal(400, (await client.SearchAddressAsync(new("", new(Postcode: "1234")), default)).StatusCode);
        Assert.Equal(400, (await client.ResolveAddressAsync(new(new string('a', 2049)), default)).StatusCode);
        Assert.Equal(400, (await client.SearchCompanyAsync(new("1", "tax-id"), default)).StatusCode);
        Assert.Equal(400, (await client.SearchAddressAsync(new("", new(ProvinceCode: "๑๒")), default)).StatusCode);
        Assert.Equal(400, (await client.ResolveAddressAsync(new("address", new(DistrictCode: "๑๒๐๖")), default)).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task MalformedOrNullReplyFailsClosed(string payload)
    {
        using var handler = new Handler(_ => Json(payload));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        Assert.Equal(503, (await new ThaiLookupClient(new Factory(http), new Tokens()).SearchAddressAsync(new("example"), default)).StatusCode);
    }

    [Fact]
    public async Task OversizedReplyFailsClosed()
    {
        using var handler = new Handler(_ => Json(new string(' ', 262145)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        Assert.Equal(503, (await new ThaiLookupClient(new Factory(http), new Tokens()).SearchAddressAsync(new("example"), default)).StatusCode);
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) { Assert.Equal("catalog", name); return client; } }
    private sealed class Tokens : IServiceAccessTokenProvider
    {
        public bool Invalidated { get; private set; }
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("fixture-token");
        public void Invalidate(string token) => Invalidated = true;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Path { get; private set; } = "";
        public string? Authorization { get; private set; }
        public HttpMethod? Method { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Path = request.RequestUri!.PathAndQuery; Method = request.Method; Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(reply(request));
        }
    }
}
