using System.Net;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace Legacy.Maliev.Web.Tests;

public sealed class WebSurfaceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public WebSurfaceTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Recaptcha:SiteKey"] = "test-site-key",
                            ["Recaptcha:ProjectId"] = "test-project"
                        }));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICareerClient>();
                    services.RemoveAll<ICountryClient>();
                    services.RemoveAll<IContactClient>();
                    services.RemoveAll<IAntiBotVerifier>();
                    services.AddSingleton<ICareerClient, StubCareerClient>();
                    services.AddSingleton<ICountryClient, StubCountryClient>();
                    services.AddSingleton<IContactClient, StubContactClient>();
                    services.AddSingleton<IAntiBotVerifier, StubAntiBotVerifier>();
                });
            })
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
    [InlineData("/career")]
    [InlineData("/contact")]
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

    [Fact]
    public async Task ContactPage_UsesConfiguredEnterpriseRecaptchaWithoutEmbeddedGoogleApiKey()
    {
        var source = await client.GetStringAsync("/contact");

        Assert.Contains("test-site-key", source, StringComparison.Ordinal);
        Assert.Contains("recaptcha/enterprise.js", source, StringComparison.Ordinal);
        Assert.Contains("name=\"g-recaptcha-response\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AIza", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceAuthentication", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientSecret", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CareerDetail_RendersServiceDataWithoutTrustingStoredHtml()
    {
        using var response = await client.GetAsync("/career/view/1");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", source, StringComparison.Ordinal);
    }

    private sealed class StubCareerClient : ICareerClient
    {
        private static readonly CareerLevel Level = new(1, "Engineer", null, null, null);

        private static readonly CareerOffer Offer = new(
            1,
            Level.Id,
            "Manufacturing Engineer",
            "<script>alert(1)</script>",
            "Build reliable manufacturing processes.",
            "Engineering experience.",
            "A practical team.",
            "Nonthaburi",
            false,
            null,
            null,
            Level);

        public Task<CareerListing> GetListingAsync(
            CareerSort sort,
            string? search,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new CareerListing(
                    [Level],
                    new CareerOfferPage([Offer], pageIndex, 1, 1, false, false),
                    true));

        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(
            int offerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<CareerOffer>(offerId == Offer.Id ? Offer : null, true));
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ServiceResponse<IReadOnlyList<Country>>(
                    [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
                    true));
    }

    private sealed class StubContactClient : IContactClient
    {
        public Task<ContactSubmissionResult> SubmitAsync(
            ContactSubmission submission,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ContactSubmissionResult(1, true, true));
    }

    private sealed class StubAntiBotVerifier : IAntiBotVerifier
    {
        public Task<bool> VerifyAsync(
            string? token,
            string expectedAction,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
