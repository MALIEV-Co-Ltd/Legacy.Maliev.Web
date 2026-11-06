namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberOverviewDisplayModel(
    string? FirstName,
    IReadOnlyList<string> Notices,
    MemberAddressSummaryDisplayModel? BillingAddress,
    MemberAddressSummaryDisplayModel? ShippingAddress,
    IReadOnlyList<MemberOrderSummaryDisplayModel> RecentOrders,
    IReadOnlyList<MemberQuotationSummaryDisplayModel> RecentQuotations)
{
    /// <summary>Gets source-assigned notice severities; legacy string-only callers remain supported.</summary>
    public IReadOnlyList<MemberOverviewNotice> NoticeDetails { get; init; } = [];

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

/// <summary>Identifies the retained information, warning and error semantics of member notices.</summary>
public enum MemberOverviewNoticeSeverity
{
    /// <summary>Informational content, including modern availability notices without a source severity mapping.</summary>
    Information = 0,

    /// <summary>A retained pending-quotation or missing-shipping-address warning.</summary>
    Warning = 1,

    /// <summary>A retained missing-billing-address error.</summary>
    Error = 2,
}

/// <summary>Pairs a localized member-notice key with its source-assigned severity.</summary>
/// <param name="Content">The existing notice text or localization key.</param>
/// <param name="Severity">The explicit producer severity, informational by default.</param>
public sealed record MemberOverviewNotice(
    string Content,
    MemberOverviewNoticeSeverity Severity = MemberOverviewNoticeSeverity.Information);
