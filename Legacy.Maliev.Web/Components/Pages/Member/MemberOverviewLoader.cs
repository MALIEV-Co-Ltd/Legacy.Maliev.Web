using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

public static class MemberOverviewLoader
{
    public static async Task<MemberOverviewLoadResult?> LoadAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerAccountClient accountClient,
        ICountryClient countryClient,
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
        var countriesTask = countryClient.GetCountriesAsync(cancellationToken);
        var ordersTask = orderClient.ListAsync(customerId.Value, "OrderCreatedDate_Descending", null, 1, 5, cancellationToken);
        var quotationsTask = quotationClient.ListAsync(customerId.Value, "QuotationCreatedDate_Descending", null, 1, 5, cancellationToken);
        await Task.WhenAll(profileTask, countriesTask, ordersTask, quotationsTask);

        var profile = await profileTask;
        var countries = await countriesTask;
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
            CreateAddress(profile.Profile?.BillingAddress, profile.Profile?.Company?.Name, countries.Value),
            CreateAddress(profile.Profile?.ShippingAddress, null, countries.Value),
            recentOrders.Select(order => new MemberOrderSummaryDisplayModel(
                order.Id,
                order.Name,
                order.CreatedDate?.ToString("O") ?? "-")).ToArray(),
            recentQuotations.Select(quotation => new MemberQuotationSummaryDisplayModel(
                quotation.Id,
                quotation.Period,
                quotation.Total,
                quotation.Accepted,
                quotation.ExpirationDate.ToString("O"))).ToArray());
        return new MemberOverviewLoadResult(profile.Profile, recentOrders, recentQuotations, notices, displayModel);
    }

    private static MemberAddressSummaryDisplayModel? CreateAddress(
        CustomerAddress? address,
        string? companyName,
        IReadOnlyList<Country>? countries)
    {
        if (address is null) return null;

        return new(
            companyName,
            address.Building,
            address.AddressLine1,
            address.AddressLine2,
            address.City,
            address.State,
            address.PostalCode,
            countries?.FirstOrDefault(country => country.Id == address.CountryId)?.Name);
    }
}

public sealed record MemberOverviewLoadResult(
    CustomerAccountDetails? Profile,
    IReadOnlyList<CustomerOrder> RecentOrders,
    IReadOnlyList<CustomerQuotation> RecentQuotations,
    IReadOnlyList<string> Notices,
    MemberOverviewDisplayModel DisplayModel);
