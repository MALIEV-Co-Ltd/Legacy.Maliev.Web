using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactTrustedCustomerLoaderTests
{
    [Fact]
    public async Task LoadAsync_AuthenticatedSessionMapsOwnedProfileAndBillingCountry()
    {
        var profile = new CustomerAccountDetails(
            27,
            "Trusted",
            "Customer",
            "Trusted Customer",
            "020000000",
            "0810000000",
            null,
            "trusted@example.com",
            null,
            9,
            11,
            null,
            null,
            null,
            new CustomerAddress(11, null, "36/1 Moo 3", null, null, null, null, 764, null, null),
            new CustomerCompany(9, "Trusted Company", null, null, null, null),
            null);
        var loader = new ContactTrustedCustomerLoader(
            new StubSessionManager(27),
            new StubCustomerAccountClient(new(profile, true, true)));
        var context = AuthenticatedContext();

        var result = await loader.LoadAsync(
            context,
            [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
            CancellationToken.None);

        Assert.True(result.IsAuthenticated);
        Assert.True(result.ServiceAvailable);
        Assert.NotNull(result.Customer);
        Assert.Equal("Trusted", result.Customer.FirstName);
        Assert.Equal("Customer", result.Customer.LastName);
        Assert.Equal("trusted@example.com", result.Customer.Email);
        Assert.Equal("0810000000", result.Customer.Phone);
        Assert.Equal("Trusted Company", result.Customer.Company);
        Assert.Equal("Thailand", result.Customer.Country);
    }

    [Fact]
    public async Task LoadAsync_AuthenticatedRequestWithoutOwnedSessionFailsClosed()
    {
        var loader = new ContactTrustedCustomerLoader(
            new StubSessionManager(null),
            new StubCustomerAccountClient(new(null, true, true)));

        var result = await loader.LoadAsync(AuthenticatedContext(), [], CancellationToken.None);

        Assert.True(result.IsAuthenticated);
        Assert.False(result.ServiceAvailable);
        Assert.Null(result.Customer);
    }

    private static DefaultHttpContext AuthenticatedContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "customer:27")],
            "test"));
        return context;
    }

    private sealed class StubSessionManager(int? customerId) : IAccountSessionManager
    {
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult(customerId);

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) =>
            Task.FromResult(AccountSignInStatus.InvalidCredentials);

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubCustomerAccountClient(CustomerAccountProfileResult profile) : ICustomerAccountClient
    {
        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken)
        {
            Assert.Equal(27, customerId);
            return Task.FromResult(profile);
        }

        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
