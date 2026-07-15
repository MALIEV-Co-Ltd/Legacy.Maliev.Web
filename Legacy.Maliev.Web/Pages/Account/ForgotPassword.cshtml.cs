using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class ForgotPassword(
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    ILogger<ForgotPassword> logger) : PageModel
{
    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [TempData]
    public string? Notification { get; set; }

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
            await SendResetAsync(challenge.Token, cancellationToken);
        }

        Notification = "If an eligible account exists, a password reset link has been sent.";
        return RedirectToPage();
    }

    private async Task SendResetAsync(string token, CancellationToken cancellationToken)
    {
        var callback = Url.Page(
            "/Account/ResetPassword",
            null,
            new { email = Email.Trim(), token },
            Request.Scheme,
            Request.Host.Value);
        if (string.IsNullOrWhiteSpace(callback))
        {
            return;
        }

        var safeCallback = WebUtility.HtmlEncode(callback);
        var result = await notificationClient.SendAsync(
            NotificationChannel.NoReply,
            new EmailNotification(
                Email.Trim(),
                "Reset your MALIEV password",
                $"<p>Use this single-use link to reset your password:</p><p><a href=\"{safeCallback}\">Reset password</a></p><p>If you did not request this, you can ignore this email.</p>",
                null,
                null,
                ["mail-tracking@maliev.com"]),
            cancellationToken);
        if (!result.Sent)
        {
            logger.LogWarning("Password reset notification delivery failed.");
        }
    }
}
