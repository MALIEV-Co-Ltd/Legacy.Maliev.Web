using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class ResetPassword(
    ICustomerAuthenticationClient authenticationClient,
    IStringLocalizer<ResetPasswordContent> localizer) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(320, ErrorMessage = "Email address is too long.")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "The password reset token is required.")]
    [StringLength(256, MinimumLength = 32, ErrorMessage = "The password reset token is invalid.")]
    public string Token { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [StringLength(1024, MinimumLength = 8, ErrorMessage = "The password must be between 8 and 1024 characters.")]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please confirm your new password")]
    [DataType(DataType.Password)]
    [StringLength(1024, MinimumLength = 8, ErrorMessage = "The password must be between 8 and 1024 characters.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [TempData]
    public string? Notification { get; set; }

    public ResetPasswordFormDisplayModel DisplayModel => new(
        Email,
        Token,
        ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value!.Errors
                    .Select(error => LocalizeModelError(error.ErrorMessage))
                    .ToArray(),
                StringComparer.Ordinal));

    private string LocalizeModelError(string? errorMessage)
    {
        var key = string.IsNullOrEmpty(errorMessage) ? "The submitted value is invalid." : errorMessage;
        var result = localizer[key];
        return !result.ResourceNotFound
            ? result.Value
            : System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
                ? key
                : localizer["The submitted value is invalid."].Value;
    }

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

        Notification = localizer["Password changed. You can now sign in."];
        return RedirectToPage("/Account/Login", new { email = Email, culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName });
    }

    private void ProtectChallengeResponse()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
