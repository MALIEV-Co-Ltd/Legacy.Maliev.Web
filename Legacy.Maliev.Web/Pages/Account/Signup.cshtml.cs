using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class Signup(
    ICustomerProfileClient customerClient,
    ICustomerAuthenticationClient authenticationClient,
    INotificationClient notificationClient,
    IAntiBotVerifier antiBotVerifier,
    IOptions<RecaptchaEnterpriseOptions> recaptchaOptions,
    ILogger<Signup> logger) : PageModel
{
    [BindProperty]
    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [StringLength(1024, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required]
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
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? "The submitted value is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal));

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? RedirectToPage("/Account/Index")
        : Page();

    public async Task<IActionResult> OnPostSignUpAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await antiBotVerifier.VerifyAsync(RecaptchaToken, "account_signup", cancellationToken))
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
        Notification = sent
            ? "Account created. Please check your email to confirm your address."
            : "Account created, but confirmation delivery is unavailable. Please contact info@maliev.com.";
        return RedirectToPage();
    }

    private async Task<bool> SendConfirmationAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var callback = Url.Page(
            "/Account/EmailConfirmation",
            null,
            new { email = Email.Trim(), token },
            Request.Scheme,
            Request.Host.Value);
        if (string.IsNullOrWhiteSpace(callback))
        {
            return false;
        }

        var name = WebUtility.HtmlEncode($"{FirstName.Trim()} {LastName.Trim()}");
        var safeCallback = WebUtility.HtmlEncode(callback);
        var result = await notificationClient.SendAsync(
            NotificationChannel.NoReply,
            new EmailNotification(
                Email.Trim(),
                "Confirm your MALIEV account",
                $"<p>Hello {name},</p><p>Confirm your account using this single-use link:</p><p><a href=\"{safeCallback}\">Confirm account</a></p>",
                null,
                null,
                ["mail-tracking@maliev.com"]),
            cancellationToken);
        return result.Sent;
    }
}
