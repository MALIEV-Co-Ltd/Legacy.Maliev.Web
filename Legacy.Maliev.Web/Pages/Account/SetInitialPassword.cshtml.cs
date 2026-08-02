using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class SetInitialPassword(
    ICustomerAuthenticationClient authenticationClient,
    IAccountSessionManager sessionManager,
    INotificationClient notificationClient,
    IStringLocalizer<SetInitialPasswordContent> localizer,
    ILogger<SetInitialPassword> logger) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty, Required, StringLength(256, MinimumLength = 32)]
    public string Token { get; set; } = string.Empty;

    [BindProperty, Required, DataType(DataType.Password), StringLength(1024, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [BindProperty, Required, DataType(DataType.Password), Compare(nameof(Password)), StringLength(1024)]
    public string ConfirmPassword { get; set; } = string.Empty;

    [BindProperty]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public bool RememberMe { get; set; }

    public SetInitialPasswordFormDisplayModel DisplayModel => new(
        Email,
        Token,
        ReturnUrl,
        RememberMe,
        ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value!.Errors
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? localizer["The submitted value is invalid."]
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal));

    public IActionResult OnGet(string? email, string? token, string? returnUrl, bool rememberMe = false)
    {
        ApplySensitiveResponseHeaders();
        Email = email?.Trim() ?? string.Empty;
        Token = token ?? string.Empty;
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        RememberMe = rememberMe;
        return string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Token)
            ? RedirectToPage("/Account/Login")
            : Page();
    }

    public async Task<IActionResult> OnPostCompleteAsync(CancellationToken cancellationToken)
    {
        ApplySensitiveResponseHeaders();
        ReturnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await authenticationClient.CompleteInitialPasswordAsync(
            Email.Trim(),
            Token,
            Password,
            cancellationToken))
        {
            ModelState.AddModelError(string.Empty, localizer["This password setup link is invalid or has expired."]);
            return Page();
        }

        await SendPasswordChangedNotificationAsync(cancellationToken);

        var status = await sessionManager.SignInAsync(
            HttpContext,
            Email.Trim(),
            Password,
            RememberMe,
            cancellationToken);
        if (status != AccountSignInStatus.Succeeded)
        {
            return RedirectToPage(
                "/Account/Login",
                new { email = Email.Trim(), returnUrl = ReturnUrl });
        }

        return ReturnUrl is not null
            ? LocalRedirect(ReturnUrl)
            : LocalRedirect("~/Account");
    }

    private void ApplySensitiveResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private async Task SendPasswordChangedNotificationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var body = $"<p>{WebUtility.HtmlEncode(localizer["Your password was changed successfully."])}</p><p>{WebUtility.HtmlEncode(localizer["If you did not make this change, contact MALIEV support immediately."])} <a href=\"mailto:support@maliev.com\">support@maliev.com</a></p>";
            var result = await notificationClient.SendAsync(
                NotificationChannel.NoReply,
                new EmailNotification(
                    Email.Trim(),
                    localizer["Your MALIEV password was changed"],
                    body,
                    null,
                    null,
                    ["mail-tracking@maliev.com"]),
                cancellationToken);
            if (!result.Sent)
            {
                logger.LogWarning("Initial-password confirmation notification delivery failed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Initial-password confirmation notification delivery failed.");
        }
    }
}
