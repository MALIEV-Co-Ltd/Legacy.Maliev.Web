namespace Legacy.Maliev.Web.Components.Pages.Account;

/// <summary>
/// Carries submitted email, recovery feedback, and validation state for the password-reset request form.
/// </summary>
/// <param name="Email">The account email redisplayed after a reset request.</param>
/// <param name="Notification">The reset-request outcome message, or null before an outcome is available.</param>
/// <param name="ValidationErrors">The server validation messages grouped by form field.</param>
public sealed record ForgotPasswordFormDisplayModel(
    string Email,
    string? Notification,
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
