using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Pages.Account;

/// <summary>Completes an email-verification challenge through the customer identity service.</summary>
/// <param name="authenticationClient">The typed identity client handling customer credential and challenge operations.</param>
[EnableRateLimiting("account")]
public sealed class EmailConfirmation(ICustomerAuthenticationClient authenticationClient) : PageModel
{
    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The safe email-verification errors for page rendering.</summary>
    public EmailConfirmationDisplayModel DisplayModel => new(
        ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                ? "The confirmation link is invalid or expired."
                : error.ErrorMessage)
            .ToArray());

    /// <summary>Completes the email-verification challenge without caching or referring the challenge URL.</summary>
    /// <param name="email">The email address associated with this account request.</param>
    /// <param name="token">The single-use identity challenge supplied by the account link.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A sign-in redirect after success, a bad-request result for missing inputs, or the page with an invalid-link error.</returns>
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
