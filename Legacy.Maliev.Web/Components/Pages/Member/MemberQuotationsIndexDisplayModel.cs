namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberQuotationsIndexDisplayModel(
    string? Search,
    string? Sort,
    int PageSize,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberQuotationListItemDisplayModel> Quotations,
    string? PreviousHref,
    string? NextHref)
{
    public static MemberQuotationsIndexDisplayModel Empty { get; } = new(null, null, 25, [], [], null, null);
}

public sealed record MemberQuotationListItemDisplayModel(
    int Id,
    bool? Accepted,
    string QuotedAmount,
    int CurrencyId,
    string ExpirationDate,
    string CreatedDate);
