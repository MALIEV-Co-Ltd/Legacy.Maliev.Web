using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class ResetPassword(ICustomerAuthenticationClient authenticationClient) : PageModel
{
    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(256, MinimumLength = 32)]
    public string Token { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [StringLength(1024, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [TempData]
    public string? Notification { get; set; }

    public IActionResult OnGet(string? email, string? token)
    {
        ProtectChallengeResponse();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return BadRequest();
        }

        Email = email;
        Token = token;
        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(CancellationToken cancellationToken)
    {
        ProtectChallengeResponse();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await authenticationClient.CompletePasswordResetAsync(
            Email,
            Token,
            Password,
            cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "The password reset link is invalid or expired.");
            return Page();
        }

        Notification = "Password changed. You can now sign in.";
        return RedirectToPage("/Account/Login", new { email = Email });
    }

    private void ProtectChallengeResponse()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
