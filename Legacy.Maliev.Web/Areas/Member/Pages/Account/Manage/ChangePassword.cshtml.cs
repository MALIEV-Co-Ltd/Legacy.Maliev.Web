using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

/// <summary>Changes the signed-in customer's password, ends the current session, and sends a security notification.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="authenticationClient">The typed identity client handling customer credential and challenge operations.</param>
/// <param name="notificationClient">The typed notification client used for customer and staff messages.</param>
/// <param name="logger">The logger instance for diagnostic output.</param>
[Authorize]
[EnableRateLimiting("account")]
public sealed class ChangePassword(
    IAccountSessionManager sessionManager,
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    ILogger<ChangePassword> logger) : PageModel
{
    /// <summary>The existing password submitted for identity verification before an account security change.</summary>
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024)]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>The replacement password submitted to the identity service after local validation.</summary>
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>The repeated password used to detect a mismatched password entry before submission.</summary>
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The safe password-change validation feedback without password values.</summary>
    public MemberChangePasswordDisplayModel DisplayModel { get; private set; } = MemberChangePasswordDisplayModel.Empty;

    /// <summary>Requires a usable server-side session before displaying the password-change form.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The password-change page or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var accessToken = await sessionManager.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Challenge();
        }

        BuildDisplayModel();
        return Page();
    }

    /// <summary>Changes the identity password and signs out the current session before requesting a security notification.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A sign-in redirect after success, the form with safe errors, or an authentication challenge.</returns>
    public async Task<IActionResult> OnPostChangePasswordAsync(CancellationToken cancellationToken)
    {
        var accessToken = await sessionManager.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            BuildDisplayModel();
            return Page();
        }

        var result = await authenticationClient.ChangePasswordAsync(
            accessToken,
            CurrentPassword,
            NewPassword,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "The current password is invalid or the new password was rejected."
                    : "Account security is temporarily unavailable.");
            BuildDisplayModel();
            return Page();
        }

        await sessionManager.SignOutAsync(HttpContext, CancellationToken.None);

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(email))
        {
            try
            {
                var notification = await notificationClient.SendAsync(
                    NotificationChannel.NoReply,
                    new EmailNotification(
                        email,
                        "Your MALIEV password was changed",
                        "<p>Your MALIEV account password was changed.</p><p>If you did not make this change, contact support@maliev.com immediately.</p>",
                        "support@maliev.com",
                        null,
                        ["mail-tracking@maliev.com"]),
                    cancellationToken);
                if (!notification.Sent)
                {
                    logger.LogWarning("Password-change security notification delivery failed.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning("Password-change security notification delivery failed unexpectedly.");
            }
        }

        Notification = "Your password was changed. Sign in again with the new password.";
        return RedirectToPage("/Account/Login", new { area = string.Empty, email });
    }

    private void BuildDisplayModel()
    {
        DisplayModel = new MemberChangePasswordDisplayModel(ProjectSafeErrors());
    }

    private IReadOnlyList<string> ProjectSafeErrors() => ModelState
        .Where(entry => entry.Value is not null)
        .SelectMany(entry => entry.Value!.Errors.Select(error => entry.Key switch
        {
            nameof(CurrentPassword) when string.IsNullOrWhiteSpace(CurrentPassword) => "Current password is required.",
            nameof(NewPassword) when string.IsNullOrWhiteSpace(NewPassword) => "New password is required.",
            nameof(NewPassword) => "New password must contain at least 8 characters.",
            nameof(ConfirmPassword) when string.IsNullOrWhiteSpace(ConfirmPassword) => "Please confirm the new password.",
            nameof(ConfirmPassword) => "Passwords do not match.",
            "" when error.Exception is null && error.ErrorMessage is
                "The current password is invalid or the new password was rejected."
                or "Account security is temporarily unavailable." => error.ErrorMessage,
            _ => "One or more password values are invalid.",
        }))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
