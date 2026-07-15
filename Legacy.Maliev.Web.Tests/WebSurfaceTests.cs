using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class WebSurfaceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public WebSurfaceTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("/web/liveness", "text/plain")]
    [InlineData("/web/readiness", "application/json")]
    [InlineData("/web/openapi/v1.json", "application/json")]
    [InlineData("/web/scalar/", "text/html")]
    public async Task OperationalAndScalarRoutes_ArePublished(string route, string mediaType)
    {
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith(mediaType, response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomePage_PreservesConsentSafeGoogleInstrumentation()
    {
        var source = await client.GetStringAsync("/");
        var consentIndex = source.IndexOf("gtag('consent', 'default'", StringComparison.Ordinal);
        var loaderIndex = source.IndexOf("(function (w, d, s, l, i)", StringComparison.Ordinal);

        Assert.True(consentIndex >= 0, "Consent Mode v2 must be declared in the document head.");
        Assert.True(loaderIndex > consentIndex, "Consent defaults must be queued before GTM loads.");
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
        Assert.Contains("google-site-verification", source, StringComparison.Ordinal);
        Assert.Contains("data-consent-action=\"accept\"", source, StringComparison.Ordinal);
        Assert.Contains("data-consent-action=\"reject\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("analytics.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UA-133315708-1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GTM-5VBH5LK", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/services")]
    [InlineData("/services/custom-manufacturing")]
    [InlineData("/services/cnc-machining")]
    [InlineData("/services/3d-printing")]
    [InlineData("/services/3d-scanning")]
    [InlineData("/about")]
    [InlineData("/about/socialmedia")]
    [InlineData("/legal")]
    [InlineData("/legal/privacypolicy")]
    [InlineData("/legal/termsconditions")]
    [InlineData("/legal/nondisclosureagreement")]
    [InlineData("/knowledges")]
    [InlineData("/knowledges/guidelines")]
    [InlineData("/knowledges/workflow")]
    [InlineData("/knowledges/specifications")]
    [InlineData("/knowledges/specifications/cnc-machining")]
    [InlineData("/knowledges/specifications/3d-printing")]
    [InlineData("/knowledges/specifications/3d-scanning")]
    public async Task MigratedPublicRoutes_RenderCanonicalLocalizedDocuments(string route)
    {
        using var response = await client.GetAsync(route);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rel=\"canonical\"", source, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\"", source, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"th\"", source, StringComparison.Ordinal);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
    }
}
