namespace Legacy.Maliev.Web.Components.Pages.Account;

/// <summary>
/// Carries the password-creation proof and sign-in preferences without retaining the entered password.
/// </summary>
/// <param name="Email">The account email for which an initial password is being created.</param>
/// <param name="Token">The protected proof authorizing initial password creation.</param>
/// <param name="ReturnUrl">The requested destination after password creation, or null for the default.</param>
/// <param name="RememberMe">Whether the resulting account session should persist.</param>
/// <param name="ValidationErrors">The server validation messages grouped by form field.</param>
public sealed record SetInitialPasswordFormDisplayModel(
    string Email,
    string Token,
    string? ReturnUrl,
    bool RememberMe,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors)
{
    /// <summary>
    /// Looks up validation messages for a single form field.
    /// </summary>
    /// <param name="fieldName">The form field key used by the server-side validation result.</param>
    /// <returns>The messages for the field, or an empty list when none are recorded.</returns>
    public IReadOnlyList<string> ErrorsFor(string fieldName) =>
        ValidationErrors.TryGetValue(fieldName, out var errors) ? errors : [];

    /// <summary>
    /// Selects the first validation message for a field's inline feedback.
    /// </summary>
    /// <param name="fieldName">The form field key used by the server-side validation result.</param>
    /// <returns>The first field error, or null when the field has no errors.</returns>
    public string? FirstErrorFor(string fieldName) => ErrorsFor(fieldName).FirstOrDefault();
}
