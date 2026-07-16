using Microsoft.AspNetCore.Mvc.RazorPages;
using Legacy.Maliev.Web.Components.Pages.Account;

namespace Legacy.Maliev.Web.Pages.Account;

public sealed class IndexModel : PageModel
{
    public AccountIndexDisplayModel DisplayModel => new(
        User.Identity?.IsAuthenticated == true,
        User.Identity?.Name);
}
