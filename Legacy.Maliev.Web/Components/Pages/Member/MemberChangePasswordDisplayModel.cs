namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberChangePasswordDisplayModel(IReadOnlyList<string> Errors)
{
    public static MemberChangePasswordDisplayModel Empty { get; } = new([]);
}
