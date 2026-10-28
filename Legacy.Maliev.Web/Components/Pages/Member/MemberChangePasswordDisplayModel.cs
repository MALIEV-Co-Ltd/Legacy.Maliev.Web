namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries validation feedback for changing an existing member password without retaining password values.
/// </summary>
/// <param name="Errors">The password-change errors displayed by the member form.</param>
public sealed record MemberChangePasswordDisplayModel(IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Provides an initial password-change form with no errors.
    /// </summary>
    public static MemberChangePasswordDisplayModel Empty { get; } = new([]);
}
