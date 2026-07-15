using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Pages.Contact;

public sealed class Index(
    ICountryClient countryClient,
    IContactClient contactClient,
    IAntiBotVerifier antiBotVerifier,
    IOptions<RecaptchaEnterpriseOptions> recaptchaOptions,
    ILogger<Index> logger) : PageModel
{
    private const string RecaptchaAction = "submit";

    [BindProperty]
    [StringLength(50)]
    public string? Company { get; set; }

    public IReadOnlyList<Country> Countries { get; private set; } = [];

    [BindProperty]
    [Required(ErrorMessage = "Please select your country")]
    [StringLength(50)]
    public string Country { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress]
    [StringLength(50)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please enter your first name")]
    [StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please enter your last name")]
    [StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please enter your message")]
    [StringLength(5000)]
    public string Message { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(50)]
    public string? Phone { get; set; }

    [BindProperty(Name = "g-recaptcha-response")]
    public string? RecaptchaToken { get; set; }

    public string RecaptchaSiteKey => recaptchaOptions.Value.SiteKey;

    public bool CountryServiceAvailable { get; private set; } = true;

    [TempData]
    public string? Notification { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadCountriesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSubmitRequestAsync(CancellationToken cancellationToken)
    {
        await LoadCountriesAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await antiBotVerifier.VerifyAsync(RecaptchaToken, RecaptchaAction, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Security verification failed. Please try again.");
            return Page();
        }

        var result = await contactClient.SubmitAsync(
            new ContactSubmission(
                FirstName.Trim(),
                LastName.Trim(),
                NormalizeOptional(Company),
                Email.Trim(),
                NormalizeOptional(Phone),
                Country.Trim(),
                Message.Trim()),
            cancellationToken);
        if (result.ReferenceNumber is not int referenceNumber)
        {
            ModelState.AddModelError(
                string.Empty,
                result.Authorized && result.ServiceAvailable
                    ? "We could not save your message. Please try again."
                    : "The contact service is temporarily unavailable. Please email info@maliev.com.");
            return Page();
        }

        if (!LeadAnalyticsEventQueue.TryQueueContactMessage(TempData, referenceNumber, out var analyticsFailure))
        {
            logger.LogWarning(
                analyticsFailure,
                "Analytics queue failed after contact message {MessageId} was persisted.",
                referenceNumber);
        }

        Notification = $"Thank you for contacting us. Your reference number is #{referenceNumber}.";
        return RedirectToPage("Index");
    }

    private async Task LoadCountriesAsync(CancellationToken cancellationToken)
    {
        var result = await countryClient.GetCountriesAsync(cancellationToken);
        Countries = result.Value ?? [];
        CountryServiceAvailable = result.ServiceAvailable;
        if (!result.ServiceAvailable)
        {
            ModelState.AddModelError(string.Empty, "Could not retrieve countries from the server.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
