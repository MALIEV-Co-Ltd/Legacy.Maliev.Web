namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries the requested replacement email and feedback for the member email-change form.
/// </summary>
/// <param name="NewEmail">The replacement email address retained for correction after submission.</param>
/// <param name="Errors">The validation or account-service failures displayed by the form.</param>
public sealed record MemberChangeEmailDisplayModel(
    string NewEmail,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Provides a blank email-change form with no errors.
    /// </summary>
    public static MemberChangeEmailDisplayModel Empty { get; } = new(string.Empty, []);
}
