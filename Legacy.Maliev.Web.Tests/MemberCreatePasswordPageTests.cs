using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using CreatePasswordPage = Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage.CreatePassword;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberCreatePasswordPageTests
{
    [Fact]
    public async Task Post_PasswordlessCustomerCreatesPasswordThroughAuthenticatedServiceBoundary()
    {
        var session = new StubSessionManager("opaque-access-token");
        var authentication = new StubAuthenticationClient(new(true, true, true, false));
        var page = new CreatePasswordPage(session, authentication, NullLogger<CreatePasswordPage>.Instance)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Password = "customer-owned-password",
            ConfirmPassword = "customer-owned-password",
        };

        var result = await page.OnPostCreatePasswordAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("opaque-access-token", authentication.AccessToken);
        Assert.Equal("customer-owned-password", authentication.NewPassword);
        Assert.True(session.SignOutCalled);
    }

    [Fact]
    public async Task Post_ExistingPasswordRendersSafeConflictWithoutCallingChangePassword()
    {
        var authentication = new StubAuthenticationClient(new(false, true, true, true));
        var page = new CreatePasswordPage(
            new StubSessionManager("opaque-access-token"),
            authentication,
            NullLogger<CreatePasswordPage>.Instance)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Password = "customer-owned-password",
            ConfirmPassword = "customer-owned-password",
        };

        var result = await page.OnPostCreatePasswordAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Contains(page.ModelState[string.Empty]!.Errors, error => error.ErrorMessage == "This account already has a password. Use change password instead.");
        Assert.False(authentication.ChangePasswordCalled);
    }

    private sealed class StubSessionManager(string? accessToken) : IAccountSessionManager
    {
        public bool SignOutCalled { get; private set; }
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult<int?>(null);
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult(accessToken);
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) { SignOutCalled = true; return Task.CompletedTask; }
    }

    private sealed class StubAuthenticationClient(CustomerPasswordCreationResult result) : ICustomerAuthenticationClient
    {
        public string? AccessToken { get; private set; }
        public string? NewPassword { get; private set; }
        public bool ChangePasswordCalled { get; private set; }
        public Task<CustomerPasswordCreationResult> CreatePasswordAsync(string accessToken, string newPassword, CancellationToken cancellationToken)
        {
            AccessToken = accessToken;
            NewPassword = newPassword;
            return Task.FromResult(result);
        }
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) { ChangePasswordCalled = true; throw new NotSupportedException(); }
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
}
