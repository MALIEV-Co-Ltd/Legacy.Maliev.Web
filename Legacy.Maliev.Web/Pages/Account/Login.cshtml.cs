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

/// <summary>Handles customer sign-in, pending password setup, and bounded email-confirmation recovery.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="authenticationClient">The typed identity client handling customer credential and challenge operations.</param>
/// <param name="notificationClient">The typed notification client used for customer and staff messages.</param>
/// <param name="localizer">The localizer supplying this page's validation and outcome messages.</param>
/// <param name="logger">The logger instance for diagnostic output.</param>
[EnableRateLimiting("account")]
public sealed class Login(
    IAccountSessionManager sessionManager,
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    IStringLocalizer<LoginContent> localizer,
    ILogger<Login> logger) : PageModel
{
    /// <summary>The customer's email address used by this account or request workflow.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(320, ErrorMessage = "Email address is too long.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>The password submitted to the identity workflow; it is excluded from the form display projection.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [StringLength(1024)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Whether successful sign-in should persist the account session beyond the current browser session.</summary>
    [BindProperty]
    public bool RememberMe { get; set; }

    /// <summary>The requested post-authentication return path, which must pass local-URL validation before use.</summary>
    [BindProperty]
    public string? ReturnUrl { get; set; }

    /// <summary>The pending identity challenge used to authorize a confirmation-email recovery request.</summary>
    [BindProperty]
    [StringLength(256, MinimumLength = 32)]
    public string? EmailConfirmationRecoveryToken { get; set; }

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The sign-in form state and localized validation feedback, excluding the password.</summary>
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

    /// <summary>Initializes sign-in inputs while accepting only a local return path.</summary>
    /// <param name="email">The email address associated with this account request.</param>
    /// <param name="returnUrl">The requested local path to visit after completing the account workflow.</param>
    /// <param name="accountCreated">Whether to display the account-created confirmation prompt.</param>
    /// <returns>The sign-in page or the account redirect for an already authenticated user.</returns>
    public IActionResult OnGet(string? email, string? returnUrl, bool accountCreated = false)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("~/Account");
        }

        Email = email?.Trim() ?? string.Empty;
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        if (accountCreated)
        {
            Notification = localizer["Account created. Check your email and follow the link to confirm your address before signing in."];
        }
        return Page();
    }

    /// <summary>Attempts server-managed sign-in and projects any pending identity action without exposing session tokens.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A safe local redirect, an initial-password redirect, or the sign-in page with controlled feedback.</returns>
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
                    culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                });
        }

        if (status == AccountSignInStatus.EmailConfirmationRequired && action is not null)
        {
            EmailConfirmationRecoveryToken = action.Token;
            ModelState.AddModelError(string.Empty, "Please verify your email before signing in.");
            return Page();
        }

        ModelState.AddModelError(
            string.Empty,
            status == AccountSignInStatus.ServiceUnavailable
                ? "Sign in is temporarily unavailable. Please try again."
                : "The email or password is invalid, or the email has not been confirmed.");
        return Page();
    }

    /// <summary>Uses a pending recovery challenge to request another email-verification link.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The form with controlled feedback or a localized redirect carrying a non-enumerating outcome.</returns>
    public async Task<IActionResult> OnPostResendEmailConfirmationAsync(CancellationToken cancellationToken)
    {
        ModelState.Remove(nameof(Password));
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(EmailConfirmationRecoveryToken))
        {
            ModelState.AddModelError(string.Empty, "Login failed");
            return Page();
        }

        var challenge = await authenticationClient.RecoverEmailConfirmationAsync(
            Email.Trim(),
            EmailConfirmationRecoveryToken,
            cancellationToken);
        if (challenge.Token is not null)
        {
            var callback = QueryHelpers.AddQueryString(
                $"{CanonicalUrlPolicy.CanonicalOrigin}/Account/EmailConfirmation",
                new Dictionary<string, string?>
                {
                    ["email"] = Email.Trim(),
                    ["token"] = challenge.Token,
                    ["culture"] = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                });
            if (!await SendEmailConfirmationAsync(callback, cancellationToken))
            {
                EmailConfirmationRecoveryToken = null;
                ModelState.AddModelError(
                    string.Empty,
                    "We could not send the verification email. Please try again later.");
                return Page();
            }
        }

        Notification = localizer["If the account requires verification, a new verification link has been sent."];
        return RedirectToPage(new { email = Email.Trim(), returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null, culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName });
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
