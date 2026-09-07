using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncAuthenticatedProfileLoaderTests
{
    [Fact]
    public async Task EmptySession_RemainsAnonymousWithoutCallingServices()
    {
        var authentication = new StubAuthentication();
        var customers = new StubCustomers();
        var countries = new StubCountries();
        var loader = new CncAuthenticatedProfileLoader(new StubSessions(null, null), authentication, customers, countries);

        CncAuthenticatedProfileLoadResult result = await loader.LoadAsync(
            new DefaultHttpContext(), new ModelStateDictionary(), CancellationToken.None);

        Assert.Equal(CncAuthenticatedProfileLoadOutcome.Anonymous, result.Outcome);
        Assert.Equal(0, authentication.Calls + customers.Calls + countries.Calls);
    }

    [Theory]
    [InlineData(null, 7)]
    [InlineData("token", null)]
    [InlineData("token", 0)]
    public async Task PartialOrInvalidSession_IsRejectedBeforeServiceCalls(string? token, int? customerId)
    {
        var authentication = new StubAuthentication();
        var customers = new StubCustomers();
        var countries = new StubCountries();
        var errors = new ModelStateDictionary();
        var loader = new CncAuthenticatedProfileLoader(new StubSessions(token, customerId), authentication, customers, countries);

        CncAuthenticatedProfileLoadResult result = await loader.LoadAsync(
            new DefaultHttpContext(), errors, CancellationToken.None);

        Assert.Equal(CncAuthenticatedProfileLoadOutcome.Rejected, result.Outcome);
        Assert.False(errors.IsValid);
        Assert.Equal(0, authentication.Calls + customers.Calls + countries.Calls);
    }

    [Fact]
    public async Task VerifiedIdentity_LoadsMatchingProfileAndCountries()
    {
        var authentication = new StubAuthentication(new(CustomerSelfIdentityStatus.Succeeded, new(7, "identity@example.com", "+66810000000")));
        var customers = new StubCustomers(ProfileResult());
        var countries = new StubCountries(new([new Country(66, "Thailand", null, null, "TH", "THA", null, null)], true));
        var errors = new ModelStateDictionary();
        var loader = new CncAuthenticatedProfileLoader(new StubSessions("token", 7), authentication, customers, countries);

        CncAuthenticatedProfileLoadResult result = await loader.LoadAsync(
            new DefaultHttpContext(), errors, CancellationToken.None);

        Assert.Equal(CncAuthenticatedProfileLoadOutcome.Loaded, result.Outcome);
        Assert.NotNull(result.Profile);
        Assert.Equal(7, result.Customer!.Id);
        Assert.Equal("Stored", result.Profile!.Details.FirstName);
        Assert.Equal("Thailand", result.Profile.Details.Country);
        Assert.True(errors.IsValid);
        Assert.Equal(1, authentication.Calls);
        Assert.Equal(1, customers.Calls);
        Assert.Equal(1, countries.Calls);
    }

    [Theory]
    [InlineData(CustomerSelfIdentityStatus.NotAuthorized, (int)CncAuthenticatedProfileLoadOutcome.Rejected)]
    [InlineData(CustomerSelfIdentityStatus.IdentityMismatch, (int)CncAuthenticatedProfileLoadOutcome.Rejected)]
    [InlineData(CustomerSelfIdentityStatus.Unavailable, (int)CncAuthenticatedProfileLoadOutcome.Unavailable)]
    public async Task FailedSelfIdentity_NeverLoadsCustomerData(
        CustomerSelfIdentityStatus status,
        int expected)
    {
        var customers = new StubCustomers();
        var countries = new StubCountries();
        var errors = new ModelStateDictionary();
        var loader = new CncAuthenticatedProfileLoader(
            new StubSessions("token", 7), new StubAuthentication(new(status)), customers, countries);

        CncAuthenticatedProfileLoadResult result = await loader.LoadAsync(
            new DefaultHttpContext(), errors, CancellationToken.None);

        Assert.Equal((CncAuthenticatedProfileLoadOutcome)expected, result.Outcome);
        Assert.False(errors.IsValid);
        Assert.Equal(0, customers.Calls + countries.Calls);
    }

    [Fact]
    public async Task MismatchedSuccessfulIdentity_IsRejectedBeforeCustomerLoad()
    {
        var customers = new StubCustomers();
        var countries = new StubCountries();
        var loader = new CncAuthenticatedProfileLoader(
            new StubSessions("token", 7),
            new StubAuthentication(new(CustomerSelfIdentityStatus.Succeeded, new(8, null, null))),
            customers,
            countries);

        CncAuthenticatedProfileLoadResult result = await loader.LoadAsync(
            new DefaultHttpContext(), new ModelStateDictionary(), CancellationToken.None);

        Assert.Equal(CncAuthenticatedProfileLoadOutcome.Rejected, result.Outcome);
        Assert.Equal(0, customers.Calls + countries.Calls);
    }

    [Fact]
    public async Task DownstreamUnavailability_FailsClosed()
    {
        var errors = new ModelStateDictionary();
        var loader = new CncAuthenticatedProfileLoader(
            new StubSessions("token", 7),
            new StubAuthentication(new(CustomerSelfIdentityStatus.Succeeded, new(7, null, null))),
            new StubCustomers(new(null, false, true)),
            new StubCountries(new([], true)));

        CncAuthenticatedProfileLoadResult result = await loader.LoadAsync(
            new DefaultHttpContext(), errors, CancellationToken.None);

        Assert.Equal(CncAuthenticatedProfileLoadOutcome.Unavailable, result.Outcome);
        Assert.False(errors.IsValid);
    }

    private static CustomerAccountProfileResult ProfileResult() => new(
        new CustomerAccountDetails(
            7, "Stored", "Customer", "Stored Customer", null, null, null, "stored@example.com", null,
            null, 9, 9, null, null,
            new CustomerAddress(9, null, "36/1 Moo 3", null, "Pak Kret", "Nonthaburi", "11120", 66, null, null),
            null,
            new CustomerAddress(9, null, "36/1 Moo 3", null, "Pak Kret", "Nonthaburi", "11120", 66, null, null)),
        true,
        true);

    private sealed class StubSessions(string? token, int? customerId) : IAccountSessionManager
    {
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult(token);
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult(customerId);
    }

    private sealed class StubAuthentication(CustomerSelfIdentityResult? result = null) : ICustomerAuthenticationClient
    {
        internal int Calls { get; private set; }
        public Task<CustomerSelfIdentityResult> GetSelfIdentityAsync(string accessToken, int expectedCustomerId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(result ?? new(CustomerSelfIdentityStatus.NotAuthorized)); }
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
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCustomers(CustomerAccountProfileResult? result = null) : ICustomerAccountClient
    {
        internal int Calls { get; private set; }
        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(result ?? new(null, true, false)); }
        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCountries(ServiceResponse<IReadOnlyList<Country>>? result = null) : ICountryClient
    {
        internal int Calls { get; private set; }
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) { Calls++; return Task.FromResult(result ?? new([], true)); }
    }
}
