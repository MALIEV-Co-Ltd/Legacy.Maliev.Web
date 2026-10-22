namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries validation feedback for creating a member's initial password without retaining password values.
/// </summary>
/// <param name="Errors">The password-creation errors displayed by the member form.</param>
public sealed record MemberCreatePasswordDisplayModel(IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Provides an initial password-creation form with no errors.
    /// </summary>
    public static MemberCreatePasswordDisplayModel Empty { get; } = new([]);
}
