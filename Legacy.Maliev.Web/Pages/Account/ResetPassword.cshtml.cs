using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Pages.Account;

/// <summary>Validates a password-reset challenge and submits the replacement password to the identity service.</summary>
/// <param name="authenticationClient">The typed identity client handling customer credential and challenge operations.</param>
/// <param name="localizer">The localizer supplying this page's validation and outcome messages.</param>
[EnableRateLimiting("account")]
public sealed class ResetPassword(
    ICustomerAuthenticationClient authenticationClient,
    IStringLocalizer<ResetPasswordContent> localizer) : PageModel
{
    /// <summary>The customer's email address used by this account or request workflow.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(320, ErrorMessage = "Email address is too long.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>The single-use identity challenge submitted to authorize this password operation.</summary>
    [BindProperty]
    [Required(ErrorMessage = "The password reset token is required.")]
    [StringLength(256, MinimumLength = 32, ErrorMessage = "The password reset token is invalid.")]
    public string Token { get; set; } = string.Empty;

    /// <summary>The password submitted to the identity workflow; it is excluded from the form display projection.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [StringLength(1024, MinimumLength = 8, ErrorMessage = "The password must be between 8 and 1024 characters.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>The repeated password used to detect a mismatched password entry before submission.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please confirm your new password")]
    [DataType(DataType.Password)]
    [StringLength(1024, MinimumLength = 8, ErrorMessage = "The password must be between 8 and 1024 characters.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The reset challenge context and localized validation feedback, excluding password entries.</summary>
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

    /// <summary>Initializes the reset form from a required email and challenge token with sensitive-response headers.</summary>
    /// <param name="email">The email address associated with this account request.</param>
    /// <param name="token">The single-use identity challenge supplied by the account link.</param>
    /// <returns>The reset page or a bad-request result for missing challenge inputs.</returns>
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

    /// <summary>Validates the form and completes the single-use password reset through identity.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A localized sign-in redirect after success or the reset page with controlled errors.</returns>
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
