namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries member-owned order history with retained filters and bounded pagination links.
/// </summary>
/// <param name="Search">The optional search text retained by the history filter.</param>
/// <param name="Sort">The selected sort expression retained by the history filter.</param>
/// <param name="PageSize">The selected number of rows per page.</param>
/// <param name="Errors">The query-validation or service failures displayed with the results.</param>
/// <param name="Orders">The order rows on the current results page.</param>
/// <param name="PageIndex">The current result page index.</param>
/// <param name="TotalPages">The total number of result pages.</param>
/// <param name="TotalRecords">The total matching records reported by the service.</param>
/// <param name="PageLinks">The numbered navigation links around the current page.</param>
/// <param name="FirstHref">The first-page URL, or null when backward navigation is unavailable.</param>
/// <param name="PreviousHref">The preceding-page URL, or null on the first page.</param>
/// <param name="NextHref">The following-page URL, or null on the final page.</param>
/// <param name="LastHref">The final-page URL, or null when forward navigation is unavailable.</param>
public sealed record MemberOrderHistoryDisplayModel(
    string? Search,
    string? Sort,
    int PageSize,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberOrderHistoryItemDisplayModel> Orders,
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
    /// Provides an empty order-history view with the default page size.
    /// </summary>
    public static MemberOrderHistoryDisplayModel Empty { get; } = new(null, null, 25, [], [], 1, 0, 0, [], null, null, null, null);
}

/// <summary>
/// Carries one formatted order row in a member's order history.
/// </summary>
/// <param name="Id">The order identifier used by its detail link.</param>
/// <param name="Name">The optional order name supplied by the customer.</param>
/// <param name="Description">The optional order description.</param>
/// <param name="Quantity">The number of parts requested by the order.</param>
/// <param name="Subtotal">The formatted subtotal or unavailable placeholder.</param>
/// <param name="CreatedDate">The invariant order creation instant or unavailable placeholder.</param>
public sealed record MemberOrderHistoryItemDisplayModel(
    int Id,
    string? Name,
    string? Description,
    int Quantity,
    string Subtotal,
    string CreatedDate);

/// <summary>
/// Represents one numbered link in member result pagination.
/// </summary>
/// <param name="Number">The one-based page number displayed by the link.</param>
/// <param name="Href">The URL preserving the selected filters and page size.</param>
/// <param name="Current">Whether the link represents the current results page.</param>
public sealed record MemberPageLinkDisplayModel(int Number, string Href, bool Current);
