using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Contact;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Pages.Contact;

/// <summary>Loads localized contact form data and persists verified contact requests before sending notifications.</summary>
/// <param name="countryClient">The typed country client supplying form country options.</param>
/// <param name="contactClient">The typed client persisting customer contact requests.</param>
/// <param name="notificationClient">The typed notification client used for customer and staff messages.</param>
/// <param name="antiBotVerifier">The verifier for the browser's anti-bot challenge.</param>
/// <param name="recaptchaOptions">The configured public browser challenge settings.</param>
/// <param name="googleMapsOptions">The configured contact-map presentation settings.</param>
/// <param name="trustedCustomerLoader">The loader resolving trusted customer contact details from the server-managed session.</param>
/// <param name="localizer">The localizer supplying this page's validation and outcome messages.</param>
/// <param name="logger">The logger instance for diagnostic output.</param>
public sealed class Index(
    ICountryClient countryClient,
    IContactClient contactClient,
    INotificationClient notificationClient,
    IAntiBotVerifier antiBotVerifier,
    IOptions<RecaptchaEnterpriseOptions> recaptchaOptions,
    IOptions<GoogleMapsOptions> googleMapsOptions,
    IContactTrustedCustomerLoader trustedCustomerLoader,
    IStringLocalizer<ContactContent> localizer,
    ILogger<Index> logger) : PageModel
{
    private const string RecaptchaAction = "submit";

    /// <summary>The optional company name associated with the submitted customer request.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? Company { get; set; }

    /// <summary>The country options returned by the country service for this form.</summary>
    public IReadOnlyList<Country> Countries { get; private set; } = [];

    /// <summary>The customer's country name used for the submitted contact or billing details.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please select your country")]
    [StringLength(50)]
    public string Country { get; set; } = string.Empty;

    /// <summary>The customer's email address used by this account or request workflow.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress]
    [StringLength(50)]
    public string Email { get; set; } = string.Empty;

    /// <summary>The customer's given name used in the account or request contact details.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please enter your first name")]
    [StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The customer's family name used in the account or request contact details.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please enter your last name")]
    [StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>The customer's request text submitted for staff review.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please enter your message")]
    [StringLength(5000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>The optional contact telephone number submitted with the request.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? Phone { get; set; }

    /// <summary>The browser's anti-bot challenge response, verified before the request is persisted.</summary>
    [BindProperty(Name = "g-recaptcha-response")]
    public string? RecaptchaToken { get; set; }

    /// <summary>The configured public site key used to obtain a browser anti-bot challenge.</summary>
    public string RecaptchaSiteKey => recaptchaOptions.Value.SiteKey;

    /// <summary>The supported English or Thai UI culture used for contact-page rendering and redirects.</summary>
    public string Culture => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "th" ? "th" : "en";

    /// <summary>The contact-map display configuration projected from the configured Google Maps options.</summary>
    public ContactMapDisplayModel Map => ContactMapDisplayModel.Create(googleMapsOptions);

    /// <summary>Whether the form is using a customer identity resolved from the server-managed session.</summary>
    public bool IsAuthenticatedCustomer { get; private set; }

    /// <summary>Whether the country lookup succeeded for the current form request.</summary>
    public bool CountryServiceAvailable { get; private set; } = true;

    /// <summary>The contact form values, country options, authentication state, and localized validation feedback.</summary>
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
        IsAuthenticatedCustomer,
        CountryServiceAvailable,
        Countries.Select(country => new ContactCountryOption(country.Name)).ToArray(),
        ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value!.Errors
                    .Select(error => LocalizeModelError(error.ErrorMessage))
                    .ToArray(),
                StringComparer.Ordinal));

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    private string LocalizeModelError(string? errorMessage)
    {
        var key = string.IsNullOrEmpty(errorMessage) ? "The submitted value is invalid." : errorMessage;
        var localized = localizer[key];
        return localized.ResourceNotFound && System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th"
            ? localizer["The submitted value is invalid."].Value
            : localized.Value;
    }

    /// <summary>Loads country options and any trusted customer contact details for the contact form.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The contact page with current service and validation feedback.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadCountriesAsync(cancellationToken);
        await LoadTrustedCustomerAsync(cancellationToken);
        return Page();
    }

    /// <summary>Verifies the form and anti-bot response, persists the contact request, then requests notifications.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The form with controlled errors or a localized redirect referencing the persisted contact request.</returns>
    public async Task<IActionResult> OnPostSubmitRequestAsync(CancellationToken cancellationToken)
    {
        await LoadCountriesAsync(cancellationToken);
        await LoadTrustedCustomerAsync(cancellationToken);
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
            ? localizer["Thank you for contacting us. Your reference number is #{0}.", referenceNumber].Value
            : localizer["Contact request #{0} was received, but confirmation delivery is unavailable. Do not submit it again; contact info@maliev.com with this reference.", referenceNumber].Value;
        return RedirectToPage("Index", new { culture = Culture });
    }

    private async Task<bool> SendNotificationsAsync(
        int referenceNumber,
        CancellationToken cancellationToken)
    {
        var customer = notificationClient.SendAsync(
            NotificationChannel.Info,
            new EmailNotification(
                Email.Trim(),
                localizer["Contact request #{0}", referenceNumber].Value,
                "<p>" + Encode(localizer["Thank you for contacting MALIEV. Your reference number is #{0}.", referenceNumber].Value)
                    + "</p><p>" + Encode(localizer["We will reply as soon as possible."].Value) + "</p>",
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

    private async Task LoadTrustedCustomerAsync(CancellationToken cancellationToken)
    {
        var result = await trustedCustomerLoader.LoadAsync(HttpContext, Countries, cancellationToken);
        IsAuthenticatedCustomer = result.IsAuthenticated;
        if (!result.IsAuthenticated)
        {
            return;
        }

        foreach (var field in new[] { nameof(Company), nameof(Country), nameof(Email), nameof(FirstName), nameof(LastName), nameof(Phone) })
        {
            ModelState.Remove(field);
        }

        if (result.Customer is null)
        {
            ModelState.AddModelError(string.Empty, "Could not retrieve your customer profile. Please update your profile or contact us directly.");
            return;
        }

        FirstName = result.Customer.FirstName;
        LastName = result.Customer.LastName;
        Email = result.Customer.Email;
        Phone = result.Customer.Phone;
        Company = result.Customer.Company;
        Country = result.Customer.Country;
        AddRequiredTrustedValue(nameof(FirstName), FirstName, "Please enter your first name");
        AddRequiredTrustedValue(nameof(LastName), LastName, "Please enter your last name");
        AddRequiredTrustedValue(nameof(Email), Email, "Email address is required");
        AddRequiredTrustedValue(nameof(Country), Country, "Please select your country");
    }

    private void AddRequiredTrustedValue(string field, string? value, string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ModelState.AddModelError(field, error);
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
