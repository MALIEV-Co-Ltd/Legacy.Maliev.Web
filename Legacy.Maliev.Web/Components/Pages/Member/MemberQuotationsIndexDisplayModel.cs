namespace Legacy.Maliev.Web.Components.Pages.Member;

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
    public static MemberQuotationsIndexDisplayModel Empty { get; } = new(null, null, 25, [], [], 1, 0, 0, [], null, null, null, null);
}

public sealed record MemberQuotationListItemDisplayModel(
    int Id,
    bool? Accepted,
    string QuotedAmount,
    int CurrencyId,
    string ExpirationDate,
    string CreatedDate);
