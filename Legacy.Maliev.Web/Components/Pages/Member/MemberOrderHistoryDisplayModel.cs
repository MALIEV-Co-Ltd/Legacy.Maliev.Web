namespace Legacy.Maliev.Web.Components.Pages.Member;

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
    public static MemberOrderHistoryDisplayModel Empty { get; } = new(null, null, 25, [], [], 1, 0, 0, [], null, null, null, null);
}

public sealed record MemberOrderHistoryItemDisplayModel(
    int Id,
    string? Name,
    string? Description,
    int Quantity,
    string Subtotal,
    string CreatedDate);

public sealed record MemberPageLinkDisplayModel(int Number, string Href, bool Current);
