using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Contact;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Pages.Contact;

public sealed class Index(
    ICountryClient countryClient,
    IContactClient contactClient,
    INotificationClient notificationClient,
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

    public ContactFormDisplayModel DisplayModel => new(
        FirstName,
        LastName,
        Email,
        Phone,
        Company,
        Country,
        Message,
        RecaptchaToken,
        RecaptchaSiteKey,
        CountryServiceAvailable,
        Countries.Select(country => new ContactCountryOption(country.Name)).ToArray(),
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

        var notificationsSent = await SendNotificationsAsync(referenceNumber, cancellationToken);
        Notification = notificationsSent
            ? $"Thank you for contacting us. Your reference number is #{referenceNumber}."
            : $"Contact request #{referenceNumber} was received, but confirmation delivery is unavailable. Do not submit it again; contact info@maliev.com with this reference.";
        return RedirectToPage("Index");
    }

    private async Task<bool> SendNotificationsAsync(
        int referenceNumber,
        CancellationToken cancellationToken)
    {
        var customer = notificationClient.SendAsync(
            NotificationChannel.Info,
            new EmailNotification(
                Email.Trim(),
                $"Contact request #{referenceNumber}",
                $"<p>Thank you for contacting MALIEV. Your reference number is <strong>#{referenceNumber}</strong>.</p><p>We will reply as soon as possible.</p>",
                null,
                null,
                null),
            cancellationToken);
        var internalNotification = notificationClient.SendAsync(
            NotificationChannel.Info,
            new EmailNotification(
                "info@maliev.com",
                $"Contact request #{referenceNumber}",
                BuildInternalMessage(referenceNumber),
                Email.Trim(),
                null,
                null),
            cancellationToken);
        var results = await Task.WhenAll(customer, internalNotification);
        return results.All(result => result.Sent);
    }

    private string BuildInternalMessage(int referenceNumber) =>
        $"""
        <h1>Contact request #{referenceNumber}</h1>
        <p><strong>Name:</strong> {Encode(FirstName)} {Encode(LastName)}</p>
        <p><strong>Email:</strong> {Encode(Email)}</p>
        <p><strong>Telephone:</strong> {Encode(Phone)}</p>
        <p><strong>Company:</strong> {Encode(Company)}</p>
        <p><strong>Country:</strong> {Encode(Country)}</p>
        <p><strong>Message:</strong><br />{Encode(Message).Replace("\n", "<br />", StringComparison.Ordinal)}</p>
        """;

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

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
