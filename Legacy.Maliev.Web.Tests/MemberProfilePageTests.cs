using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MemberProfile = Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage.Profile;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberProfilePageTests
{
    [Fact]
    public async Task Get_MissingEncryptedSessionChallengesBeforeLoadingProfile()
    {
        var account = new StubAccountClient();
        var page = new MemberProfile(new StubSessionManager(), account)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.Null(account.CustomerId);
        Assert.Equal(MemberProfileDisplayModel.Empty, page.DisplayModel);
    }

    private sealed class StubSessionManager : IAccountSessionManager
    {
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubAccountClient : ICustomerAccountClient
    {
        public int? CustomerId { get; private set; }

        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            throw new InvalidOperationException("The profile service must not be called without a BFF customer session.");
        }

        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
