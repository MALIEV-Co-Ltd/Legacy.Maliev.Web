using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ChangePasswordPage = Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage.ChangePassword;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberChangePasswordPageTests
{
    [Fact]
    public async Task Get_MissingAccessTokenChallengesWithoutProjectingTheForm()
    {
        var page = CreatePage(new StubSessionManager(null), new StubAuthenticationClient());

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.Equal(MemberChangePasswordDisplayModel.Empty, page.DisplayModel);
    }

    [Fact]
    public async Task Post_MissingAccessTokenChallengesBeforeValidationOrCredentialService()
    {
        var authentication = new StubAuthenticationClient();
        var page = CreatePage(new StubSessionManager(null), authentication);
        page.ModelState.AddModelError(nameof(page.CurrentPassword), "untrusted validation detail");

        var result = await page.OnPostChangePasswordAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.False(authentication.WasCalled);
    }

    [Fact]
    public async Task Post_NotificationFailureCannotBypassSignOutOrLogPasswordValues()
    {
        var session = new StubSessionManager("opaque-access-token");
        var authentication = new StubAuthenticationClient { Result = new(true, true, true) };
        var notification = new StubNotificationClient { Exception = new HttpRequestException("mail transport failed") };
        var logger = new RecordingLogger();
        var page = CreatePage(session, authentication, notification, logger);
        page.CurrentPassword = "current-secret-value";
        page.NewPassword = "new-secret-value";
        page.ConfirmPassword = "new-secret-value";
        page.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "customer@example.com")],
                "test"));

        var result = await page.OnPostChangePasswordAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(session.SignOutCalled);
        Assert.True(authentication.WasCalled);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("current-secret-value", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("new-secret-value", StringComparison.Ordinal));
    }

    private static ChangePasswordPage CreatePage(
        IAccountSessionManager session,
        ICustomerAuthenticationClient authentication,
        INotificationClient? notification = null,
        ILogger<ChangePasswordPage>? logger = null) => new(
            session,
            authentication,
            notification ?? new StubNotificationClient(),
            logger ?? NullLogger<ChangePasswordPage>.Instance)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

    private sealed class StubSessionManager(string? accessToken) : IAccountSessionManager
    {
        public bool SignOutCalled { get; private set; }

        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult(accessToken);

        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken)
        {
            SignOutCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthenticationClient : ICustomerAuthenticationClient
    {
        public bool WasCalled { get; private set; }

        public CustomerCredentialOperationResult? Result { get; init; }

        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(
            string accessToken,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Result is null
                ? throw new InvalidOperationException("Credential service must not be called without an access token.")
                : Task.FromResult(Result);
        }

        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubNotificationClient : INotificationClient
    {
        public Exception? Exception { get; init; }

        public Task<NotificationResult> SendAsync(
            NotificationChannel channel,
            EmailNotification notification,
            CancellationToken cancellationToken) => Exception is null
                ? Task.FromResult(new NotificationResult(true, true, true))
                : Task.FromException<NotificationResult>(Exception);
    }

    private sealed class RecordingLogger : ILogger<ChangePasswordPage>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
