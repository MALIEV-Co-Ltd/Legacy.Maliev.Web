namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberOverviewDisplayModel(
    string? FirstName,
    IReadOnlyList<string> Notices,
    MemberAddressSummaryDisplayModel? BillingAddress,
    MemberAddressSummaryDisplayModel? ShippingAddress,
    IReadOnlyList<MemberOrderSummaryDisplayModel> RecentOrders,
    IReadOnlyList<MemberQuotationSummaryDisplayModel> RecentQuotations)
{
    public static MemberOverviewDisplayModel Empty { get; } = new(null, [], null, null, [], []);
}

public sealed record MemberOrderSummaryDisplayModel(int Id, string? Name, string CreatedDate);

public sealed record MemberQuotationSummaryDisplayModel(
    int Id,
    int Period,
    decimal Total,
    bool? Accepted,
    string ExpirationDate);

public sealed record MemberAddressSummaryDisplayModel(
    string? CompanyName,
    string? Building,
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryName);
