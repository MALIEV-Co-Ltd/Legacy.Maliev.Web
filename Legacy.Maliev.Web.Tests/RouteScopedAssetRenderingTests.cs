namespace Legacy.Maliev.Web.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Legacy.Maliev.Web.Application;

public sealed class RouteScopedAssetRenderingTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly HttpClient client;
    private readonly TestingWebApplicationFactory factory;

    public RouteScopedAssetRenderingTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    [Theory]
    [InlineData("Home", "/Index", "route-home.css", "route-home.js")]
    [InlineData("About", "/About/Index", "route-about.css", "route-about.js")]
    [InlineData("Contact", "/Contact/Index", "route-inquiry.css", "route-inquiry.js")]
    [InlineData("Services", "/Services/Index", "route-services-index.css", "route-service-finder.js")]
    public async Task IndexAlias_RendersOwnedAssetsInBothRouteOwners(string feature, string route, string style, string script)
    {
        foreach (var active in new[] { true, false })
        {
            await using var host = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting($"BlazorRouting:{feature}", active.ToString());
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICountryClient>();
                    services.AddSingleton<ICountryClient>(new PublicIndexAliasHttpTests.CountryStub());
                });
            });
            using var aliasClient = host.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
            using var response = await aliasClient.GetAsync(route);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains($"/dist/{style}?v=", html, StringComparison.Ordinal);
            Assert.Contains($"/dist/{script}?v=", html, StringComparison.Ordinal);
            if (feature == "Services")
            {
                Assert.DoesNotContain("/dist/route-services.css?v=", html, StringComparison.Ordinal);
                Assert.DoesNotContain("/dist/route-service-toc.js?v=", html, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("/", "route-home.css", "route-home.js")]
    [InlineData("/services", "route-services-index.css", "route-service-finder.js")]
    [InlineData("/services/3d-printing", "route-services.css", "route-service-printing.js")]
    [InlineData("/services/cnc-machining", "route-services.css", "route-service-cnc.js")]
    [InlineData("/about", "route-about.css", "route-about.js")]
    public async Task PublicRoutes_RenderOnlyTheirOwnedAssets(string route, string style, string? script)
    {
        using var response = await client.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains($"/dist/{style}?v=", html, StringComparison.Ordinal);
        if (script is not null)
        {
            Assert.Contains($"/dist/{script}?v=", html, StringComparison.Ordinal);
        }

        Assert.Contains("/dist/site.min.css?v=", html, StringComparison.Ordinal);
        Assert.Contains("/dist/app.min.js?v=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountRoute_DoesNotRenderAnyRouteOwnedAsset()
    {
        using var response = await client.GetAsync("/account/login");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain("/dist/route-", html, StringComparison.Ordinal);
    }
}
