using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

public static class MemberOverviewLoader
{
    public static async Task<MemberOverviewLoadResult?> LoadAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerAccountClient accountClient,
        ICustomerOrderClient orderClient,
        ICustomerQuotationClient quotationClient,
        CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is null)
        {
            return null;
        }

        var profileTask = accountClient.GetProfileAsync(customerId.Value, cancellationToken);
        var ordersTask = orderClient.ListAsync(customerId.Value, "OrderCreatedDate_Descending", null, 1, 5, cancellationToken);
        var quotationsTask = quotationClient.ListAsync(customerId.Value, "QuotationCreatedDate_Descending", null, 1, 5, cancellationToken);
        await Task.WhenAll(profileTask, ordersTask, quotationsTask);

        var profile = await profileTask;
        var orders = await ordersTask;
        var quotations = await quotationsTask;
        var recentOrders = orders.Page?.Items ?? [];
        var recentQuotations = quotations.Page?.Items ?? [];
        var notices = new List<string>();
        if (profile.Profile is null)
        {
            notices.Add(profile.ServiceAvailable
                ? "Your customer profile could not be loaded."
                : "Customer service is temporarily unavailable.");
        }
        else
        {
            if (profile.Profile.BillingAddress is null) notices.Add("Add a billing address to complete your account.");
            if (profile.Profile.ShippingAddress is null) notices.Add("Add a shipping address before an order is dispatched.");
        }

        if (orders.Page is null)
        {
            notices.Add(orders.ServiceAvailable ? "Recent orders could not be loaded." : "Order service is temporarily unavailable.");
        }

        if (quotations.Page is null)
        {
            notices.Add(quotations.ServiceAvailable ? "Recent quotations could not be loaded." : "Quotation service is temporarily unavailable.");
        }
        else if (recentQuotations.Any(value => value.Accepted is null))
        {
            notices.Add("You have an open quotation to review.");
        }

        var displayModel = new MemberOverviewDisplayModel(
            profile.Profile?.FirstName,
            notices,
            recentOrders.Select(order => new MemberOrderSummaryDisplayModel(
                order.Id,
                order.Name,
                order.CreatedDate?.ToString("yyyy-MM-dd") ?? "-")).ToArray(),
            recentQuotations.Select(quotation => new MemberQuotationSummaryDisplayModel(
                quotation.Id,
                quotation.Accepted,
                quotation.ExpirationDate.ToString("yyyy-MM-dd"))).ToArray());
        return new MemberOverviewLoadResult(profile.Profile, recentOrders, recentQuotations, notices, displayModel);
    }
}

public sealed record MemberOverviewLoadResult(
    CustomerAccountDetails? Profile,
    IReadOnlyList<CustomerOrder> RecentOrders,
    IReadOnlyList<CustomerQuotation> RecentQuotations,
    IReadOnlyList<string> Notices,
    MemberOverviewDisplayModel DisplayModel);
