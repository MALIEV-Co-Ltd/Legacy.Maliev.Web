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
    private readonly WebApplicationFactory<Program> configuredFactory;
    private readonly HttpClient client;

    public WebSurfaceTests(WebApplicationFactory<Program> factory)
    {
        configuredFactory = factory.WithWebHostBuilder(builder =>
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
                    services.RemoveAll<ICustomerAccountClient>();
                    services.RemoveAll<ICustomerOrderClient>();
                    services.RemoveAll<IAntiBotVerifier>();
                    services.AddSingleton<ICareerClient, StubCareerClient>();
                    services.AddSingleton<ICountryClient, StubCountryClient>();
                    services.AddSingleton<IContactClient, StubContactClient>();
                    services.AddSingleton<IQuotationClient, StubQuotationClient>();
                    services.AddSingleton<IQuotationFileClient, StubQuotationFileClient>();
                    services.AddSingleton<INotificationClient, StubNotificationClient>();
                    services.AddSingleton<ICustomerAuthenticationClient, StubCustomerAuthenticationClient>();
                    services.AddSingleton<ICustomerProfileClient, StubCustomerProfileClient>();
                    services.AddSingleton<ICustomerAccountClient, StubCustomerAccountClient>();
                    services.AddSingleton<ICustomerOrderClient, StubCustomerOrderClient>();
                    services.AddSingleton<IAntiBotVerifier, StubAntiBotVerifier>();
                });
            });
        client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    [Theory]
    [InlineData("/member/account/manage/address")]
    [InlineData("/member/account/manage/changeemail")]
    [InlineData("/member/account/manage/changepassword")]
    [InlineData("/member/account/manage/createpassword")]
    [InlineData("/member/account/manage/profile")]
    [InlineData("/member/orders")]
    [InlineData("/member/orders/history")]
    [InlineData("/member/orders/view?itemID=7")]
    public async Task MemberRoutes_RedirectAnonymousUsersToLocalLogin(string route)
    {
        using var anonymous = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await anonymous.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("localhost", location.Host);
        Assert.Equal("/Account/Login", location.AbsolutePath);
        Assert.Contains("ReturnUrl=", location.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRoute_ReExecutesErrorPageWithoutExposingRequestContext()
    {
        using var response = await client.GetAsync("/definitely-not-a-maliev-route?culture=en");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<p class=\"maliev-eyebrow\">404</p>", content, StringComparison.Ordinal);
        Assert.Contains("noindex", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customer@example.com", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REFERRER", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnhandledPageException_RendersGenericHtmlWithoutExceptionDetails()
    {
        using var throwingFactory = configuredFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICareerClient>();
                services.AddSingleton<ICareerClient, ThrowingCareerClient>();
            }));
        using var throwingClient = throwingFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await throwingClient.GetAsync("/career?culture=en");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Something did not work properly", content, StringComparison.Ordinal);
        Assert.Contains("Request ID", content, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive exception detail", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"statusCode\"", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetiredPaymentSuccessRoute_IsAuthenticatedAndDoesNotProcessPayment()
    {
        using var anonymous = await client.GetAsync(
            "/member/quotations/paymentsuccess?paymentId=untrusted&invoice=untrusted");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal("/Account/Login", anonymous.Headers.Location?.AbsolutePath);

        await SignInAsync();
        using var authenticated = await client.GetAsync(
            "/member/quotations/paymentsuccess?paymentId=untrusted&invoice=untrusted");

        Assert.Equal(HttpStatusCode.Redirect, authenticated.StatusCode);
        Assert.Equal("/Member/Quotations", authenticated.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SignedInMember_AddressAndOrderRoutesPreserveSecureBffContract()
    {
        await SignInAsync();

        var account = await client.GetStringAsync("/account?culture=en");
        var address = await client.GetStringAsync("/member/account/manage/address");
        var orders = await client.GetStringAsync("/member/orders?culture=en");

        Assert.Contains("/Member/Account/Manage/Address", account, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Member/Account/Manage/Profile", account, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Member/Account/Manage/ChangeEmail", account, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Member/Account/Manage/ChangePassword", account, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Member/Orders", account, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"BillingAddress1\"", address, StringComparison.Ordinal);
        Assert.Contains("name=\"ShippingAddress1\"", address, StringComparison.Ordinal);
        Assert.Contains("Thailand", address, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", address, StringComparison.Ordinal);
        Assert.Contains("noindex,follow", address, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-access-token", address, StringComparison.Ordinal);
        Assert.DoesNotContain("service-token", address, StringComparison.Ordinal);
        Assert.Contains("Start or review an order", orders, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignedInMember_OrderHistoryDetailAndCancellationStayInsideOwnedBffBoundary()
    {
        await SignInAsync();

        var history = await client.GetStringAsync("/member/orders/history?search=CNC");
        var detail = await client.GetStringAsync("/member/orders/view?itemID=7");

        Assert.Contains("Part", history, StringComparison.Ordinal);
        Assert.DoesNotContain("The Sort field is required.", history, StringComparison.Ordinal);
        Assert.Contains("/Member/Orders/View?itemID=7", history, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CNC", detail, StringComparison.Ordinal);
        Assert.Contains("Reviewing", detail, StringComparison.Ordinal);
        Assert.Contains("orders/part.step", detail, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-access-token", history, StringComparison.Ordinal);
        Assert.DoesNotContain("service-token", detail, StringComparison.Ordinal);

        var form = await GetAntiforgeryFormAsync("/member/orders/view?itemID=7");
        form["orderId"] = "7";
        using var response = await client.PostAsync(
            "/member/orders/view?handler=CancelOrder",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/Member/Orders/View?itemID=7",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AddressUpdate_UsesAntiforgeryAndRedirectAfterPost()
    {
        await SignInAsync();
        var form = await GetAntiforgeryFormAsync("/member/account/manage/address");
        form["BillingAddress1"] = "1 Billing Rd";
        form["BillingCity"] = "Bangkok";
        form["BillingCountryId"] = "764";
        form["ShippingAddress1"] = "2 Shipping Rd";
        form["ShippingCity"] = "Bangkok";
        form["ShippingCountryId"] = "764";

        using var response = await client.PostAsync(
            "/member/account/manage/address?handler=UpdateAddress",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/member/account/manage/address", response.Headers.Location?.OriginalString?.ToLowerInvariant());
    }

    [Fact]
    public async Task SignedInMember_CredentialFormsStayInsideOpaqueBffSession()
    {
        await SignInAsync();

        var changeEmail = await client.GetStringAsync("/member/account/manage/changeemail");
        var changePassword = await client.GetStringAsync("/member/account/manage/changepassword");
        var profile = await client.GetStringAsync("/member/account/manage/profile");
        using var createPassword = await client.GetAsync("/member/account/manage/createpassword");

        Assert.Contains("name=\"NewEmail\"", changeEmail, StringComparison.Ordinal);
        Assert.Contains("name=\"CurrentPassword\"", changeEmail, StringComparison.Ordinal);
        Assert.Contains("name=\"NewPassword\"", changePassword, StringComparison.Ordinal);
        Assert.Contains("name=\"ConfirmPassword\"", changePassword, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", changeEmail, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-access-token", changeEmail, StringComparison.Ordinal);
        Assert.DoesNotContain("service-token", changePassword, StringComparison.Ordinal);
        Assert.Contains("name=\"FirstName\"", profile, StringComparison.Ordinal);
        Assert.Contains("customer@example.com", profile, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Email\"", profile, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, createPassword.StatusCode);
        Assert.Equal(
            "/Member/Account/Manage/ChangePassword",
            createPassword.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ChangePassword_PostsWithAntiforgeryThenClearsTheBffSession()
    {
        await SignInAsync();
        var form = await GetAntiforgeryFormAsync("/member/account/manage/changepassword");
        form["CurrentPassword"] = "current-password";
        form["NewPassword"] = "new-password";
        form["ConfirmPassword"] = "new-password";

        using var response = await client.PostAsync(
            "/member/account/manage/changepassword?handler=ChangePassword",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        using var account = await client.GetAsync("/member/account/manage/changepassword");
        Assert.Equal(HttpStatusCode.Redirect, account.StatusCode);
    }

    [Fact]
    public async Task ProfileUpdate_UsesAntiforgeryAndRedirectAfterPost()
    {
        await SignInAsync();
        var form = await GetAntiforgeryFormAsync("/member/account/manage/profile");
        form["FirstName"] = "Ada";
        form["LastName"] = "Lovelace";
        form["CompanyName"] = "Analytical Engines";

        using var response = await client.PostAsync(
            "/member/account/manage/profile?handler=UpdateProfile",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/member/account/manage/profile",
            response.Headers.Location?.OriginalString?.ToLowerInvariant());
    }

    [Fact]
    public async Task ChangeEmail_SynchronizesProfileThenClearsTheBffSession()
    {
        await SignInAsync();
        var form = await GetAntiforgeryFormAsync("/member/account/manage/changeemail");
        form["CurrentPassword"] = "current-password";
        form["NewEmail"] = "new@example.com";

        using var response = await client.PostAsync(
            "/member/account/manage/changeemail?handler=ChangeEmail",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Contains("email=new@example.com", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        using var account = await client.GetAsync("/member/account/manage/changeemail");
        Assert.Equal(HttpStatusCode.Redirect, account.StatusCode);
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
    public async Task ChangeEmailConfirmation_ConsumesSingleUseChallengeAndReturnsToLogin()
    {
        using var response = await client.GetAsync(
            "/account/changeemailconfirmation?email=new%40example.com&token=confirmation-token");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/Account/Login?email=new@example.com",
            response.Headers.Location?.OriginalString);
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

    private sealed class ThrowingCareerClient : ICareerClient
    {
        public Task<CareerListing> GetListingAsync(
            CareerSort sort,
            string? search,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromException<CareerListing>(new InvalidOperationException("sensitive exception detail"));

        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(
            int offerId,
            CancellationToken cancellationToken) =>
            Task.FromException<ServiceResponse<CareerOffer>>(
                new InvalidOperationException("sensitive exception detail"));
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

    private async Task SignInAsync()
    {
        var form = await GetAntiforgeryFormAsync("/account/login");
        form["Email"] = "customer@example.com";
        form["Password"] = "correct-password";
        form["RememberMe"] = "false";
        using var response = await client.PostAsync(
            "/account/login?handler=Login",
            new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
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
                true,
                42));

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

        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(
            string accessToken,
            string currentPassword,
            string newEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerCredentialOperationResult(true, true, true, "confirmation-token"));

        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(
            string accessToken,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerCredentialOperationResult(true, true, true));
    }

    private sealed class StubCustomerProfileClient : ICustomerProfileClient
    {
        public Task<CustomerProfileResult> CreateAsync(string firstName, string lastName, string email, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerProfileResult(new CustomerProfile(42, firstName, lastName, email), true, true));

        public Task<bool> DeleteAsync(int customerId, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class StubCustomerAccountClient : ICustomerAccountClient
    {
        private static readonly CustomerAddress Billing = new(
            7, null, "Existing Billing", null, "Bangkok", null, "10110", 764, null, null);
        private static readonly CustomerAddress Shipping = new(
            11, null, "Existing Shipping", null, "Bangkok", null, "10200", 764, null, null);
        private static readonly CustomerAccountDetails Customer = new(
            42,
            "Ada",
            "Lovelace",
            "Ada Lovelace",
            null,
            null,
            null,
            "customer@example.com",
            null,
            null,
            Billing.Id,
            Shipping.Id,
            null,
            null,
            Billing,
            null,
            Shipping);

        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(
            int customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAddressProfileResult(
                customerId == Customer.Id ? new CustomerAddressProfile(Customer) : null,
                true,
                customerId == Customer.Id));

        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(
            int customerId,
            CustomerAddressUpdate update,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAddressOperationResult(
                customerId == Customer.Id,
                true,
                customerId == Customer.Id));

        public Task<CustomerAddressOperationResult> UpdateEmailAsync(
            int customerId,
            string email,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAddressOperationResult(
                customerId == Customer.Id,
                true,
                customerId == Customer.Id));

        public Task<CustomerAccountProfileResult> GetProfileAsync(
            int customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAccountProfileResult(
                customerId == Customer.Id ? Customer : null,
                true,
                customerId == Customer.Id));

        public Task<CustomerAddressOperationResult> UpdateProfileAsync(
            int customerId,
            CustomerProfileUpdate update,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAddressOperationResult(
                customerId == Customer.Id,
                true,
                customerId == Customer.Id));
    }

    private sealed class StubCustomerOrderClient : ICustomerOrderClient
    {
        private static readonly CustomerOrder Order = new(
            7, 42, "Part", "CNC part", 3, 2, 0, 2, 100, 0, 200, 5,
            null, null, null, true, false, null,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));

        private static readonly CustomerOrderDetails Details = new(
            Order,
            new CustomerOrderProcess(3, 1, "CNC"),
            [new CustomerOrderStatus(9, 7, 2, "Reviewing", null, null, null)],
            [new CustomerOrderFile(4, 7, "legacy-orders", "orders/part.step", null, null)]);

        public Task<CustomerOrderListResult> ListAsync(
            int customerId,
            string? sort,
            string? search,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerOrderListResult(
                customerId == 42 ? new CustomerOrderPage([Order], pageIndex, 1, 1) : null,
                true,
                customerId == 42));

        public Task<CustomerOrderDetailsResult> GetAsync(
            int customerId,
            int orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerOrderDetailsResult(
                customerId == 42 && orderId == 7 ? Details : null,
                true,
                customerId == 42));

        public Task<CustomerOrderOperationResult> CancelAsync(
            int customerId,
            int orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerOrderOperationResult(
                customerId == 42 && orderId == 7,
                true,
                customerId == 42,
                false));
    }
}
