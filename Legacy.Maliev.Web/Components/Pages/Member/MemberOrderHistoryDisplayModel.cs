namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberOrderHistoryDisplayModel(
    string? Search,
    string? Sort,
    int PageSize,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberOrderHistoryItemDisplayModel> Orders,
    string? PreviousHref,
    string? NextHref)
{
    public static MemberOrderHistoryDisplayModel Empty { get; } = new(null, null, 25, [], [], null, null);
}

public sealed record MemberOrderHistoryItemDisplayModel(
    int Id,
    string? Name,
    string? Description,
    int Quantity,
    string Subtotal,
    string CreatedDate);
