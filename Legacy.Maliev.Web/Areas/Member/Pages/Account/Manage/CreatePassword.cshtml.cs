using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

[Authorize]
public sealed class CreatePassword : PageModel
{
    public IActionResult OnGet() => RedirectToPage("ChangePassword");
}
