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
    [BindProperty, Required(ErrorMessage = "Please enter your email address"), EmailAddress(ErrorMessage = "Please enter a valid email address"), StringLength(320, ErrorMessage = "Email address is too long.")]
    public string Email { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "The password setup token is required."), StringLength(256, MinimumLength = 32, ErrorMessage = "The password setup token is invalid.")]
    public string Token { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "Please enter a new password"), DataType(DataType.Password), StringLength(1024, MinimumLength = 8, ErrorMessage = "The new password must be between 8 and 1024 characters.")]
    public string Password { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "Please confirm your new password"), DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Password confirmation does not match."), StringLength(1024, ErrorMessage = "Password confirmation is too long.")]
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
