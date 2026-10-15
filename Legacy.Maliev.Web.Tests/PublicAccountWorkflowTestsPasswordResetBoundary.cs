using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Pages.Account;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Controlled public Web reset form; no Auth producer, credential write or original-source qualification.</summary>
public sealed class PublicAccountWorkflowTestsPasswordResetBoundary(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Email = PublicAccountWorkflowTestsPasswordResetClient.Email;
    private const string Token = PublicAccountWorkflowTestsPasswordResetClient.Token;
    private const string Password = PublicAccountWorkflowTestsPasswordResetClient.Password;
    private const string PrivateDetail = PublicAccountWorkflowTestsPasswordResetClient.PrivateDetail;
    private const string Retry = "We couldn’t reset your password right now. Please try again later.";
    private const string Rejected = "We couldn’t reset your password. Please check your details or request a new reset link.";

    public static IEnumerable<object[]> FailureCases =>
        from culture in new[] { "en", "th" }
        from outcome in Enum.GetValues<PasswordResetCompletionOutcome>()
        where outcome != PasswordResetCompletionOutcome.Completed
        select new object[] { culture, (int)outcome };

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task ResetFailure_ActualFormKeepsChallengeAndUsesHonestLocalizedCategory(string culture, int classification)
    {
        var outcome = (PasswordResetCompletionOutcome)classification;
        var boundary = new OutcomeBoundary(outcome);
        await using var application = Configure(boundary);
        using var client = Client(application);
        var response = await PostActualForm(client, culture);

        AssertFailure(response, culture, outcome == PasswordResetCompletionOutcome.UnclassifiedFailure);
        Assert.Equal(1, boundary.TypedCalls);
        Assert.Equal(0, boundary.LegacyCalls);
        Assert.Equal((Email, Token, Password), boundary.LastTypedArguments);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ResetCompleted_RetainsExistingLoginRedirect(string culture)
    {
        var boundary = new OutcomeBoundary(PasswordResetCompletionOutcome.Completed);
        await using var application = Configure(boundary);
        using var client = Client(application);
        var response = await PostActualForm(client, culture);

        AssertSuccess(response, culture);
        Assert.Equal(1, boundary.TypedCalls);
        Assert.Equal(0, boundary.LegacyCalls);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task LegacyOnlyConsumer_UsesDefaultBridgeWithoutInventingFailureStatus(string culture, bool completed)
    {
        var legacy = new ResetPasswordLegacyBoundary { Result = completed };
        await using var application = Configure(legacy);
        using var client = Client(application);
        var response = await PostActualForm(client, culture);

        if (completed) AssertSuccess(response, culture);
        else AssertFailure(response, culture, unclassified: true);
        Assert.Equal(1, legacy.LegacyCalls);
        Assert.Equal((Email, Token, Password), legacy.LastArguments);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task UnexpectedOrNonCallerCancellation_IsRetryableWithoutExportingPrivateDetails(string culture, bool cancellation)
    {
        var details = string.Join("|", Email, Token, Password, PrivateDetail);
        var boundary = new OutcomeBoundary(PasswordResetCompletionOutcome.UnclassifiedFailure)
        {
            Failure = cancellation ? new OperationCanceledException(details) : new InvalidOperationException(details),
        };
        await using var application = Configure(boundary);
        using var client = Client(application);
        var response = await PostActualForm(client, culture);

        AssertFailure(response, culture, unclassified: false);
        Assert.Equal(1, boundary.TypedCalls);
        Assert.Equal(0, boundary.LegacyCalls);
    }

    [Theory]
    [InlineData("en", "token")]
    [InlineData("th", "token")]
    [InlineData("en", "password")]
    [InlineData("th", "password")]
    [InlineData("en", "email")]
    [InlineData("th", "email")]
    public async Task InvalidFormNeverInvokesEitherCompletionMethod(string culture, string invalid)
    {
        var boundary = new OutcomeBoundary(PasswordResetCompletionOutcome.Completed);
        await using var application = Configure(boundary);
        using var client = Client(application);
        var response = await PostActualForm(client, culture, invalid: invalid);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Null(response.Location);
        Assert.Equal(0, boundary.TypedCalls);
        Assert.Equal(0, boundary.LegacyCalls);
        Assert.DoesNotContain(Password, response.Body, StringComparison.Ordinal);
        if (invalid == "token" && culture == "en")
            Assert.Contains("The password reset token is invalid.", response.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ForgedAntiforgeryNeverInvokesEitherCompletionMethod(string culture)
    {
        var boundary = new OutcomeBoundary(PasswordResetCompletionOutcome.Completed);
        await using var application = Configure(boundary);
        using var client = Client(application);
        var response = await PostActualForm(client, culture, forgeToken: true);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(0, boundary.TypedCalls);
        Assert.Equal(0, boundary.LegacyCalls);
    }

    [Fact]
    public async Task CallerCancellationIsNotRenderedAsAnExpiredLinkOrRetry()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var boundary = new OutcomeBoundary(PasswordResetCompletionOutcome.UnclassifiedFailure)
        {
            Failure = new OperationCanceledException(cancellation.Token),
        };
        var page = new ResetPassword(boundary, new EchoLocalizer())
        {
            Email = Email,
            Token = Token,
            Password = Password,
            ConfirmPassword = Password,
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => page.OnPostChangePasswordAsync(cancellation.Token));
        Assert.Equal(1, boundary.TypedCalls);
        Assert.Equal(0, boundary.LegacyCalls);
        Assert.Empty(page.ModelState);
    }

    private WebApplicationFactory<Program> Configure(ICustomerAuthenticationClient boundary) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICustomerAuthenticationClient>();
            services.AddSingleton(boundary);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://www.maliev.com"),
            AllowAutoRedirect = false,
        });

    private static async Task<ResponseObservation> PostActualForm(
        HttpClient client, string culture, string? invalid = null, bool forgeToken = false)
    {
        var url = QueryHelpers.AddQueryString("/account/resetpassword", new Dictionary<string, string?>
        {
            ["email"] = Email, ["token"] = Token, ["culture"] = culture,
        });
        using var page = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var form = Assert.Single(Regex.Matches(html, "<form[^>]*>[\\s\\S]*?</form>",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).Cast<Match>(),
            candidate => candidate.Value.Contains("handler=ChangePassword", StringComparison.Ordinal));
        var action = Match(form.Value, "formaction=\"(?<value>[^\"]+)\"").Groups["value"].Value;
        var anti = Match(form.Value, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"").Groups["value"].Value;
        using var response = await client.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = invalid == "email" ? "invalid-address" : Email,
            ["Token"] = invalid == "token" ? "short" : Token,
            ["Password"] = Password,
            ["ConfirmPassword"] = invalid == "password" ? "different-confirmation" : Password,
            ["__RequestVerificationToken"] = forgeToken ? "synthetic-forged-antiforgery" : anti,
        }));
        return new(response.StatusCode, response.Headers.Location,
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()),
            response.Headers.CacheControl?.NoStore == true,
            response.Headers.TryGetValues("Referrer-Policy", out var policy) ? policy.Single() : null);
    }

    private static void AssertFailure(ResponseObservation response, string culture, bool unclassified)
    {
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Null(response.Location);
        Assert.True(response.NoStore);
        Assert.Equal("no-referrer", response.ReferrerPolicy);
        var expected = culture == "en"
            ? (unclassified ? Rejected : Retry)
            : (unclassified
                ? "ไม่สามารถตั้งรหัสผ่านใหม่ได้ กรุณาตรวจสอบข้อมูลหรือขอลิงก์ตั้งรหัสผ่านใหม่อีกครั้ง"
                : "ไม่สามารถตั้งรหัสผ่านใหม่ได้ในขณะนี้ กรุณาลองอีกครั้งภายหลัง");
        Assert.Contains(expected, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("The password reset link is invalid or expired.", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateDetail, response.Body, StringComparison.Ordinal);
        // Email/token are intentionally retained in existing hidden challenge fields, never new error data.
        Assert.Equal(Email, Match(response.Body, "name=\"Email\"[^>]*value=\"(?<value>[^\"]*)\"").Groups["value"].Value);
        Assert.Equal(Token, Match(response.Body, "name=\"Token\"[^>]*value=\"(?<value>[^\"]*)\"").Groups["value"].Value);
        foreach (var passwordInput in Regex.Matches(response.Body, "<input[^>]*type=\"password\"[^>]*>",
                     RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).Cast<Match>())
            Assert.DoesNotContain("value=", passwordInput.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSuccess(ResponseObservation response, string culture)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.Status);
        var location = new Uri(new Uri("https://www.maliev.com"), Assert.IsType<Uri>(response.Location));
        Assert.Equal("/Account/Login", location.AbsolutePath);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal(Email, query["email"].ToString());
        Assert.Equal(culture, query["culture"].ToString());
        Assert.DoesNotContain(Token, location.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, location.ToString(), StringComparison.Ordinal);
    }

    private static Match Match(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        Assert.True(match.Success);
        return match;
    }

    private sealed record ResponseObservation(HttpStatusCode Status, Uri? Location, string Body, bool NoStore, string? ReferrerPolicy);
    private sealed class OutcomeBoundary(PasswordResetCompletionOutcome outcome)
        : ResetPasswordLegacyBoundary, IPasswordResetCompletionClient
    {
        internal new Exception? Failure { get; set; }
        internal int TypedCalls { get; private set; }
        internal (string Email, string Token, string Password) LastTypedArguments { get; private set; }
        Task<PasswordResetCompletionOutcome> IPasswordResetCompletionClient.CompletePasswordResetOutcomeAsync(
            string email, string token, string password, CancellationToken cancellationToken)
        {
            TypedCalls++;
            LastTypedArguments = (email, token, password);
            return Failure is null ? Task.FromResult(outcome) : Task.FromException<PasswordResetCompletionOutcome>(Failure);
        }
    }
    private sealed class EchoLocalizer : IStringLocalizer<ResetPasswordContent>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
