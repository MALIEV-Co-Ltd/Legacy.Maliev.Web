using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Retained password validation through the real authorized route without credential writes.</summary>
public sealed class SharedSelectorValidationOriginalParityTestsPassword(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", "different-confirmation", "Passwords do not match.")]
    [InlineData("th", "different-confirmation", "รหัสผ่านใหม่ทั้งสองช่องไม่ตรงกัน")]
    [InlineData("en", "", "Please confirm the new password.")]
    [InlineData("th", "", "กรุณายืนยันรหัสผ่านใหม่")]
    public async Task PasswordConfirmation_ActualAuthorizedPostLocalizesWithoutCredentialServiceCall(
        string culture, string confirmation, string expected)
    {
        var boundary = new PasswordReadBoundary();
        var session = new PasswordReadSession();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccountSessionManager>();
            services.RemoveAll<ICustomerAuthenticationClient>();
            services.AddSingleton<IAccountSessionManager>(session);
            services.AddSingleton<ICustomerAuthenticationClient>(boundary);
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = PasswordQualificationAuthentication.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, PasswordQualificationAuthentication>(PasswordQualificationAuthentication.SchemeName, _ => { });
        }));
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("Authorization", PasswordQualificationAuthentication.SchemeName);
        const string password = "synthetic-validation-password";
        using var page = await client.GetAsync($"/member/account/manage/createpassword?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var form = Form(html, "<form(?=[^>]*action=\"(?<action>[^\"]*handler=CreatePassword[^\"]*)\")[^>]*[\\s\\S]*?</form>");
        using var response = await client.PostAsync(QueryHelpers.AddQueryString(form.Groups["action"].Value, "culture", culture),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Token(form.Value),
                ["Password"] = password,
                ["ConfirmPassword"] = confirmation,
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var alert = Regex.Match(body, "<div[^>]*role=\"alert\"[^>]*>[\\s\\S]*?</div>", RegexOptions.CultureInvariant);
        Assert.True(alert.Success);
        Assert.Contains(expected, alert.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(password, body, StringComparison.Ordinal);
        Assert.DoesNotContain("qualification-access-token", body, StringComparison.Ordinal);
        Assert.Equal(0, boundary.CreationCalls);
        Assert.Equal(0, session.SignOutCalls);
    }

    private sealed class PasswordQualificationAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "PasswordLocalizationQualification";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(Request.Headers.Authorization, SchemeName, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "customer:42"),
                new Claim("identity_kind", "customer"),
                new Claim("legacy_database_id", "42")
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class PasswordReadSession : IAccountSessionManager
    {
        public int SignOutCalls { get; private set; }
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken token) => Task.FromResult<int?>(42);
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken token) => Task.FromResult<string?>("qualification-access-token");
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken token) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken token) { SignOutCalls++; throw new NotSupportedException(); }
    }

    private sealed class PasswordReadBoundary : ICustomerAuthenticationClient
    {
        public int CreationCalls { get; private set; }
        public bool ChangePasswordCalled { get; private set; }
        public Task<CustomerPasswordCreationResult> CreatePasswordAsync(string accessToken, string newPassword, CancellationToken cancellationToken)
        {
            CreationCalls++;
            throw new NotSupportedException("Credential creation is forbidden during validation qualification.");
        }
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) { ChangePasswordCalled = true; throw new NotSupportedException(); }
        public Task<CustomerSelfIdentityResult> GetSelfIdentityAsync(string accessToken, int expectedCustomerId, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerSelfIdentityResult(CustomerSelfIdentityStatus.Unavailable));

        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeValidationResult> ValidateEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeCompletionResult> CompleteEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static Match Form(string html, string pattern)
    {
        var form = Regex.Match(html, pattern, RegexOptions.CultureInvariant);
        Assert.True(form.Success);
        return form;
    }

    private static string Token(string form)
    {
        var token = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(token.Success);
        return token.Groups["value"].Value;
    }

}
