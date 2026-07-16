using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.Account;

[Authorize]
public sealed class Logout(IAccountSessionManager sessionManager) : PageModel
{
    public void OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await sessionManager.SignOutAsync(HttpContext, cancellationToken);
        return RedirectToPage("/Index");
    }
}
