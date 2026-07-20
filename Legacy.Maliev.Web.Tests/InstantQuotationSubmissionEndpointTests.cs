using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.InstantQuotation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationSubmissionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public InstantQuotationSubmissionEndpointTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Fact]
    public async Task SubmitRequest_RequiresAValidAntiforgeryToken()
    {
        var service = new RecordingSubmissionService(Completed(717));
        await using var application = CreateFactory(service);
        using var client = CreateClient(application);

        using var missing = await client.PostAsync(SubmitRoute, CustomerForm());
        using var bad = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new() { ["__RequestVerificationToken"] = "not-a-token" }));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Empty(service.Calls);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "member-42")]
    public async Task ValidPost_BindsOnlyCustomerFields_AndUsesProtectedClaims(
        bool authenticated,
        string? expectedOwner)
    {
        var service = new RecordingSubmissionService(Completed(718));
        var tempData = new RecordingTempDataProvider();
        await using var application = CreateFactory(service, tempData, authenticated);
        using var client = CreateClient(application);
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        }
        var token = await GetAntiforgeryTokenAsync(client);
        tempData.Clear();
        using var response = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new()
            {
                ["__RequestVerificationToken"] = token,
                ["Parts"] = "[{\"unitPrice\":0.01}]",
                ["FinalOrderPrice"] = "0.01",
                ["UploadReference"] = "browser-secret",
                ["SessionId"] = "attacker-session",
                ["SubmissionId"] = "attacker-submission",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/InstantQuotation/3D-Printing", response.Headers.Location?.OriginalString);
        var call = Assert.Single(service.Calls);
        Assert.Matches("^[0-9A-Fa-f]{64}$", call.SessionId);
        Assert.Equal(expectedOwner, call.OwnerIdentity);
        Assert.Equal(
            new InstantQuotationCustomerSubmission(
                "Ada",
                "Lovelace",
                "ada@example.com",
                "+66 80 000 0000",
                "TH",
                "Analytical Engines",
                "TAX-42",
                "Please quote this part."),
            call.Customer);
        AssertSafeCompletedTempData(tempData.Values, 718);
        Assert.DoesNotContain("Ada", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(response.Headers, header =>
            string.Join(',', header.Value).Contains("attacker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuplicatePosts_DelegateIdempotencyToTheSubmissionService()
    {
        var service = new RecordingSubmissionService(Completed(719));
        await using var application = CreateFactory(service);
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(client);

        using var first = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new() { ["__RequestVerificationToken"] = token }));
        token = await GetAntiforgeryTokenAsync(client);
        using var second = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new() { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal(2, service.Calls.Count);
        Assert.Equal(service.Calls[0].SessionId, service.Calls[1].SessionId);
    }

    [Theory]
    [InlineData(InstantQuotationSubmissionOutcome.Partial)]
    [InlineData(InstantQuotationSubmissionOutcome.Persisted)]
    public async Task PersistedOrPartialPost_StoresSafeDoNotResubmitStateAndPersistedAnalytics(
        InstantQuotationSubmissionOutcome outcome)
    {
        var service = new RecordingSubmissionService(new(
            outcome,
            720,
            InstantQuotationProblemCategory.DependencyUnavailable));
        var tempData = new RecordingTempDataProvider();
        await using var application = CreateFactory(service, tempData);
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(client);
        tempData.Clear();

        using var response = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new() { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            ThreeDimensionalPrinting.SubmissionStatusPartial,
            tempData.Values[ThreeDimensionalPrinting.SubmissionStatusTempDataKey]);
        Assert.Equal(720, tempData.Values[ThreeDimensionalPrinting.RequestReferenceTempDataKey]);
        AssertRequestQuoteAnalytics(tempData.Values, 720, fileUploadCompleted: false);
        Assert.Equal(3, tempData.Values.Count);
    }

    [Fact]
    public async Task CompletedPost_RendersRequestAndFileCompletionOnceAfterRedirect()
    {
        var service = new RecordingSubmissionService(Completed(724));
        await using var application = CreateFactory(service);
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(client);

        using var post = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new() { ["__RequestVerificationToken"] = token }));
        var firstGet = WebUtility.HtmlDecode(await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en"));
        var secondGet = WebUtility.HtmlDecode(await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en"));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("\"event\":\"request_quote\"", firstGet, StringComparison.Ordinal);
        Assert.Contains("\"event\":\"file_upload_complete\"", firstGet, StringComparison.Ordinal);
        Assert.DoesNotContain("\"event\":\"request_quote\"", secondGet, StringComparison.Ordinal);
        Assert.DoesNotContain("\"event\":\"file_upload_complete\"", secondGet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedPost_StoresOnlyTheControlledProblemCategory()
    {
        var service = new RecordingSubmissionService(new(
            InstantQuotationSubmissionOutcome.Rejected,
            null,
            InstantQuotationProblemCategory.Authorization));
        var tempData = new RecordingTempDataProvider();
        await using var application = CreateFactory(service, tempData);
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(client);
        tempData.Clear();

        using var response = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new() { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            ThreeDimensionalPrinting.SubmissionStatusRejected,
            tempData.Values[ThreeDimensionalPrinting.SubmissionStatusTempDataKey]);
        Assert.Equal(
            "authorization",
            tempData.Values[ThreeDimensionalPrinting.ProblemCategoryTempDataKey]);
        Assert.Equal(2, tempData.Values.Count);
    }

    [Fact]
    public async Task OverlongCustomerField_RedirectsWithControlledValidationWithoutCallingService()
    {
        var service = new RecordingSubmissionService(Completed(723));
        var tempData = new RecordingTempDataProvider();
        await using var application = CreateFactory(service, tempData);
        using var client = CreateClient(application);
        var token = await GetAntiforgeryTokenAsync(client);
        tempData.Clear();

        using var response = await client.PostAsync(
            SubmitRoute,
            CustomerForm(new()
            {
                ["FirstName"] = new string('a', 51),
                ["Description"] = new string('d', 513),
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(service.Calls);
        Assert.Equal(ThreeDimensionalPrinting.SubmissionStatusRejected, tempData.Values[ThreeDimensionalPrinting.SubmissionStatusTempDataKey]);
        Assert.Equal("validation", tempData.Values[ThreeDimensionalPrinting.ProblemCategoryTempDataKey]);
        var fields = JsonSerializer.Deserialize<string[]>(
            Assert.IsType<string>(tempData.Values[ThreeDimensionalPrinting.ValidationFieldsTempDataKey]));
        Assert.NotNull(fields);
        Assert.Equal(["Description", "FirstName"], fields);
        Assert.Equal(3, tempData.Values.Count);

        using var correctionResponse = await client.GetAsync("/InstantQuotation/3D-Printing?culture=en");
        var correctionSource = WebUtility.HtmlDecode(await correctionResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);
        Assert.Contains("data-workflow-customer-details", correctionSource, StringComparison.Ordinal);
        Assert.Contains("There are problems with the customer details.", correctionSource, StringComparison.Ordinal);
        Assert.Matches("id=\"instant-quote-firstname\"[^>]*aria-invalid=\"true\"", correctionSource);
        Assert.Matches("id=\"instant-quote-description\"[^>]*aria-invalid=\"true\"", correctionSource);
    }

    [Fact]
    public async Task MissingProtectedSessionClaim_FailsClosedWithoutCallingTheService()
    {
        var service = new RecordingSubmissionService(Completed(721));
        var page = CreatePage(service, []);

        var result = await page.OnPostSubmitRequestAsync(default);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/InstantQuotation/3D-Printing", redirect.Url);
        Assert.Empty(service.Calls);
        Assert.Equal(
            ThreeDimensionalPrinting.SubmissionStatusRejected,
            page.TempData[ThreeDimensionalPrinting.SubmissionStatusTempDataKey]);
        Assert.Equal(
            "authorization",
            page.TempData[ThreeDimensionalPrinting.ProblemCategoryTempDataKey]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AuthenticatedPrincipalWithoutOwnerIdentifier_FailsClosed(string? ownerIdentity)
    {
        var service = new RecordingSubmissionService(Completed(722));
        var claims = new List<Claim>
        {
            new(InstantQuotationSessionIdentityClaim.Type, new string('a', 64)),
        };
        if (ownerIdentity is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, ownerIdentity));
        }

        var page = CreatePage(service, claims, "test");

        var result = await page.OnPostSubmitRequestAsync(default);

        Assert.IsType<LocalRedirectResult>(result);
        Assert.Empty(service.Calls);
        Assert.Equal("authorization", page.TempData[ThreeDimensionalPrinting.ProblemCategoryTempDataKey]);
    }

    [Fact]
    public void RazorPageRoute_IsPostOnlyWhileBlazorOwnsGetAndHead()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Program.cs"));
        var start = source.IndexOf("\"/InstantQuotation/3D-Printing\"", StringComparison.Ordinal);
        var routeConvention = source[start..source.IndexOf("});", start, StringComparison.Ordinal)];

        Assert.Contains("HttpMethodMetadata([\"POST\"])", routeConvention, StringComparison.Ordinal);
        Assert.DoesNotContain("Selectors.Clear", routeConvention, StringComparison.Ordinal);
    }

    private const string SubmitRoute = "/InstantQuotation/3D-Printing?handler=SubmitRequest";

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingSubmissionService service,
        RecordingTempDataProvider? tempData = null,
        bool authenticated = false) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IInstantQuotationSubmissionService>();
            services.AddSingleton<IInstantQuotationSubmissionService>(service);
            services.RemoveAll<ICountryClient>();
            services.AddSingleton<ICountryClient, ImmediateCountryClient>();
            services.RemoveAll<IAccountSessionManager>();
            services.AddSingleton<IAccountSessionManager, EmptyAccountSessionManager>();
            if (tempData is not null)
            {
                services.RemoveAll<ITempDataProvider>();
                services.AddSingleton<ITempDataProvider>(tempData);
            }

            if (authenticated)
            {
                services.AddAuthentication(options => options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        static _ => { });
            }
        });
    });

    private static HttpClient CreateClient(WebApplicationFactory<Program> application) => application.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en"));
        var match = AntiforgeryTokenRegex().Match(source);
        Assert.True(match.Success);
        return match.Groups["value"].Value;
    }

    private static FormUrlEncodedContent CustomerForm(Dictionary<string, string>? additions = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["FirstName"] = " Ada ",
            ["LastName"] = " Lovelace ",
            ["Email"] = " ada@example.com ",
            ["Telephone"] = " +66 80 000 0000 ",
            ["Company"] = " Analytical Engines ",
            ["TaxNumber"] = " TAX-42 ",
            ["Country"] = " TH ",
            ["Description"] = " Please quote this part. ",
        };
        foreach (var (key, value) in additions ?? [])
        {
            fields[key] = value;
        }

        return new FormUrlEncodedContent(fields);
    }

    private static ThreeDimensionalPrinting CreatePage(
        IInstantQuotationSubmissionService service,
        IReadOnlyList<Claim> claims,
        string? authenticationType = null)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType)),
        };
        var tempData = new RecordingTempDataProvider();
        return new ThreeDimensionalPrinting(service, NullLogger<ThreeDimensionalPrinting>.Instance)
        {
            PageContext = new PageContext { HttpContext = context },
            TempData = new TempDataDictionary(context, tempData),
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            Telephone = "+66 80 000 0000",
            Country = "TH",
        };
    }

    private static InstantQuotationSubmissionResult Completed(int reference) => new(
        InstantQuotationSubmissionOutcome.Completed,
        reference,
        InstantQuotationProblemCategory.None);

    private static void AssertSafeCompletedTempData(IReadOnlyDictionary<string, object> values, int reference)
    {
        Assert.Equal(ThreeDimensionalPrinting.SubmissionStatusCompleted, values[ThreeDimensionalPrinting.SubmissionStatusTempDataKey]);
        Assert.Equal(reference, values[ThreeDimensionalPrinting.RequestReferenceTempDataKey]);
        AssertRequestQuoteAnalytics(values, reference, fileUploadCompleted: true);
        Assert.Equal(3, values.Count);
    }

    private static void AssertRequestQuoteAnalytics(
        IReadOnlyDictionary<string, object> values,
        int reference,
        bool fileUploadCompleted)
    {
        var serialized = Assert.Single(
            values.Values.OfType<string>(),
            value => value.Contains("\"event\":\"request_quote\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;
        Assert.Equal(
            ["event", "file_upload_completed", "has_files", "intent_type", "service", "submission_status", "transaction_id"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("request_quote", root.GetProperty("event").GetString());
        Assert.Equal("quotation_request", root.GetProperty("intent_type").GetString());
        Assert.Equal("3d_printing", root.GetProperty("service").GetString());
        Assert.Equal($"quotation-{reference}", root.GetProperty("transaction_id").GetString());
        Assert.Equal("persisted", root.GetProperty("submission_status").GetString());
        Assert.True(root.GetProperty("has_files").GetBoolean());
        Assert.Equal(fileUploadCompleted, root.GetProperty("file_upload_completed").GetBoolean());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenRegex();

    private sealed class RecordingSubmissionService(InstantQuotationSubmissionResult result)
        : IInstantQuotationSubmissionService
    {
        public List<SubmissionCall> Calls { get; } = [];

        public Task<InstantQuotationSubmissionResult> SubmitAsync(
            string sessionId,
            string? ownerIdentity,
            InstantQuotationCustomerSubmission customer,
            CancellationToken cancellationToken)
        {
            Calls.Add(new(sessionId, ownerIdentity, customer));
            return Task.FromResult(result);
        }
    }

    private sealed class ImmediateCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(1, "Thailand", "Asia", "TH", "TH", "THA", null, null)],
                true));
    }

    private sealed class EmptyAccountSessionManager : IAccountSessionManager
    {
        public Task<AccountSignInStatus> SignInAsync(
            HttpContext context,
            string email,
            string password,
            bool rememberMe,
            CancellationToken cancellationToken) => Task.FromResult(AccountSignInStatus.InvalidCredentials);

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);
    }

    private sealed record SubmissionCall(
        string SessionId,
        string? OwnerIdentity,
        InstantQuotationCustomerSubmission Customer);

    private sealed class RecordingTempDataProvider : ITempDataProvider
    {
        public Dictionary<string, object> Values { get; private set; } = [];

        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(Values);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
            Values = new Dictionary<string, object>(values);

        public void Clear() => Values.Clear();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "InstantQuotationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization, SchemeName, StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "member-42")],
                SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
