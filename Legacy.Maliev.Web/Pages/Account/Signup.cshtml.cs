using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class Signup(
    ICustomerProfileClient customerClient,
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    IAntiBotVerifier antiBotVerifier,
    IOptions<RecaptchaEnterpriseOptions> recaptchaOptions,
    IStringLocalizer<SignupContent> localizer,
    ILogger<Signup> logger) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "First name is required")]
    [StringLength(100, ErrorMessage = "First name is too long.")]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Last name is required")]
    [StringLength(100, ErrorMessage = "Last name is too long.")]
    public string LastName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(320, ErrorMessage = "Email address is too long.")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [StringLength(1024, MinimumLength = 8, ErrorMessage = "The password must be between 8 and 1024 characters.")]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please confirm your new password")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [BindProperty(Name = "g-recaptcha-response")]
    public string? RecaptchaToken { get; set; }

    public string RecaptchaSiteKey => recaptchaOptions.Value.SiteKey;

    [TempData]
    public string? Notification { get; set; }

    public SignupFormDisplayModel DisplayModel => new(
        FirstName,
        LastName,
        Email,
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

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? LocalRedirect("~/Account")
        : Page();

    public async Task<IActionResult> OnPostSignUpAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await antiBotVerifier.VerifyAsync(RecaptchaToken, "submit", cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Security verification failed. Please try again.");
            return Page();
        }

        var customerResult = await customerClient.CreateAsync(
            FirstName.Trim(),
            LastName.Trim(),
            Email.Trim(),
            cancellationToken);
        if (customerResult.Customer is not { } customer)
        {
            ModelState.AddModelError(string.Empty, "We could not create your account. Please try again.");
            return Page();
        }

        var identity = await authenticationClient.RegisterAsync(
            customer.Id,
            Email.Trim(),
            Password,
            cancellationToken);
        if (!identity.Succeeded)
        {
            if (!await customerClient.DeleteAsync(customer.Id, cancellationToken))
            {
                logger.LogCritical(
                    "Customer profile {CustomerId} requires manual cleanup after identity registration failed.",
                    customer.Id);
            }

            ModelState.AddModelError(string.Empty, "We could not create your account. Please try again.");
            return Page();
        }

        var challenge = await authenticationClient.RequestEmailConfirmationAsync(
            Email.Trim(),
            cancellationToken);
        var sent = challenge.Token is not null
            && await SendConfirmationAsync(challenge.Token, cancellationToken);
        if (!sent)
        {
            Notification = localizer["Account created, but confirmation delivery is unavailable. Please contact info@maliev.com."];
            return RedirectToPage(new { culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName });
        }

        return RedirectToPage(
            "/Account/Login",
            new
            {
                email = Email.Trim(),
                accountCreated = true,
                culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            });
    }

    private async Task<bool> SendConfirmationAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var callback = QueryHelpers.AddQueryString(
            $"{CanonicalUrlPolicy.CanonicalOrigin}/Account/EmailConfirmation",
            new Dictionary<string, string?>
            {
                ["email"] = Email.Trim(),
                ["token"] = token,
                ["culture"] = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            });

        var greeting = WebUtility.HtmlEncode(localizer["Hello {0},", $"{FirstName.Trim()} {LastName.Trim()}"]);
        var safeCallback = WebUtility.HtmlEncode(callback);
        var result = await notificationClient.SendAsync(
            NotificationChannel.NoReply,
            new EmailNotification(
                Email.Trim(),
                localizer["Confirm your MALIEV account"],
                $"<p>{greeting}</p><p>{WebUtility.HtmlEncode(localizer["Confirm your account using this single-use link:"])}</p><p><a href=\"{safeCallback}\">{WebUtility.HtmlEncode(localizer["Confirm account"])}</a></p>",
                null,
                null,
                ["mail-tracking@maliev.com"]),
            cancellationToken);
        return result.Sent;
    }
}
