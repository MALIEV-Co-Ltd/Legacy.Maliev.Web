using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Loads account and recent-order information for the signed-in member overview.
/// </summary>
public static class MemberOverviewLoader
{
    /// <summary>
    /// Loads the member profile, country labels, and five recent orders and quotations concurrently.
    /// </summary>
    /// <param name="context">The current authenticated request.</param>
    /// <param name="sessionManager">The server-side session accessor used to resolve the customer identifier.</param>
    /// <param name="accountClient">The account client used to retrieve the customer profile.</param>
    /// <param name="countryClient">The country lookup client used to label saved addresses.</param>
    /// <param name="orderClient">The customer-scoped client used to retrieve recent orders.</param>
    /// <param name="quotationClient">The customer-scoped client used to retrieve recent quotations.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The loaded account data and overview model, or null when no customer session is available.</returns>
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
        var noticeDetails = new List<MemberOverviewNotice>();
        void AddNotice(string content, MemberOverviewNoticeSeverity severity = MemberOverviewNoticeSeverity.Information)
        {
            notices.Add(content);
            noticeDetails.Add(new MemberOverviewNotice(content, severity));
        }

        if (profile.Profile is null)
        {
            AddNotice(profile.ServiceAvailable
                ? "Your customer profile could not be loaded."
                : "Customer service is temporarily unavailable.");
        }
        else
        {
            if (profile.Profile.BillingAddress is null) AddNotice("Add a billing address to complete your account.", MemberOverviewNoticeSeverity.Error);
            if (profile.Profile.ShippingAddress is null) AddNotice("Add a shipping address before an order is dispatched.", MemberOverviewNoticeSeverity.Warning);
        }

        if (orders.Page is null)
        {
            AddNotice(orders.ServiceAvailable ? "Recent orders could not be loaded." : "Order service is temporarily unavailable.");
        }

        if (quotations.Page is null)
        {
            AddNotice(quotations.ServiceAvailable ? "Recent quotations could not be loaded." : "Quotation service is temporarily unavailable.");
        }
        else if (recentQuotations.Any(value => value.Accepted is null))
        {
            AddNotice("You have an open quotation to review.", MemberOverviewNoticeSeverity.Warning);
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
                quotation.ExpirationDate.ToString("O"))).ToArray())
        {
            NoticeDetails = noticeDetails.ToArray(),
        };
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

/// <summary>
/// Carries loaded overview data together with its public member presentation projection.
/// </summary>
/// <param name="Profile">The loaded customer profile, or null when unavailable.</param>
/// <param name="RecentOrders">The recent customer-scoped orders returned by the service.</param>
/// <param name="RecentQuotations">The recent customer-scoped quotations returned by the service.</param>
/// <param name="Notices">The account reminders and partial-service failure notices.</param>
/// <param name="DisplayModel">The formatted overview model rendered by the member component.</param>
public sealed record MemberOverviewLoadResult(
    CustomerAccountDetails? Profile,
    IReadOnlyList<CustomerOrder> RecentOrders,
    IReadOnlyList<CustomerQuotation> RecentQuotations,
    IReadOnlyList<string> Notices,
    MemberOverviewDisplayModel DisplayModel);
