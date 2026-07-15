using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages;

[Authorize]
public sealed class Index(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient accountClient,
    ICustomerOrderClient orderClient,
    ICustomerQuotationClient quotationClient) : PageModel
{
    public CustomerAccountDetails? Profile { get; private set; }

    public IReadOnlyList<CustomerOrder> RecentOrders { get; private set; } = [];

    public IReadOnlyList<CustomerQuotation> RecentQuotations { get; private set; } = [];

    public IReadOnlyList<string> Notices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        var profileTask = accountClient.GetProfileAsync(customerId.Value, cancellationToken);
        var ordersTask = orderClient.ListAsync(
            customerId.Value,
            "OrderCreatedDate_Descending",
            null,
            1,
            5,
            cancellationToken);
        var quotationsTask = quotationClient.ListAsync(
            customerId.Value,
            "QuotationCreatedDate_Descending",
            null,
            1,
            5,
            cancellationToken);
        await Task.WhenAll(profileTask, ordersTask, quotationsTask);

        var profile = await profileTask;
        var orders = await ordersTask;
        var quotations = await quotationsTask;
        Profile = profile.Profile;
        RecentOrders = orders.Page?.Items ?? [];
        RecentQuotations = quotations.Page?.Items ?? [];

        var notices = new List<string>();
        if (Profile is null)
        {
            notices.Add(profile.ServiceAvailable
                ? "Your customer profile could not be loaded."
                : "Customer service is temporarily unavailable.");
        }
        else
        {
            if (Profile.BillingAddress is null) notices.Add("Add a billing address to complete your account.");
            if (Profile.ShippingAddress is null) notices.Add("Add a shipping address before an order is dispatched.");
        }

        if (orders.Page is null)
        {
            notices.Add(orders.ServiceAvailable
                ? "Recent orders could not be loaded."
                : "Order service is temporarily unavailable.");
        }

        if (quotations.Page is null)
        {
            notices.Add(quotations.ServiceAvailable
                ? "Recent quotations could not be loaded."
                : "Quotation service is temporarily unavailable.");
        }
        else if (RecentQuotations.Any(value => value.Accepted is null))
        {
            notices.Add("You have an open quotation to review.");
        }

        Notices = notices;
        return Page();
    }
}
