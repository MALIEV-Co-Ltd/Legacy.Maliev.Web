namespace Legacy.Maliev.Web.Components.Pages.Account;

/// <summary>
/// Carries non-password sign-in fields and validation feedback for the account login form.
/// </summary>
/// <param name="Email">The submitted account email redisplayed after sign-in.</param>
/// <param name="RememberMe">Whether the visitor requested a persistent account session.</param>
/// <param name="ReturnUrl">The requested post-login destination, or null for the default destination.</param>
/// <param name="Notification">The sign-in status message, or null when absent.</param>
/// <param name="EmailConfirmationRecoveryToken">The protected recovery token used to request another confirmation email, or null when unavailable.</param>
/// <param name="ValidationErrors">The server validation messages grouped by form field.</param>
public sealed record LoginFormDisplayModel(
    string Email,
    bool RememberMe,
    string? ReturnUrl,
    string? Notification,
    string? EmailConfirmationRecoveryToken,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidationErrors)
{
    /// <summary>
    /// Collects validation messages from every field for the form-level error summary.
    /// </summary>
    public IReadOnlyList<string> AllErrors => ValidationErrors.Values.SelectMany(errors => errors).ToArray();

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
