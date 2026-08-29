namespace Legacy.Maliev.Web.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class RouteScopedAssetRenderingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public RouteScopedAssetRenderingTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
    }

    [Theory]
    [InlineData("/", "route-home.css", null)]
    [InlineData("/services", "route-services-index.css", "route-service-finder.js")]
    [InlineData("/services/3d-printing", "route-services.css", "route-service-printing.js")]
    [InlineData("/services/cnc-machining", "route-services.css", "route-service-cnc.js")]
    [InlineData("/about", "route-about.css", null)]
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
