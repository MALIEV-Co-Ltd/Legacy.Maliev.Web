using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class Login(
    IAccountSessionManager sessionManager,
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    IStringLocalizer<LoginContent> localizer,
    ILogger<Login> logger) : PageModel
{
    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [StringLength(1024)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    [StringLength(256, MinimumLength = 32)]
    public string? EmailConfirmationRecoveryToken { get; set; }

    [TempData]
    public string? Notification { get; set; }

    public LoginFormDisplayModel DisplayModel => new(
        Email,
        RememberMe,
        ReturnUrl,
        Notification,
        EmailConfirmationRecoveryToken,
        ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value!.Errors
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? "The submitted value is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal));

    public IActionResult OnGet(string? email, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("~/Account");
        }

        Email = email?.Trim() ?? string.Empty;
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return Page();
    }

    public async Task<IActionResult> OnPostLoginAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var status = await sessionManager.SignInAsync(
            HttpContext,
            Email,
            Password,
            RememberMe,
            cancellationToken);
        if (status == AccountSignInStatus.Succeeded)
        {
            return Url.IsLocalUrl(ReturnUrl)
                ? LocalRedirect(ReturnUrl!)
                : LocalRedirect("~/Account");
        }

        var action = sessionManager.GetPendingAction(HttpContext);
        if (status == AccountSignInStatus.InitialPasswordRequired && action is not null)
        {
            return RedirectToPage(
                "/Account/SetInitialPassword",
                new
                {
                    email = Email.Trim(),
                    token = action.Token,
                    returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null,
                    rememberMe = RememberMe,
                });
        }

        if (status == AccountSignInStatus.EmailConfirmationRequired && action is not null)
        {
            EmailConfirmationRecoveryToken = action.Token;
            ModelState.AddModelError(string.Empty, localizer["Please verify your email before signing in."]);
            return Page();
        }

        ModelState.AddModelError(
            string.Empty,
            status == AccountSignInStatus.ServiceUnavailable
                ? "Sign in is temporarily unavailable. Please try again."
                : "The email or password is invalid, or the email has not been confirmed.");
        return Page();
    }

    public async Task<IActionResult> OnPostResendEmailConfirmationAsync(CancellationToken cancellationToken)
    {
        ModelState.Remove(nameof(Password));
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(EmailConfirmationRecoveryToken))
        {
            ModelState.AddModelError(string.Empty, localizer["Login failed"]);
            return Page();
        }

        var challenge = await authenticationClient.RecoverEmailConfirmationAsync(
            Email.Trim(),
            EmailConfirmationRecoveryToken,
            cancellationToken);
        if (challenge.Token is not null)
        {
            var callbackPath = QueryHelpers.AddQueryString(
                "/Account/EmailConfirmation",
                new Dictionary<string, string?>
                {
                    ["email"] = Email.Trim(),
                    ["token"] = challenge.Token,
                });
            var callback = UriHelper.BuildAbsolute(Request.Scheme, Request.Host, callbackPath);
            if (!await SendEmailConfirmationAsync(callback, cancellationToken))
            {
                EmailConfirmationRecoveryToken = null;
                ModelState.AddModelError(
                    string.Empty,
                    localizer["We could not send the verification email. Please try again later."]);
                return Page();
            }
        }

        Notification = localizer["If the account requires verification, a new verification link has been sent."];
        return RedirectToPage(new { email = Email.Trim(), returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null });
    }

    private async Task<bool> SendEmailConfirmationAsync(string callback, CancellationToken cancellationToken)
    {
        try
        {
            var safeCallback = WebUtility.HtmlEncode(callback);
            var result = await notificationClient.SendAsync(
                NotificationChannel.NoReply,
                new EmailNotification(
                    Email.Trim(),
                    localizer["Email Confirmation"],
                    $"<p>{WebUtility.HtmlEncode(localizer["Please confirm your MALIEV customer email address by selecting the link below."])}</p><p><a href=\"{safeCallback}\">{WebUtility.HtmlEncode(localizer["Confirm email"])}</a></p><p>{WebUtility.HtmlEncode(localizer["If you did not request this email, you can ignore it."])}</p>",
                    null,
                    null,
                    ["mail-tracking@maliev.com"]),
                cancellationToken);
            if (!result.Sent)
            {
                logger.LogWarning("Email confirmation notification delivery failed.");
            }

            return result.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Email confirmation notification delivery failed.");
            return false;
        }
    }
}
