using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Quotation;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Pages.Quotation;

/// <summary>Loads and submits manual quotation requests with trusted customer details, validated finder attribution, and attachment follow-up.</summary>
/// <param name="countryClient">The typed country client supplying form country options.</param>
/// <param name="quotationClient">The typed client reading or submitting quotation operations for this page.</param>
/// <param name="quotationFileClient">The typed client uploading and linking quotation attachments.</param>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="customerAccountClient">The typed customer account client used to load trusted quotation contact details.</param>
/// <param name="notificationClient">The typed notification client used for customer and staff messages.</param>
/// <param name="antiBotVerifier">The verifier for the browser's anti-bot challenge.</param>
/// <param name="recaptchaOptions">The configured public browser challenge settings.</param>
/// <param name="localizer">The localizer supplying this page's validation and outcome messages.</param>
/// <param name="logger">The logger instance for diagnostic output.</param>
public sealed class Index(
    ICountryClient countryClient,
    IQuotationClient quotationClient,
    IQuotationFileClient quotationFileClient,
    IAccountSessionManager sessionManager,
    ICustomerAccountClient customerAccountClient,
    INotificationClient notificationClient,
    IAntiBotVerifier antiBotVerifier,
    IOptions<RecaptchaEnterpriseOptions> recaptchaOptions,
    IStringLocalizer<QuotationContent> localizer,
    ILogger<Index> logger) : PageModel
{
    private const long MaximumUploadBytes = 100L * 1024L * 1024L;
    private const string RecaptchaAction = "submit";

    /// <summary>Gets the service item selected by the public quotation GET route.</summary>
    public string? RequestedItem { get; private set; }

    /// <summary>The optional company name associated with the submitted customer request.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? Company { get; set; }

    /// <summary>The service finder's file-input category carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(80)]
    public string? FinderFiles { get; set; }

    /// <summary>The service finder's selected service carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(80)]
    public string? FinderService { get; set; }

    /// <summary>The service finder's selected material carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(80)]
    public string? FinderMaterial { get; set; }

    /// <summary>The service finder's quantity category carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(80)]
    public string? FinderQuantity { get; set; }

    /// <summary>The service finder's intended-use category carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(80)]
    public string? FinderEndUse { get; set; }

    /// <summary>The service finder's performance requirements carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(80)]
    public string? FinderPerformance { get; set; }

    /// <summary>The service finder's operating-environment category carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(80)]
    public string? FinderEnvironment { get; set; }

    /// <summary>The service finder's recommendation identifiers carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(400)]
    public string? FinderRecommendations { get; set; }

    /// <summary>The service finder's decision path carried to quotation attribution validation.</summary>
    [BindProperty]
    [StringLength(400)]
    public string? FinderPath { get; set; }

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
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(50)]
    public string Email { get; set; } = string.Empty;

    /// <summary>The uploaded request files validated before submission or attachment follow-up.</summary>
    [BindProperty]
    public IReadOnlyList<IFormFile> Files { get; set; } = [];

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
    [Required(ErrorMessage = "Please describe what you need")]
    [StringLength(10000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The optional contact telephone number submitted with the request.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? Phone { get; set; }

    /// <summary>The browser's anti-bot challenge response, verified before the request is persisted.</summary>
    [BindProperty(Name = "g-recaptcha-response")]
    public string? RecaptchaToken { get; set; }

    /// <summary>The configured public site key used to obtain a browser anti-bot challenge.</summary>
    public string RecaptchaSiteKey => recaptchaOptions.Value.SiteKey;

    /// <summary>The manufacturing-service context used for the manual quotation request and its controlled analytics.</summary>
    [BindProperty]
    public string ServiceContext { get; set; } = "custom_manufacturing";

    /// <summary>The per-form identifier used to correlate an idempotent quotation request and attachment follow-up.</summary>
    [BindProperty]
    public Guid SubmissionId { get; set; }

    /// <summary>The optional tax identifier associated with the customer's billing or company details.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? TaxNumber { get; set; }

    /// <summary>Whether the country lookup succeeded for the current form request.</summary>
    public bool CountryServiceAvailable { get; private set; } = true;

    /// <summary>Whether the form is using a customer identity resolved from the server-managed session.</summary>
    public bool IsAuthenticatedCustomer { get; private set; }

    /// <summary>Whether trusted customer profile details were available for the authenticated quotation request.</summary>
    public bool CustomerProfileAvailable { get; private set; } = true;

    /// <summary>The quotation form values, trusted-customer state, finder handoff, and localized validation feedback.</summary>
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
        IsAuthenticatedCustomer,
        CustomerProfileAvailable,
        Countries.Select(country => new QuotationCountryOption(country.Name)).ToArray(),
        ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value!.Errors
                    .Select(error => LocalizeModelError(error.ErrorMessage))
                    .ToArray(),
                StringComparer.Ordinal),
        FinderFiles,
        FinderService,
        FinderMaterial,
        FinderQuantity,
        FinderEndUse,
        FinderPerformance,
        FinderEnvironment,
        FinderRecommendations,
        FinderPath);

    private string LocalizeModelError(string? errorMessage)
    {
        var key = string.IsNullOrEmpty(errorMessage) ? "The submitted value is invalid." : errorMessage;
        var localized = localizer[key];
        return localized.ResourceNotFound && System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th"
            ? localizer["The submitted value is invalid."].Value
            : localized.Value;
    }

    /// <summary>Loads form dependencies and applies trusted customer, finder, and finishing-context prefills.</summary>
    /// <param name="culture">The requested supported UI culture.</param>
    /// <param name="item">The item context used to prefill the quotation request.</param>
    /// <param name="process">The manufacturing-process context used in quotation request prefill.</param>
    /// <param name="material">The material context used in quotation request prefill.</param>
    /// <param name="finder_files">The service finder's file-input category supplied in the incoming handoff.</param>
    /// <param name="finder_service">The service finder's selected service supplied in the incoming handoff.</param>
    /// <param name="finder_material">The service finder's selected material supplied in the incoming handoff.</param>
    /// <param name="finder_quantity">The service finder's quantity category supplied in the incoming handoff.</param>
    /// <param name="finder_end_use">The service finder's intended-use category supplied in the incoming handoff.</param>
    /// <param name="finder_performance">The service finder's performance requirements supplied in the incoming handoff.</param>
    /// <param name="finder_environment">The service finder's environment category supplied in the incoming handoff.</param>
    /// <param name="finder_recommendations">The service finder's recommendation identifiers supplied in the incoming handoff.</param>
    /// <param name="finder_path">The service finder's decision path supplied in the incoming handoff.</param>
    /// <param name="finish_hex">The optional hexadecimal finish colour used in quotation prefill.</param>
    /// <param name="finish_hlc">The optional HLC finish colour used in quotation prefill.</param>
    /// <param name="finish_lab">The optional Lab finish colour used in quotation prefill.</param>
    /// <param name="finish_pantone">The optional Pantone finish reference used in quotation prefill.</param>
    /// <param name="finish_sheen">The optional finish sheen used in quotation prefill.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The quotation form with a new submission identifier and current dependency feedback.</returns>
    public async Task<IActionResult> OnGetAsync(
        string? culture,
        string? item,
        string? process,
        string? material,
        string? finder_files,
        string? finder_service,
        string? finder_material,
        string? finder_quantity,
        string? finder_end_use,
        string? finder_performance,
        string? finder_environment,
        string? finder_recommendations,
        string? finder_path,
        string? finish_hex,
        string? finish_hlc,
        string? finish_lab,
        string? finish_pantone,
        string? finish_sheen,
        CancellationToken cancellationToken)
    {
        RequestedItem = item;
        await LoadCountriesAsync(cancellationToken);
        await ApplyTrustedCustomerAsync(cancellationToken);
        SubmissionId = Guid.NewGuid();
        FinderFiles = finder_files;
        FinderService = finder_service;
        FinderMaterial = finder_material;
        FinderQuantity = finder_quantity;
        FinderEndUse = finder_end_use;
        FinderPerformance = finder_performance;
        FinderEnvironment = finder_environment;
        FinderRecommendations = finder_recommendations;
        FinderPath = finder_path;
        var prefill = QuotationPrefill.Create(
            culture,
            item,
            process,
            material,
            finish_hex,
            finish_hlc,
            finish_lab,
            finish_pantone,
            finish_sheen);
        ServiceContext = prefill.ServiceContext;
        Message = prefill.Message;
        return Page();
    }

    /// <summary>Validates ownership, anti-bot and finder input, persists the quotation request, and performs attachment and notification follow-up.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The form with controlled errors or a localized redirect referencing the persisted quotation request.</returns>
    public async Task<IActionResult> OnPostSubmitRequestAsync(CancellationToken cancellationToken)
    {
        await LoadCountriesAsync(cancellationToken);
        await ApplyTrustedCustomerAsync(cancellationToken);
        if (IsAuthenticatedCustomer && !CustomerProfileAvailable)
        {
            return Page();
        }

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

        var finderAttribution = ServiceFinderAttribution.TryCreate(
            FinderFiles ?? string.Empty,
            FinderService ?? string.Empty,
            FinderMaterial ?? string.Empty,
            FinderQuantity ?? string.Empty,
            FinderEndUse ?? string.Empty,
            FinderRecommendations ?? string.Empty,
            FinderPath ?? string.Empty,
            FinderPerformance ?? string.Empty,
            FinderEnvironment ?? string.Empty,
            out var validatedFinderAttribution)
            ? validatedFinderAttribution
            : null;
        if (HasFinderInput() && finderAttribution is null)
        {
            logger.LogWarning("An invalid service-finder handoff was omitted from quotation metadata.");
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
                Message.Trim(),
                finderAttribution?.ToMetadataJson(),
                SubmissionId),
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

        bool hasFiles = Files.Any(file => file.Length > 0);
        bool analyticsQueued = false;
        Exception? analyticsFailure = null;
        if (result.TransactionId is { Length: > 0 }
            && result.JourneyId is Guid journeyId)
        {
            analyticsQueued = finderAttribution is null
                ? LeadAnalyticsEventQueue.TryQueueManualQuotation(
                    TempData,
                    result.TransactionId,
                    hasFiles,
                    ServiceContext,
                    journeyId.ToString(),
                    out analyticsFailure)
                : LeadAnalyticsEventQueue.TryQueueManualQuotationWithFinder(
                    TempData,
                    result.TransactionId,
                    hasFiles,
                    ServiceContext,
                    journeyId.ToString(),
                    finderAttribution,
                    out analyticsFailure);
        }
        else
        {
            analyticsFailure = new InvalidOperationException("Quotation service did not return its API-owned transaction and journey identifiers.");
        }

        if (!analyticsQueued)
        {
            logger.LogWarning(
                "Analytics queue failed after quotation request {QuotationRequestId} was persisted.",
                referenceNumber);
        }

        var fileResult = new QuotationFileResult(false, false, false, false);
        var notificationsSent = false;
        var followUpIsNotification = false;
        try
        {
            var uploads = Files
                .Where(file => file.Length > 0)
                .Select(file => new QuotationUpload(
                    Path.GetFileName(file.FileName),
                    string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                    file.Length,
                    file.OpenReadStream))
                .ToArray();

            fileResult = await quotationFileClient.UploadAndLinkAsync(
                referenceNumber,
                SubmissionId,
                uploads,
                cancellationToken);
            followUpIsNotification = true;
            notificationsSent = await SendNotificationsAsync(referenceNumber, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (!followUpIsNotification)
            {
                logger.LogError(
                    "Quotation follow-up failed after request {QuotationRequestId} was persisted. Stage={Stage} ExceptionType={ExceptionType}",
                    referenceNumber,
                    "attachment_upload",
                    exception.GetType().Name);
            }
        }

        Notification = fileResult.Completed && notificationsSent
            ? localizer["Thank you. Your quotation request reference is #{0}.", referenceNumber].Value
            : fileResult.Rejected
                ? localizer["Quotation request #{0} was received, but an attachment was rejected by malware scanning. Do not submit it again; contact info@maliev.com with this reference.", referenceNumber].Value
                : localizer["Quotation request #{0} was received, but an attachment or notification could not be completed. Do not submit it again; contact info@maliev.com with this reference.", referenceNumber].Value;
        return RedirectToPage("Index", new { culture = CurrentCulture });
    }

    private async Task<bool> SendNotificationsAsync(
        int referenceNumber,
        CancellationToken cancellationToken)
    {
        var customer = SendCustomerAsync();
        var internalNotification = SendInternalAsync();
        var results = await Task.WhenAll(customer, internalNotification);
        return results.All(result => result.Sent);

        async Task<NotificationResult> SendCustomerAsync() =>
            await SendAsync(
                "customer_notification",
                () => new EmailNotification(
                    Email.Trim(),
                    localizer["Quotation request #{0}", referenceNumber].Value,
                    "<p>" + Encode(localizer["Thank you for requesting a quotation from MALIEV. Your reference number is #{0}.", referenceNumber].Value)
                        + "</p><p>" + Encode(localizer["Our manufacturing team will review the request and reply directly."].Value) + "</p>",
                    null,
                    null,
                    null));

        async Task<NotificationResult> SendInternalAsync() =>
            await SendAsync(
                "internal_notification",
                () => new EmailNotification(
                    "manufacturing@maliev.com",
                    $"Quotation request #{referenceNumber}",
                    BuildInternalMessage(referenceNumber),
                    Email.Trim(),
                    null,
                    null));

        async Task<NotificationResult> SendAsync(string stage, Func<EmailNotification> createNotification)
        {
            try
            {
                return await notificationClient.SendAsync(NotificationChannel.Manufacturing, createNotification(), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    "Quotation follow-up failed after request {QuotationRequestId} was persisted. Stage={Stage} ExceptionType={ExceptionType}",
                    referenceNumber,
                    stage,
                    exception.GetType().Name);
                throw;
            }
        }
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

    private async Task ApplyTrustedCustomerAsync(CancellationToken cancellationToken)
    {
        var trustedCustomer = await QuotationTrustedCustomerLoader.LoadAsync(
            HttpContext,
            sessionManager,
            customerAccountClient,
            Countries,
            cancellationToken);
        IsAuthenticatedCustomer = trustedCustomer.IsAuthenticated;
        CustomerProfileAvailable = trustedCustomer.ProfileAvailable;
        if (!trustedCustomer.IsAuthenticated)
        {
            return;
        }

        ClearCustomerModelState();
        FirstName = trustedCustomer.FirstName;
        LastName = trustedCustomer.LastName;
        Email = trustedCustomer.Email;
        Phone = trustedCustomer.Phone;
        Company = trustedCustomer.Company;
        TaxNumber = trustedCustomer.TaxNumber;
        Country = trustedCustomer.Country;
    }

    private void ClearCustomerModelState()
    {
        foreach (var field in new[]
                 {
                     nameof(FirstName), nameof(LastName), nameof(Email), nameof(Phone),
                     nameof(Company), nameof(TaxNumber), nameof(Country)
                 })
        {
            ModelState.Remove(field);
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool HasFinderInput() =>
        !string.IsNullOrWhiteSpace(FinderFiles)
        || !string.IsNullOrWhiteSpace(FinderService)
        || !string.IsNullOrWhiteSpace(FinderMaterial)
        || !string.IsNullOrWhiteSpace(FinderQuantity)
        || !string.IsNullOrWhiteSpace(FinderEndUse)
        || !string.IsNullOrWhiteSpace(FinderPerformance)
        || !string.IsNullOrWhiteSpace(FinderEnvironment)
        || !string.IsNullOrWhiteSpace(FinderRecommendations)
        || !string.IsNullOrWhiteSpace(FinderPath);

    private static string CurrentCulture =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "en" ? "en" : "th";

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
