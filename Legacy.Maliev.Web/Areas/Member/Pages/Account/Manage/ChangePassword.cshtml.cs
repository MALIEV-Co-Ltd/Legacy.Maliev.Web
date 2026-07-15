using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

[Authorize]
[EnableRateLimiting("account")]
public sealed class ChangePassword(
    IAccountSessionManager sessionManager,
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    ILogger<ChangePassword> logger) : PageModel
{
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024)]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty, Required, DataType(DataType.Password), StringLength(1024, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty, Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [TempData]
    public string? Notification { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var accessToken = await sessionManager.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Challenge();
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
            return Page();
        }

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(email))
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

        await sessionManager.SignOutAsync(HttpContext, cancellationToken);
        Notification = "Your password was changed. Sign in again with the new password.";
        return RedirectToPage("/Account/Login", new { area = string.Empty, email });
    }
}
