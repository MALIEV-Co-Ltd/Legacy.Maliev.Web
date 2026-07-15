using System.Net;
using System.Xml.Linq;
using System.Text.RegularExpressions;
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
                builder.UseEnvironment("Testing");
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
                    services.RemoveAll<IQuotationClient>();
                    services.RemoveAll<IQuotationFileClient>();
                    services.RemoveAll<INotificationClient>();
                    services.RemoveAll<ICustomerAuthenticationClient>();
                    services.RemoveAll<ICustomerProfileClient>();
                    services.RemoveAll<IAntiBotVerifier>();
                    services.AddSingleton<ICareerClient, StubCareerClient>();
                    services.AddSingleton<ICountryClient, StubCountryClient>();
                    services.AddSingleton<IContactClient, StubContactClient>();
                    services.AddSingleton<IQuotationClient, StubQuotationClient>();
                    services.AddSingleton<IQuotationFileClient, StubQuotationFileClient>();
                    services.AddSingleton<INotificationClient, StubNotificationClient>();
                    services.AddSingleton<ICustomerAuthenticationClient, StubCustomerAuthenticationClient>();
                    services.AddSingleton<ICustomerProfileClient, StubCustomerProfileClient>();
                    services.AddSingleton<IAntiBotVerifier, StubAntiBotVerifier>();
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
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
    [InlineData("/quotation")]
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

    [Theory]
    [InlineData("/account")]
    [InlineData("/account/accessdenied")]
    [InlineData("/account/forgotpassword")]
    [InlineData("/account/login")]
    [InlineData("/account/signup")]
    public async Task AccountInteractiveRoutes_RenderWithoutAuthentication(string route)
    {
        using var response = await client.GetAsync(route);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/account/changeemailconfirmation")]
    [InlineData("/account/emailconfirmation")]
    [InlineData("/account/resetpassword")]
    public async Task AccountChallengeRoutes_RejectMissingChallengeData(string route)
    {
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_UsesOpaqueHardenedCookieAndRejectsExternalReturnUrl()
    {
        var form = await GetAntiforgeryFormAsync("/account/login");
        form["Email"] = "customer@example.com";
        form["Password"] = "correct-password";
        form["RememberMe"] = "false";
        form["ReturnUrl"] = "https://attacker.example/steal";

        using var response = await client.PostAsync(
            "/account/login?handler=Login",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account", response.Headers.Location?.OriginalString);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-Maliev.Legacy.Session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-access-token", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-refresh-token", cookie, StringComparison.Ordinal);

        var account = await client.GetStringAsync("/account");
        Assert.Contains("customer@example.com", account, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_DoesNotPromoteUntrustedQueryTextIntoTrustedAlert()
    {
        using var response = await client.GetAsync(
            "/account/login?message=Send%20your%20password%20to%20support");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Send your password to support", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForgotPassword_UsesSamePublicResponseForUnknownEmail()
    {
        var form = await GetAntiforgeryFormAsync("/account/forgotpassword");
        form["Email"] = "unknown@example.com";

        using var response = await client.PostAsync(
            "/account/forgotpassword?handler=PasswordReset",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var source = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains(
            "If an eligible account exists, a password reset link has been sent.",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("not existed", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not found", source, StringComparison.OrdinalIgnoreCase);
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
    public async Task QuotationPage_PreservesPrefillAndSecureUploadContract()
    {
        var source = await client.GetStringAsync(
            "/quotation?culture=en&item=3d-printing&process=sls&material=pa12");

        Assert.Contains("I want: 3d-printing", source, StringComparison.Ordinal);
        Assert.Contains("Please use: sls", source, StringComparison.Ordinal);
        Assert.Contains("Material: pa12", source, StringComparison.Ordinal);
        Assert.Contains("enctype=\"multipart/form-data\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"g-recaptcha-response\"", source, StringComparison.Ordinal);
        Assert.Contains("100 MB", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AIza", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PayPal", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sitemap_PublishesEveryIndexedRouteWithLocalizedAlternates()
    {
        using var response = await client.GetAsync("/sitemap");
        var xml = await response.Content.ReadAsStringAsync();
        var document = XDocument.Parse(xml);
        XNamespace sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        var routes = document.Root!.Elements(sitemap + "url").ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(22, routes.Length);
        Assert.Contains(routes, route => route.Element(sitemap + "loc")?.Value == "https://www.maliev.com/contact");
        Assert.Contains(routes, route => route.Element(sitemap + "loc")?.Value == "https://www.maliev.com/quotation");
        Assert.All(routes, route => Assert.Equal(3, route.Elements(xhtml + "link").Count()));
        Assert.DoesNotContain("/account", xml, StringComparison.OrdinalIgnoreCase);
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

    private sealed class StubQuotationClient : IQuotationClient
    {
        public Task<QuotationRequestResult> CreateRequestAsync(
            QuotationRequestSubmission submission,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(new QuotationRequestResult(1, true, true));
    }

    private sealed class StubQuotationFileClient : IQuotationFileClient
    {
        public Task<QuotationFileResult> UploadAndLinkAsync(
            int requestId,
            Guid submissionId,
            IReadOnlyList<QuotationUpload> files,
            CancellationToken cancellationToken) =>
            Task.FromResult(new QuotationFileResult(true, true, true, false));
    }

    private sealed class StubAntiBotVerifier : IAntiBotVerifier
    {
        public Task<bool> VerifyAsync(
            string? token,
            string expectedAction,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubNotificationClient : INotificationClient
    {
        public Task<NotificationResult> SendAsync(
            NotificationChannel channel,
            EmailNotification notification,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationResult(true, true, true));
    }

    private async Task<Dictionary<string, string>> GetAntiforgeryFormAsync(string path)
    {
        var source = await client.GetStringAsync(path);
        var match = Regex.Match(
            source,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The Razor form must include an antiforgery token.");
        return new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
        };
    }

    private sealed class StubCustomerAuthenticationClient : ICustomerAuthenticationClient
    {
        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAuthenticationResult(
                new CustomerTokenSet(
                    "sensitive-access-token",
                    "sensitive-refresh-token",
                    "Bearer",
                    900,
                    DateTimeOffset.UtcNow.AddDays(1)),
                true));

        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAuthenticationResult(null, true));

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerIdentityRegistration(true, "identity-1", databaseId, email));

        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerActionChallenge(true, "confirmation-token", true, true));

        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerActionChallenge(true, null, true, true));

        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubCustomerProfileClient : ICustomerProfileClient
    {
        public Task<CustomerProfileResult> CreateAsync(string firstName, string lastName, string email, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerProfileResult(new CustomerProfile(42, firstName, lastName, email), true, true));

        public Task<bool> DeleteAsync(int customerId, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
