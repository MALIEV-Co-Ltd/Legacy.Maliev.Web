using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class ThaiLookupBoundaryTests
{
    [Theory]
    [InlineData("/lookups/addresses/search")]
    [InlineData("/lookups/addresses/resolve")]
    [InlineData("/lookups/companies/search")]
    public async Task MissingCsrfTokenCannotReachWorkloadClient(string route)
    {
        var lookup = new Stub();
        await using var app = await Start(lookup);
        using var http = app.GetTestClient();
        using var reply = await http.PostAsJsonAsync(route, new { q = "example", text = "example" });
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Equal(0, lookup.Calls);
        Assert.Contains("no-store", reply.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(422)]
    [InlineData(503)]
    public async Task AnonymousLookupUsesServerBoundaryAndRetainsFailureCategory(int resultStatus)
    {
        var lookup = new Stub { ResultStatus = resultStatus };
        await using var app = await Start(lookup);
        using var http = app.GetTestClient();
        await AddToken(http);
        using var reply = await http.PostAsJsonAsync("/lookups/companies/search", new { q = "example" });
        Assert.Equal(resultStatus, (int)reply.StatusCode);
        Assert.Equal(1, lookup.Calls);
        Assert.DoesNotContain("fixture-token", await reply.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateLimitRejectsBeforeCallingCatalogAgain()
    {
        var lookup = new Stub();
        await using var app = await Start(lookup);
        using var http = app.GetTestClient();
        await AddToken(http);
        for (var index = 0; index < 30; index++)
        {
            using var reply = await http.PostAsJsonAsync("/lookups/companies/search", new { q = "example" });
            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        }
        using var rejected = await http.PostAsJsonAsync("/lookups/companies/search", new { q = "example" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(30, lookup.Calls);
    }

    private static async Task<WebApplication> Start(Stub lookup)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton<IThaiLookupClient>(lookup);
        builder.Services.AddThaiLookupBoundary();
        var app = builder.Build();
        app.UseRateLimiter();
        app.MapThaiLookupEndpoints();
        app.MapGet("/fixture-token", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Json(antiforgery.GetAndStoreTokens(context).RequestToken));
        await app.StartAsync();
        return app;
    }

    private static async Task AddToken(HttpClient http)
    {
        using var reply = await http.GetAsync("/fixture-token");
        var token = await reply.Content.ReadFromJsonAsync<string>();
        http.DefaultRequestHeaders.Add("Cookie", string.Join("; ", reply.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])));
        http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
    }

    private sealed class Stub : IThaiLookupClient
    {
        public int Calls { get; private set; }
        public int ResultStatus { get; init; } = 200;
        public Task<LookupResult<CompanyLookupPage>> SearchCompanyAsync(CompanyLookupQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new LookupResult<CompanyLookupPage>(new("no-match", "creden", "suggestion", [], false), ResultStatus));
        }
        public Task<LookupResult<ThaiAddressPage>> SearchAddressAsync(ThaiAddressQuery query, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new LookupResult<ThaiAddressPage>(new("fixture", [], false, null), 200)); }
        public Task<LookupResult<ThaiAddressResolution>> ResolveAddressAsync(ThaiAddressResolveRequest request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Missing-CSRF resolve must not reach Catalog."); }
    }
}
