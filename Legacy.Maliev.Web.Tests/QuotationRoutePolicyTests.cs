using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class QuotationRoutePolicyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public QuotationRoutePolicyTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
    }

    [Fact]
    public void Middleware_ExposesTheConventionalPublicConstructorAndInvokeMethod()
    {
        Type middleware = typeof(QuotationRouteRedirectMiddleware);

        Assert.NotNull(middleware.GetConstructor([typeof(RequestDelegate)]));
        Assert.NotNull(middleware.GetMethod(nameof(QuotationRouteRedirectMiddleware.InvokeAsync), [typeof(HttpContext)]));
    }

    [Theory]
    [InlineData("3d-design")]
    [InlineData("3d-printing")]
    [InlineData("3d-scanning")]
    [InlineData("cnc-machining")]
    [InlineData("custom-manufacturing")]
    [InlineData("low-volume-injection-molding")]
    [InlineData("silicone-casting")]
    public async Task KnownLegacyServicePath_RedirectsAndPreservesQuery(string service)
    {
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/quotation/{service}";
        context.Request.QueryString = new QueryString("?culture=en&finder_service=cnc&item=obsolete");
        bool nextCalled = false;
        QuotationRouteRedirectMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        string location = context.Response.Headers.Location.ToString();
        Assert.StartsWith("/quotation?", location, StringComparison.Ordinal);
        Assert.Contains("culture=en", location, StringComparison.Ordinal);
        Assert.Contains("finder_service=cnc", location, StringComparison.Ordinal);
        Assert.Contains($"item={service}", location, StringComparison.Ordinal);
        Assert.DoesNotContain("item=obsolete", location, StringComparison.Ordinal);
        Assert.False(nextCalled);
    }

    [Theory]
    [InlineData("/quotation/arbitrary-slug")]
    [InlineData("/quotation/cnc-machining/process")]
    [InlineData("/quotation/cnc-machining/process/material")]
    public async Task UnknownOrSurplusPath_ReturnsNotFound(string path)
    {
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        bool nextCalled = false;
        QuotationRouteRedirectMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task BaseQuotation_IsNoindexAndExcludedFromSitemap()
    {
        using var client = this.factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var quotation = await client.GetAsync("/quotation?culture=en&item=3d-printing");
        string source = await quotation.Content.ReadAsStringAsync();
        using var sitemap = await client.GetAsync("/sitemap");
        string sitemapSource = await sitemap.Content.ReadAsStringAsync();
        string robots = await client.GetStringAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.OK, quotation.StatusCode);
        Assert.Equal("noindex, follow", quotation.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("<meta name=\"robots\" content=\"noindex,follow\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("https://www.maliev.com/quotation", sitemapSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Disallow: /quotation/*", robots, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpPipeline_ContainsLegacyQuotationPaths()
    {
        using var client = this.factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var known = await client.GetAsync("/quotation/3d-printing?culture=th&item=obsolete");
        using var unknown = await client.GetAsync("/quotation/3d-printing/sls");

        Assert.Equal(HttpStatusCode.MovedPermanently, known.StatusCode);
        Assert.Equal("/quotation?culture=th&item=3d-printing", known.Headers.Location?.OriginalString);
        Assert.Equal("noindex, follow", known.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("noindex, follow", unknown.Headers.GetValues("X-Robots-Tag").Single());
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
                true));
    }
}
