using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MemberDashboard = Legacy.Maliev.Web.Areas.Member.Pages.Index;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberDashboardPageTests
{
    [Fact]
    public async Task Get_ComposesOwnedProfileOrdersAndQuotationsFromSessionCustomer()
    {
        var account = new StubAccountClient(Profile());
        var orders = new StubOrderClient(new CustomerOrderPage([Order()], 1, 1, 1));
        var quotations = new StubQuotationClient(new CustomerQuotationPage([Quotation()], 1, 1, 1));
        var page = CreatePage(new StubSessionManager(42), account, orders, quotations);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(42, account.CustomerId);
        Assert.Equal(42, orders.CustomerId);
        Assert.Equal(42, quotations.CustomerId);
        Assert.Equal(7, Assert.Single(page.RecentOrders).Id);
        Assert.Equal(9, Assert.Single(page.RecentQuotations).Id);
        Assert.Contains(page.Notices, value => value.Contains("billing address", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(page.Notices, value => value.Contains("open quotation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Get_MissingEncryptedSessionChallengesBeforeCallingServices()
    {
        var account = new StubAccountClient(Profile());
        var orders = new StubOrderClient(new CustomerOrderPage([], 1, 0, 0));
        var quotations = new StubQuotationClient(new CustomerQuotationPage([], 1, 0, 0));
        var page = CreatePage(new StubSessionManager(null), account, orders, quotations);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<ChallengeResult>(result);
        Assert.Null(account.CustomerId);
        Assert.Null(orders.CustomerId);
        Assert.Null(quotations.CustomerId);
    }

    private static MemberDashboard CreatePage(
        IAccountSessionManager session,
        ICustomerAccountClient account,
        ICustomerOrderClient orders,
        ICustomerQuotationClient quotations) => new(session, account, orders, quotations)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

    private static CustomerAccountDetails Profile() => new(
        42,
        "Local",
        "Customer",
        "Local Customer",
        null,
        null,
        null,
        "local@example.test",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    private static CustomerOrder Order() => new(
        7,
        42,
        "CNC part",
        null,
        3,
        2,
        0,
        2,
        50,
        0,
        100,
        5,
        null,
        null,
        null,
        true,
        false,
        null,
        new DateTime(2026, 7, 15),
        null);

    private static CustomerQuotation Quotation() => new(
        9,
        42,
        null,
        30,
        new DateTime(2026, 12, 31),
        100,
        7,
        107,
        3,
        104,
        764,
        null,
        null,
        null,
        null,
        null,
        new DateTime(2026, 7, 15),
        null);

    private sealed class StubSessionManager(int? customerId) : IAccountSessionManager
    {
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult(customerId);

        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubAccountClient(CustomerAccountDetails profile) : ICustomerAccountClient
    {
        public int? CustomerId { get; private set; }

        public Task<CustomerAccountProfileResult> GetProfileAsync(int customerId, CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            return Task.FromResult(new CustomerAccountProfileResult(profile, true, true));
        }

        public Task<CustomerAddressProfileResult> GetAddressProfileAsync(int customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateAddressesAsync(int customerId, CustomerAddressUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateEmailAsync(int customerId, string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAddressOperationResult> UpdateProfileAsync(int customerId, CustomerProfileUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubOrderClient(CustomerOrderPage page) : ICustomerOrderClient
    {
        public int? CustomerId { get; private set; }

        public Task<CustomerOrderListResult> ListAsync(int customerId, string? sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            return Task.FromResult(new CustomerOrderListResult(page, true, true));
        }

        public Task<CustomerOrderDetailsResult> GetAsync(int customerId, int orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerOrderOperationResult> CancelAsync(int customerId, int orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubQuotationClient(CustomerQuotationPage page) : ICustomerQuotationClient
    {
        public int? CustomerId { get; private set; }

        public Task<CustomerQuotationListResult> ListAsync(int customerId, string? sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken)
        {
            CustomerId = customerId;
            return Task.FromResult(new CustomerQuotationListResult(page, true, true));
        }

        public Task<CustomerQuotationDetailsResult> GetAsync(int customerId, int quotationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
