namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries member-owned quotation history with retained filters and bounded pagination links.
/// </summary>
/// <param name="Search">The optional search text retained by the history filter.</param>
/// <param name="Sort">The selected sort expression retained by the history filter.</param>
/// <param name="PageSize">The selected number of rows per page.</param>
/// <param name="Errors">The query-validation or service failures displayed with the results.</param>
/// <param name="Quotations">The quotation rows on the current results page.</param>
/// <param name="PageIndex">The current result page index.</param>
/// <param name="TotalPages">The total number of result pages.</param>
/// <param name="TotalRecords">The total matching records reported by the service.</param>
/// <param name="PageLinks">The numbered navigation links around the current page.</param>
/// <param name="FirstHref">The first-page URL, or null when backward navigation is unavailable.</param>
/// <param name="PreviousHref">The preceding-page URL, or null on the first page.</param>
/// <param name="NextHref">The following-page URL, or null on the final page.</param>
/// <param name="LastHref">The final-page URL, or null when forward navigation is unavailable.</param>
public sealed record MemberQuotationsIndexDisplayModel(
    string? Search,
    string? Sort,
    int PageSize,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberQuotationListItemDisplayModel> Quotations,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    IReadOnlyList<MemberPageLinkDisplayModel> PageLinks,
    string? FirstHref,
    string? PreviousHref,
    string? NextHref,
    string? LastHref)
{
    /// <summary>
    /// Provides an empty quotation-history view with the default page size.
    /// </summary>
    public static MemberQuotationsIndexDisplayModel Empty { get; } = new(null, null, 25, [], [], 1, 0, 0, [], null, null, null, null);
}

/// <summary>
/// Carries the decision state and formatted amounts of one member quotation row.
/// </summary>
/// <param name="Id">The quotation identifier used by its detail link.</param>
/// <param name="Accepted">The customer's acceptance decision, or null before a decision is recorded.</param>
/// <param name="QuotedAmount">The formatted amount quoted to the customer or unavailable placeholder.</param>
/// <param name="CurrencyId">The currency identifier supplied with the quotation.</param>
/// <param name="ExpirationDate">The invariant quotation expiration date.</param>
/// <param name="CreatedDate">The invariant quotation creation instant or unavailable placeholder.</param>
public sealed record MemberQuotationListItemDisplayModel(
    int Id,
    bool? Accepted,
    string QuotedAmount,
    int CurrencyId,
    string ExpirationDate,
    string CreatedDate);
