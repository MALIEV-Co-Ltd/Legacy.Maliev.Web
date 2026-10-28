using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Pages.Account;

/// <summary>Requests a password-reset challenge and sends a localized single-use link without disclosing account eligibility.</summary>
/// <param name="authenticationClient">The typed identity client handling customer credential and challenge operations.</param>
/// <param name="notificationClient">The typed notification client used for customer and staff messages.</param>
/// <param name="localizer">The localizer supplying this page's validation and outcome messages.</param>
/// <param name="logger">The logger instance for diagnostic output.</param>
[EnableRateLimiting("account")]
public sealed class ForgotPassword(
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    IStringLocalizer<ForgotPasswordContent> localizer,
    ILogger<ForgotPassword> logger) : PageModel
{
    /// <summary>The customer's email address used by this account or request workflow.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(320, ErrorMessage = "Email address is too long.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The reset-request email, outcome, and localized validation feedback.</summary>
    public ForgotPasswordFormDisplayModel DisplayModel => new(
        Email,
        Notification,
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

    /// <summary>Requests a reset challenge and sends a localized reset link when identity permits it.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The invalid form or a localized redirect carrying a non-enumerating outcome message.</returns>
    public async Task<IActionResult> OnPostPasswordResetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var challenge = await authenticationClient.RequestPasswordResetAsync(
            Email.Trim(),
            cancellationToken);
        if (challenge.Token is not null)
        {
            _ = await SendResetAsync(challenge.Token, cancellationToken);
        }

        Notification = localizer["If an eligible account exists, a password reset link has been sent."];
        return RedirectToPage(new { culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName });
    }

    private async Task<bool> SendResetAsync(string token, CancellationToken cancellationToken)
    {
        var callback = QueryHelpers.AddQueryString(
            $"{CanonicalUrlPolicy.CanonicalOrigin}/Account/ResetPassword",
            new Dictionary<string, string?>
            {
                ["email"] = Email.Trim(),
                ["token"] = token,
                ["culture"] = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            });

        try
        {
            var safeCallback = WebUtility.HtmlEncode(callback);
            var result = await notificationClient.SendAsync(
                NotificationChannel.NoReply,
                new EmailNotification(
                    Email.Trim(),
                    localizer["Reset your MALIEV password"],
                    $"<p>{WebUtility.HtmlEncode(localizer["Use this single-use link to reset your password:"])}</p><p><a href=\"{safeCallback}\">{WebUtility.HtmlEncode(localizer["Reset password"])}</a></p><p>{WebUtility.HtmlEncode(localizer["If you did not request this, you can ignore this email."])}</p>",
                    null,
                    null,
                    ["mail-tracking@maliev.com"]),
                cancellationToken);
            if (!result.Sent)
            {
                logger.LogWarning("Password reset notification delivery failed.");
            }

            return result.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Password reset notification delivery failed.");
            return false;
        }
    }
}
