namespace Legacy.Maliev.Web.Components.Pages.Account;

/// <summary>
/// Carries the authenticated account summary displayed on the account landing page.
/// </summary>
/// <param name="IsAuthenticated">Whether the current request has an authenticated identity.</param>
/// <param name="DisplayName">The identity name displayed to the signed-in visitor, or null when unavailable.</param>
public sealed record AccountIndexDisplayModel(bool IsAuthenticated, string? DisplayName);
