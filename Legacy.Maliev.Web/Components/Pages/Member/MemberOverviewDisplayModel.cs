namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberOverviewDisplayModel(
    string? FirstName,
    IReadOnlyList<string> Notices,
    IReadOnlyList<MemberOrderSummaryDisplayModel> RecentOrders,
    IReadOnlyList<MemberQuotationSummaryDisplayModel> RecentQuotations)
{
    public static MemberOverviewDisplayModel Empty { get; } = new(null, [], [], []);
}

public sealed record MemberOrderSummaryDisplayModel(int Id, string? Name, string CreatedDate);

public sealed record MemberQuotationSummaryDisplayModel(int Id, bool? Accepted, string ExpirationDate);
