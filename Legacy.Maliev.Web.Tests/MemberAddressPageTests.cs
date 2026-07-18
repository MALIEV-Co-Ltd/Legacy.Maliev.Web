using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MemberAddressPage = Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage.Address;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberAddressPageTests
{
    [Fact]
    public async Task Get_MissingEncryptedSessionChallengesBeforeCallingServices()
    {
        var account = new StubAccountClient();
        var countries = new StubCountryClient();
        var page = new MemberAddressPage(new StubSessionManager(), account, countries)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.False(account.WasCalled);
        Assert.False(countries.WasCalled);
    }

    private sealed class StubSessionManager : IAccountSessionManager
    {
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult<int?>(null);
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubAccountClient : ICustomerAccountClient
    {
        public bool WasCalled { get; private set; }
        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Customer service must not be called without a BFF session.");
        }

        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public bool WasCalled { get; private set; }
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Country service must not be called without a BFF session.");
        }
    }
}
