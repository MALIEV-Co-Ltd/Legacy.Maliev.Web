using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class EmailConfirmation(ICustomerAuthenticationClient authenticationClient) : PageModel
{
    [TempData]
    public string? Notification { get; set; }

    public EmailConfirmationDisplayModel DisplayModel => new(
        ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                ? "The confirmation link is invalid or expired."
                : error.ErrorMessage)
            .ToArray());

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
            Notification = "Email confirmed. You can now sign in.";
            return RedirectToPage("/Account/Login", new { email });
        }

        ModelState.AddModelError(string.Empty, "The confirmation link is invalid or expired.");
        return Page();
    }
}
