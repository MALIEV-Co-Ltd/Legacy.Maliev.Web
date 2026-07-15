using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.Account;

public sealed class ChangeEmailConfirmation : PageModel
{
    public IActionResult OnGet(string? id, string? email, string? token)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(token))
        {
            return BadRequest();
        }

        ModelState.AddModelError(
            string.Empty,
            "This legacy email-change link cannot be verified. Sign in and request a new link.");
        return Page();
    }
}
