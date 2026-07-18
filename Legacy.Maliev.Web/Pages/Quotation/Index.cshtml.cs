using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Quotation;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Pages.Quotation;

public sealed class Index(
    ICountryClient countryClient,
    IQuotationClient quotationClient,
    IQuotationFileClient quotationFileClient,
    INotificationClient notificationClient,
    IAntiBotVerifier antiBotVerifier,
    IOptions<RecaptchaEnterpriseOptions> recaptchaOptions,
    ILogger<Index> logger) : PageModel
{
    private const long MaximumUploadBytes = 100L * 1024L * 1024L;
    private const int MaximumFileCount = 10;
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
    public IReadOnlyList<IFormFile> Files { get; set; } = [];

    [BindProperty]
    [Required(ErrorMessage = "Please enter your first name")]
    [StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please enter your last name")]
    [StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Please describe what you need")]
    [StringLength(10000)]
    public string Message { get; set; } = string.Empty;

    [TempData]
    public string? Notification { get; set; }

    [BindProperty]
    [StringLength(50)]
    public string? Phone { get; set; }

    [BindProperty(Name = "g-recaptcha-response")]
    public string? RecaptchaToken { get; set; }

    public string RecaptchaSiteKey => recaptchaOptions.Value.SiteKey;

    [BindProperty]
    public string ServiceContext { get; set; } = "custom_manufacturing";

    [BindProperty]
    public Guid SubmissionId { get; set; }

    [BindProperty]
    [StringLength(50)]
    public string? TaxNumber { get; set; }

    public bool CountryServiceAvailable { get; private set; } = true;

    public QuotationFormDisplayModel DisplayModel => new(
        SubmissionId,
        ServiceContext,
        FirstName,
        LastName,
        Email,
        Phone,
        Company,
        TaxNumber,
        Country,
        Message,
        RecaptchaToken,
        RecaptchaSiteKey,
        CountryServiceAvailable,
        Countries.Select(country => new QuotationCountryOption(country.Name)).ToArray(),
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

    public async Task<IActionResult> OnGetAsync(
        string? culture,
        string? item,
        string? process,
        string? material,
        CancellationToken cancellationToken)
    {
        await LoadCountriesAsync(cancellationToken);
        SubmissionId = Guid.NewGuid();
        var prefill = QuotationPrefill.Create(culture, item, process, material);
        ServiceContext = prefill.ServiceContext;
        Message = prefill.Message;
        return Page();
    }

    public async Task<IActionResult> OnPostSubmitRequestAsync(CancellationToken cancellationToken)
    {
        await LoadCountriesAsync(cancellationToken);
        ValidateSubmission();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await antiBotVerifier.VerifyAsync(RecaptchaToken, RecaptchaAction, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Security verification failed. Please try again.");
            return Page();
        }

        var result = await quotationClient.CreateRequestAsync(
            new QuotationRequestSubmission(
                FirstName.Trim(),
                LastName.Trim(),
                Email.Trim(),
                NormalizeOptional(Phone),
                Country.Trim(),
                NormalizeOptional(Company),
                NormalizeOptional(TaxNumber),
                Message.Trim()),
            $"legacy-web-quotation-{SubmissionId:N}",
            cancellationToken);
        if (result.ReferenceNumber is not int referenceNumber)
        {
            ModelState.AddModelError(
                string.Empty,
                result.Authorized && result.ServiceAvailable
                    ? "We could not save your quotation request. Please try again."
                    : "The quotation service is temporarily unavailable. Please email info@maliev.com.");
            return Page();
        }

        var uploads = Files
            .Where(file => file.Length > 0)
            .Select(file => new QuotationUpload(
                Path.GetFileName(file.FileName),
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length,
                file.OpenReadStream))
            .ToArray();

        var fileResult = await quotationFileClient.UploadAndLinkAsync(
            referenceNumber,
            SubmissionId,
            uploads,
            cancellationToken);
        if (!LeadAnalyticsEventQueue.TryQueueManualQuotation(
                TempData,
                referenceNumber,
                QuotationPrefill.NormalizeServiceContext(ServiceContext),
                uploads.Length > 0,
                uploads.Length > 0 && fileResult.Completed,
                out var analyticsFailure))
        {
            logger.LogWarning(
                analyticsFailure,
                "Analytics queue failed after quotation request {QuotationRequestId} was persisted.",
                referenceNumber);
        }

        var notificationsSent = await SendNotificationsAsync(referenceNumber, cancellationToken);
        Notification = fileResult.Completed && notificationsSent
            ? $"Thank you. Your quotation request reference is #{referenceNumber}."
            : fileResult.Rejected
                ? $"Quotation request #{referenceNumber} was received, but an attachment was rejected by malware scanning. Do not submit it again; contact info@maliev.com with this reference."
                : $"Quotation request #{referenceNumber} was received, but an attachment or notification could not be completed. Do not submit it again; contact info@maliev.com with this reference.";
        return RedirectToPage("Index");
    }

    private async Task<bool> SendNotificationsAsync(
        int referenceNumber,
        CancellationToken cancellationToken)
    {
        var customer = notificationClient.SendAsync(
            NotificationChannel.Manufacturing,
            new EmailNotification(
                Email.Trim(),
                $"Quotation request #{referenceNumber}",
                $"<p>Thank you for requesting a quotation from MALIEV. Your reference number is <strong>#{referenceNumber}</strong>.</p><p>Our manufacturing team will review the request and reply directly.</p>",
                null,
                null,
                null),
            cancellationToken);
        var internalNotification = notificationClient.SendAsync(
            NotificationChannel.Manufacturing,
            new EmailNotification(
                "manufacturing@maliev.com",
                $"Quotation request #{referenceNumber}",
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
        <h1>Quotation request #{referenceNumber}</h1>
        <p><strong>Name:</strong> {Encode(FirstName)} {Encode(LastName)}</p>
        <p><strong>Email:</strong> {Encode(Email)}</p>
        <p><strong>Telephone:</strong> {Encode(Phone)}</p>
        <p><strong>Company:</strong> {Encode(Company)}</p>
        <p><strong>Tax ID:</strong> {Encode(TaxNumber)}</p>
        <p><strong>Country:</strong> {Encode(Country)}</p>
        <p><strong>Message:</strong><br />{Encode(Message).Replace("\n", "<br />", StringComparison.Ordinal)}</p>
        """;

    private void ValidateSubmission()
    {
        if (SubmissionId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(SubmissionId), "This quotation form has expired. Please reload it.");
        }

        if (Files.Count > MaximumFileCount)
        {
            ModelState.AddModelError(nameof(Files), $"Upload no more than {MaximumFileCount} files.");
        }

        if (Files.Any(file => file.Length <= 0))
        {
            ModelState.AddModelError(nameof(Files), "Empty files cannot be uploaded.");
        }

        if (Files.Sum(file => file.Length) > MaximumUploadBytes)
        {
            ModelState.AddModelError(nameof(Files), "Combined file size cannot exceed 100 MB.");
        }
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

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
