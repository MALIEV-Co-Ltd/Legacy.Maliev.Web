using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class ChangeEmailConfirmation(ICustomerAuthenticationClient authenticationClient) : PageModel
{
    [TempData]
    public string? Notification { get; set; }

    public async Task<IActionResult> OnGetAsync(
        string? email,
        string? token,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return BadRequest();
        }

        if (await authenticationClient.CompleteEmailConfirmationAsync(
            email,
            token,
            cancellationToken))
        {
            Notification = "Email change confirmed. Sign in with your new address.";
            return RedirectToPage("/Account/Login", new { email });
        }

        ModelState.AddModelError(string.Empty, "The email-change link is invalid or expired.");
        return Page();
    }
}
