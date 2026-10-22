namespace Legacy.Maliev.Web.Components.Pages.Account;

/// <summary>
/// Carries user-facing failures returned while confirming a newly registered account email.
/// </summary>
/// <param name="Errors">The confirmation errors displayed when the supplied confirmation cannot be completed.</param>
public sealed record EmailConfirmationDisplayModel(IReadOnlyList<string> Errors);
