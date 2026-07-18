namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberChangeEmailDisplayModel(
    string NewEmail,
    IReadOnlyList<string> Errors)
{
    public static MemberChangeEmailDisplayModel Empty { get; } = new(string.Empty, []);
}
