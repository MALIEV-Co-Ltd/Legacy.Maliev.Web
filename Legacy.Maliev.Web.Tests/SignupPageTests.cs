using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Account;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class SignupPageTests
{
    [Fact]
    public async Task IdentityRegistrationFailure_DeletesPreviouslyCreatedCustomerProfile()
    {
        var customers = new CompensatingCustomerClient();
        var model = new Signup(
            customers,
            new RejectingAuthenticationClient(),
            new NoopNotificationClient(),
            new PassingAntiBotVerifier(),
            Options.Create(new RecaptchaEnterpriseOptions { SiteKey = "test" }),
            NullLogger<Signup>.Instance)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            FirstName = "Customer",
            LastName = "Example",
            Email = "customer@example.com",
            Password = "correct-password",
            ConfirmPassword = "correct-password",
            RecaptchaToken = "valid-token",
        };

        var result = await model.OnPostSignUpAsync(default);

        Assert.IsType<PageResult>(result);
        Assert.Equal(42, customers.DeletedCustomerId);
        Assert.False(model.ModelState.IsValid);
    }

    private sealed class CompensatingCustomerClient : ICustomerProfileClient
    {
        public int? DeletedCustomerId { get; private set; }

        public Task<CustomerProfileResult> CreateAsync(string firstName, string lastName, string email, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerProfileResult(new CustomerProfile(42, firstName, lastName, email), true, true));

        public Task<bool> DeleteAsync(int customerId, CancellationToken cancellationToken)
        {
            DeletedCustomerId = customerId;
            return Task.FromResult(true);
        }
    }

    private sealed class RejectingAuthenticationClient : ICustomerAuthenticationClient
    {
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerIdentityRegistration(false, null, null, null));

        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PassingAntiBotVerifier : IAntiBotVerifier
    {
        public Task<bool> VerifyAsync(string? token, string expectedAction, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class NoopNotificationClient : INotificationClient
    {
        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationResult(true, true, true));
    }
}
