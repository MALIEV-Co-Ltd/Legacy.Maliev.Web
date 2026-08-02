using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerEmailChangeWorkflowTests
{
    [Fact]
    public async Task Complete_UpdatesProfileBeforeCommittingIdentity()
    {
        var authentication = new StubAuthenticationClient
        {
            Validation = PendingValidation(),
            Completion = new(true, true, true),
        };
        var account = new StubAccountClient("old@example.com");
        var workflow = new CustomerEmailChangeWorkflow(
            authentication,
            account,
            NullLogger<CustomerEmailChangeWorkflow>.Instance);

        var result = await workflow.CompleteAsync("new@example.com", "opaque-token", default);

        Assert.True(result.Succeeded);
        Assert.Equal(["new@example.com"], account.EmailUpdates);
        Assert.Equal(1, authentication.CompletionCalls);
    }

    [Fact]
    public async Task Complete_TransientCompletionProbesOutcomeBeforeReportingFailure()
    {
        var authentication = new StubAuthenticationClient
        {
            Validation = PendingValidation(),
            Completion = new(false, false, true),
            ValidationAfterCompletion = PendingValidation(completed: true),
        };
        var account = new StubAccountClient("old@example.com");
        var workflow = new CustomerEmailChangeWorkflow(
            authentication,
            account,
            NullLogger<CustomerEmailChangeWorkflow>.Instance);

        var result = await workflow.CompleteAsync("new@example.com", "opaque-token", default);

        Assert.True(result.Succeeded);
        Assert.Equal(["new@example.com"], account.EmailUpdates);
        Assert.Equal(2, authentication.ValidationCalls);
    }

    [Fact]
    public async Task Complete_DefinitiveIdentityRejectionCompensatesProfile()
    {
        var authentication = new StubAuthenticationClient
        {
            Validation = PendingValidation(),
            Completion = new(false, true, true),
        };
        var account = new StubAccountClient("old@example.com");
        var workflow = new CustomerEmailChangeWorkflow(
            authentication,
            account,
            NullLogger<CustomerEmailChangeWorkflow>.Instance);

        var result = await workflow.CompleteAsync("new@example.com", "opaque-token", default);

        Assert.False(result.Succeeded);
        Assert.Equal(["new@example.com", "old@example.com"], account.EmailUpdates);
    }

    [Fact]
    public async Task Complete_RejectsProfileThatDoesNotMatchTheBoundIdentity()
    {
        var authentication = new StubAuthenticationClient { Validation = PendingValidation() };
        var account = new StubAccountClient("unexpected@example.com");
        var workflow = new CustomerEmailChangeWorkflow(
            authentication,
            account,
            NullLogger<CustomerEmailChangeWorkflow>.Instance);

        var result = await workflow.CompleteAsync("new@example.com", "opaque-token", default);

        Assert.False(result.Succeeded);
        Assert.Empty(account.EmailUpdates);
        Assert.Equal(0, authentication.CompletionCalls);
    }

    [Fact]
    public async Task Complete_CompletedIdentityReconcilesStaleCurrentProfile()
    {
        var authentication = new StubAuthenticationClient
        {
            Validation = PendingValidation(completed: true),
        };
        var account = new StubAccountClient("old@example.com");
        var workflow = new CustomerEmailChangeWorkflow(
            authentication,
            account,
            NullLogger<CustomerEmailChangeWorkflow>.Instance);

        var result = await workflow.CompleteAsync("new@example.com", "opaque-token", default);

        Assert.True(result.Succeeded);
        Assert.Equal(["new@example.com"], account.EmailUpdates);
        Assert.Equal(0, authentication.CompletionCalls);
    }

    private static CustomerEmailChangeValidationResult PendingValidation(bool completed = false) =>
        new(true, true, true, 42, "old@example.com", "new@example.com", completed);

    private sealed class StubAuthenticationClient : ICustomerAuthenticationClient
    {
        public CustomerEmailChangeValidationResult Validation { get; init; } = new(false, true, true);
        public CustomerEmailChangeValidationResult? ValidationAfterCompletion { get; init; }
        public CustomerEmailChangeCompletionResult Completion { get; init; } = new(false, true, true);
        public int ValidationCalls { get; private set; }
        public int CompletionCalls { get; private set; }

        public Task<CustomerEmailChangeValidationResult> ValidateEmailChangeAsync(string email, string token, CancellationToken cancellationToken)
        {
            ValidationCalls++;
            return Task.FromResult(ValidationCalls == 1 || ValidationAfterCompletion is null
                ? Validation
                : ValidationAfterCompletion);
        }

        public Task<CustomerEmailChangeCompletionResult> CompleteEmailChangeAsync(string email, string token, CancellationToken cancellationToken)
        {
            CompletionCalls++;
            return Task.FromResult(Completion);
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
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubAccountClient(string email) : ICustomerAccountClient
    {
        private CustomerAccountDetails profile = new(
            42,
            "Test",
            "Customer",
            "Test Customer",
            null,
            null,
            null,
            email,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        public List<string> EmailUpdates { get; } = [];

        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAccountProfileResult(profile, true, true));

        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken)
        {
            EmailUpdates.Add(email);
            profile = profile with { Email = email };
            return Task.FromResult(new CustomerAddressOperationResult(true, true, true));
        }

        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
