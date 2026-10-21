namespace Legacy.Maliev.Web.Components.Pages.Account;

/// <summary>
/// Carries user-facing failures returned while confirming an account email change.
/// </summary>
/// <param name="Errors">The confirmation errors displayed instead of a successful email-change result.</param>
public sealed record ChangeEmailConfirmationDisplayModel(IReadOnlyList<string> Errors);
