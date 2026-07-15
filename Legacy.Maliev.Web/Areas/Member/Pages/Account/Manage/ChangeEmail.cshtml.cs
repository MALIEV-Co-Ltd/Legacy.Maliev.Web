using System.ComponentModel.DataAnnotations;
using System.Net;
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
public sealed class ChangeEmail(
    IAccountSessionManager sessionManager,
    ICustomerAuthenticationClient authenticationClient,
    ICustomerAccountClient customerClient,
    INotificationClient notificationClient,
    ILogger<ChangeEmail> logger) : PageModel
{
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024)]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty, Required, EmailAddress, StringLength(320)]
    public string NewEmail { get; set; } = string.Empty;

    [TempData]
    public string? Notification { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostChangeEmailAsync(CancellationToken cancellationToken)
    {
        var oldEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(oldEmail))
        {
            return Challenge();
        }

        if (string.Equals(oldEmail, NewEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(NewEmail), "Enter a different email address.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var accessToken = await sessionManager.GetAccessTokenAsync(HttpContext, cancellationToken);
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken) || customerId is null)
        {
            return Challenge();
        }

        var newEmail = NewEmail.Trim();
        var customerUpdate = await customerClient.UpdateEmailAsync(customerId.Value, newEmail, cancellationToken);
        if (!customerUpdate.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "The email address could not be changed.");
            return Page();
        }

        var identityUpdate = await authenticationClient.ChangeEmailAsync(
            accessToken,
            CurrentPassword,
            newEmail,
            cancellationToken);
        if (!identityUpdate.Succeeded || string.IsNullOrWhiteSpace(identityUpdate.Token))
        {
            var rolledBack = await customerClient.UpdateEmailAsync(customerId.Value, oldEmail, cancellationToken);
            if (!rolledBack.Succeeded)
            {
                logger.LogCritical(
                    "Customer profile {CustomerId} requires manual email reconciliation after identity change rejection.",
                    customerId.Value);
            }

            ModelState.AddModelError(
                string.Empty,
                identityUpdate.ServiceAvailable
                    ? "The current password is invalid or the email address is already in use."
                    : "Account security is temporarily unavailable.");
            return Page();
        }

        var callback = Url.Page(
            "/Account/ChangeEmailConfirmation",
            null,
            new { area = string.Empty, email = newEmail, token = identityUpdate.Token },
            Request.Scheme,
            Request.Host.Value);
        var sent = !string.IsNullOrWhiteSpace(callback)
            && (await notificationClient.SendAsync(
                NotificationChannel.NoReply,
                new EmailNotification(
                    newEmail,
                    "Confirm your new MALIEV email address",
                    $"<p>Confirm your new email address using this single-use link:</p><p><a href=\"{WebUtility.HtmlEncode(callback)}\">Confirm email</a></p>",
                    null,
                    null,
                    ["mail-tracking@maliev.com"]),
                cancellationToken)).Sent;

        await sessionManager.SignOutAsync(HttpContext, cancellationToken);
        Notification = sent
            ? "Check your new email address to confirm the change."
            : "Your email was changed, but confirmation delivery failed. Contact info@maliev.com.";
        return RedirectToPage("/Account/Login", new { area = string.Empty, email = newEmail });
    }
}
