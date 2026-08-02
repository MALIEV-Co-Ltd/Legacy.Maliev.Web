namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberCreatePasswordDisplayModel(IReadOnlyList<string> Errors)
{
    public static MemberCreatePasswordDisplayModel Empty { get; } = new([]);
}
