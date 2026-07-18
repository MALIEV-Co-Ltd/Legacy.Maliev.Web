using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using ChangeEmailPage = Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage.ChangeEmail;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberChangeEmailPageTests
{
    [Fact]
    public async Task Get_MissingOpaqueSessionChallengesWithoutProjectingTheForm()
    {
        var page = CreatePage(
            new StubSessionManager(null, null),
            new StubAuthenticationClient(),
            new StubAccountClient());

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.Equal(
            Legacy.Maliev.Web.Components.Pages.Member.MemberChangeEmailDisplayModel.Empty,
            page.DisplayModel);
    }

    [Fact]
    public async Task Post_MissingOpaqueSessionChallengesBeforeValidationOrServiceCalls()
    {
        var authentication = new StubAuthenticationClient();
        var account = new StubAccountClient();
        var page = CreatePage(new StubSessionManager(null, null), authentication, account);
        page.ModelState.AddModelError(nameof(page.CurrentPassword), "untrusted detail");

        var result = await page.OnPostChangeEmailAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.False(authentication.WasCalled);
        Assert.Empty(account.Emails);
    }

    [Fact]
    public async Task Post_IdentityRejectionRollsBackProfileToClaimedEmail()
    {
        var session = new StubSessionManager("opaque-access-token", 42);
        var authentication = new StubAuthenticationClient
        {
            Result = new CustomerCredentialOperationResult(false, true, true),
        };
        var account = new StubAccountClient();
        var page = CreatePage(session, authentication, account);
        page.CurrentPassword = "current-secret";
        page.NewEmail = " new@example.com ";

        var result = await page.OnPostChangeEmailAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(["new@example.com", "server-current@example.com"], account.Emails);
        Assert.False(session.SignOutCalled);
    }

    [Fact]
    public async Task Post_UnauthorizedIdentityStillSignsOutWhenRollbackThrows()
    {
        var session = new StubSessionManager("opaque-access-token", 42);
        var authentication = new StubAuthenticationClient
        {
            Result = new CustomerCredentialOperationResult(false, true, false),
        };
        var account = new StubAccountClient { ThrowOnEmailUpdateNumber = 2 };
        var logger = new RecordingLogger();
        var page = CreatePage(session, authentication, account, logger: logger);
        page.CurrentPassword = "current-secret";
        page.NewEmail = "new@example.com";

        var result = await page.OnPostChangeEmailAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.True(session.SignOutCalled);
        Assert.Contains(logger.Messages, message => message.Contains("manual email reconciliation", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("current-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_NotificationFailureCannotBypassSignOutOrLogCredentials()
    {
        var session = new StubSessionManager("opaque-access-token", 42);
        var authentication = new StubAuthenticationClient
        {
            Result = new CustomerCredentialOperationResult(true, true, true, "single-use-confirmation-token"),
        };
        var account = new StubAccountClient();
        var notification = new StubNotificationClient
        {
            Exception = new HttpRequestException("mail transport failed"),
        };
        var logger = new RecordingLogger();
        var page = CreatePage(session, authentication, account, notification, logger);
        page.CurrentPassword = "current-secret";
        page.NewEmail = "new@example.com";

        var result = await page.OnPostChangeEmailAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(session.SignOutCalled);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("current-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("single-use-confirmation-token", StringComparison.Ordinal));
    }

    private static ChangeEmailPage CreatePage(
        IAccountSessionManager session,
        ICustomerAuthenticationClient authentication,
        ICustomerAccountClient account,
        INotificationClient? notification = null,
        ILogger<ChangeEmailPage>? logger = null)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Email, "old@example.com")],
                    "test")),
        };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");

        return new ChangeEmailPage(
            session,
            authentication,
            account,
            notification ?? new StubNotificationClient(),
            logger ?? new RecordingLogger())
        {
            PageContext = new PageContext { HttpContext = context },
            Url = new StubUrlHelper(context),
        };
    }

    private sealed class StubSessionManager(string? accessToken, int? customerId) : IAccountSessionManager
    {
        public bool SignOutCalled { get; private set; }

        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult(customerId);
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult(accessToken);
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Result is null
                ? throw new InvalidOperationException("Identity service must not be called.")
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
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubAccountClient : ICustomerAccountClient
    {
        public List<string> Emails { get; } = [];

        public int? ThrowOnEmailUpdateNumber { get; init; }

        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken)
        {
            Emails.Add(email);
            if (Emails.Count == ThrowOnEmailUpdateNumber)
            {
                throw new HttpRequestException("customer rollback failed");
            }

            return Task.FromResult(new CustomerAddressOperationResult(true, true, true));
        }

        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAccountProfileResult(
                new CustomerAccountDetails(
                    customerId,
                    "Test",
                    "Customer",
                    "Test Customer",
                    null,
                    null,
                    null,
                    "server-current@example.com",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                true,
                true));
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubNotificationClient : INotificationClient
    {
        public Exception? Exception { get; init; }

        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken) =>
            Exception is null
                ? Task.FromResult(new NotificationResult(true, true, true))
                : Task.FromException<NotificationResult>(Exception);
    }

    private sealed class RecordingLogger : ILogger<ChangeEmailPage>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class StubUrlHelper(HttpContext context) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new(
            context,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => false;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext routeContext) =>
            "https://localhost/account/changeemailconfirmation?email=new%40example.com&token=single-use-confirmation-token";
    }
}
