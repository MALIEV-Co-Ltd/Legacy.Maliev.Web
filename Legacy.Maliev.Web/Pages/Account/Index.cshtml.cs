using Microsoft.AspNetCore.Mvc.RazorPages;
using Legacy.Maliev.Web.Components.Pages.Account;

namespace Legacy.Maliev.Web.Pages.Account;

/// <summary>Projects the current sign-in state for the public account landing page.</summary>
public sealed class IndexModel : PageModel
{
    /// <summary>The current authenticated state and identity display name for the account landing page.</summary>
    public AccountIndexDisplayModel DisplayModel => new(
        User.Identity?.IsAuthenticated == true,
        User.Identity?.Name);
}
