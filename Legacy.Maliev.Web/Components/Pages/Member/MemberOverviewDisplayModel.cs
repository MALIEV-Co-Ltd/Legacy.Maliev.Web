namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries the signed-in member's account summary, address reminders, and recent activity.
/// </summary>
/// <param name="FirstName">The member's given name, or null when the profile is unavailable.</param>
/// <param name="Notices">Account-completion reminders and unavailable-service notices.</param>
/// <param name="BillingAddress">The saved billing address summary, or null when absent.</param>
/// <param name="ShippingAddress">The saved shipping address summary, or null when absent.</param>
/// <param name="RecentOrders">The recent orders selected for the member overview.</param>
/// <param name="RecentQuotations">The recent quotations selected for the member overview.</param>
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

    /// <summary>
    /// Provides an account overview without profile or recent-activity data.
    /// </summary>
    public static MemberOverviewDisplayModel Empty { get; } = new(null, [], null, null, [], []);
}

/// <summary>
/// Carries an order link and creation timestamp for the member overview.
/// </summary>
/// <param name="Id">The order identifier used to open its details.</param>
/// <param name="Name">The optional order name displayed in recent activity.</param>
/// <param name="CreatedDate">The invariant order creation instant or unavailable placeholder.</param>
public sealed record MemberOrderSummaryDisplayModel(int Id, string? Name, string CreatedDate);

/// <summary>
/// Carries a recent quotation's value, decision state, and validity for the member overview.
/// </summary>
/// <param name="Id">The quotation identifier used to open its details.</param>
/// <param name="Period">The quotation period supplied by the quotation service.</param>
/// <param name="Total">The quotation total supplied by the service.</param>
/// <param name="Accepted">The customer's acceptance decision, or null before a decision.</param>
/// <param name="ExpirationDate">The invariant quotation expiration date.</param>
public sealed record MemberQuotationSummaryDisplayModel(
    int Id,
    int Period,
    decimal Total,
    bool? Accepted,
    string ExpirationDate);

/// <summary>
/// Carries the saved postal-address summary displayed on the member overview.
/// </summary>
/// <param name="CompanyName">The optional company name associated with this address summary.</param>
/// <param name="Building">The optional building or organization-specific address line.</param>
/// <param name="AddressLine1">The first street-address line.</param>
/// <param name="AddressLine2">The optional additional street-address line.</param>
/// <param name="City">The optional city or district.</param>
/// <param name="State">The optional province or region.</param>
/// <param name="PostalCode">The optional postal delivery code.</param>
/// <param name="CountryName">The resolved country label, or null when lookup data is unavailable.</param>
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
