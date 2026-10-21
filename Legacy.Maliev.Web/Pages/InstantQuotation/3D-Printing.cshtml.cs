using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Pages.InstantQuotation;

/// <summary>Provides the retained additive quotation page, compatibility calculations, and session-owned customer submission flow.</summary>
public sealed class ThreeDimensionalPrinting : PageModel
{
    private readonly ILogger<ThreeDimensionalPrinting>? logger;
    private readonly IInstantQuotationSubmissionService? submissionService;

    /// <summary>Initializes the retained quotation page model without submission dependencies.</summary>
    public ThreeDimensionalPrinting()
    {
    }

    /// <summary>Initializes the quotation page with its owned submission service and diagnostic logger.</summary>
    /// <param name="submissionService">The service coordinating persistence and follow-up for the owned submission.</param>
    /// <param name="logger">The logger instance for diagnostic output.</param>
    [ActivatorUtilitiesConstructor]
    public ThreeDimensionalPrinting(
        IInstantQuotationSubmissionService submissionService,
        ILogger<ThreeDimensionalPrinting> logger)
    {
        this.submissionService = submissionService;
        this.logger = logger;
    }

    /// <summary>The TempData key carrying a controlled quotation-submission problem category across a redirect.</summary>
    public const string ProblemCategoryTempDataKey = "InstantQuotationProblemCategory";
    /// <summary>The TempData key carrying the persisted quotation-request reference across a redirect.</summary>
    public const string RequestReferenceTempDataKey = "InstantQuotationRequestReference";
    /// <summary>The status marker for a completed quotation submission with a persisted reference.</summary>
    public const string SubmissionStatusCompleted = "completed";
    /// <summary>The status marker for a persisted quotation submission with incomplete follow-up steps.</summary>
    public const string SubmissionStatusPartial = "partial";
    /// <summary>The status marker for a rejected quotation submission.</summary>
    public const string SubmissionStatusRejected = "rejected";
    /// <summary>The TempData key carrying the quotation-submission outcome across a redirect.</summary>
    public const string SubmissionStatusTempDataKey = "InstantQuotationSubmissionStatus";
    /// <summary>The TempData key carrying allowlisted invalid field names for redirected form feedback.</summary>
    public const string ValidationFieldsTempDataKey = "InstantQuotationValidationFields";
    /// <summary>The TempData key identifying overlength building fields for redirected form feedback.</summary>
    public const string OverlengthBuildingFieldsTempDataKey = "InstantQuotationOverlengthBuildingFields";

    /// <summary>The optional company name associated with the submitted customer request.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? Company { get; set; }

    /// <summary>The optional building name for the customer's billing address, separate from street and locality fields.</summary>
    [BindProperty]
    [StringLength(256)]
    public string? BillingBuilding { get; set; }

    /// <summary>The required first street-address line for quotation billing.</summary>
    [BindProperty]
    [Required]
    [StringLength(256)]
    public string BillingStreet1 { get; set; } = string.Empty;

    /// <summary>The optional second street-address line for quotation billing.</summary>
    [BindProperty]
    [StringLength(256)]
    public string? BillingStreet2 { get; set; }

    /// <summary>The city or district used for customer billing.</summary>
    [BindProperty]
    [Required]
    [StringLength(256)]
    public string BillingCity { get; set; } = string.Empty;

    /// <summary>The state or province used for quotation billing.</summary>
    [BindProperty]
    [Required]
    [StringLength(256)]
    public string BillingProvince { get; set; } = string.Empty;

    /// <summary>The postal code used for customer billing.</summary>
    [BindProperty]
    [Required]
    [StringLength(20)]
    public string BillingPostalCode { get; set; } = string.Empty;

    /// <summary>The customer's country name used for the submitted contact or billing details.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please select your country.")]
    [StringLength(50)]
    public string Country { get; set; } = string.Empty;

    /// <summary>The optional customer instructions accompanying the manufacturing request.</summary>
    [BindProperty]
    [StringLength(512)]
    public string? Description { get; set; }

    /// <summary>The calculator catalogue and display options for the retained quotation page.</summary>
    public InstantQuotationDisplayModel DisplayModel => InstantQuotationCalculator.CreateDisplayModel();

    /// <summary>The customer's email address used by this account or request workflow.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please enter your email address.")]
    [EmailAddress]
    [StringLength(50)]
    public string Email { get; set; } = string.Empty;

    /// <summary>The customer's given name used in the account or request contact details.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please enter your first name.")]
    [StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The customer's family name used in the account or request contact details.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please enter your last name.")]
    [StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>The mobile contact number supplied with the customer's details.</summary>
    [BindProperty]
    [Required(ErrorMessage = "Please enter your mobile number.")]
    [StringLength(50)]
    public string Mobile { get; set; } = string.Empty;

    /// <summary>The optional tax identifier associated with the customer's billing or company details.</summary>
    [BindProperty]
    [RegularExpression(@"^[0-9]{13}$", ErrorMessage = "Thai tax ID must contain exactly 13 digits.")]
    [StringLength(50)]
    public string? TaxNumber { get; set; }

    /// <summary>The selected head-office or branch designation used when formatting the tax identifier.</summary>
    [BindProperty]
    public string TaxBranch { get; set; } = "head-office";

    /// <summary>The five-digit branch code required when a tax identifier belongs to a branch.</summary>
    [BindProperty]
    [RegularExpression(@"^[0-9]{5}$")]
    [StringLength(5)]
    public string? TaxBranchCode { get; set; }

    /// <summary>Whether the quotation's shipping destination reuses its billing address.</summary>
    [BindProperty]
    public bool ShipToBillingAddress { get; set; } = true;

    /// <summary>The optional building name for the shipping destination, separate from street and locality fields.</summary>
    [BindProperty]
    [StringLength(256)]
    public string? ShippingBuilding { get; set; }

    /// <summary>The first shipping street-address line required when billing and shipping destinations differ.</summary>
    [BindProperty]
    [StringLength(256)]
    public string? ShippingStreet1 { get; set; }

    /// <summary>The optional second street-address line of the separate shipping destination.</summary>
    [BindProperty]
    [StringLength(256)]
    public string? ShippingStreet2 { get; set; }

    /// <summary>The city or district of the shipping destination.</summary>
    [BindProperty]
    [StringLength(256)]
    public string? ShippingCity { get; set; }

    /// <summary>The state or province of the separate shipping destination.</summary>
    [BindProperty]
    [StringLength(256)]
    public string? ShippingProvince { get; set; }

    /// <summary>The postal code of the shipping destination.</summary>
    [BindProperty]
    [StringLength(20)]
    public string? ShippingPostalCode { get; set; }

    /// <summary>The country name required for a separate shipping destination.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? ShippingCountry { get; set; }

    /// <summary>The optional landline contact number supplied with the customer's details.</summary>
    [BindProperty]
    [StringLength(50)]
    public string? Telephone { get; set; }

    /// <summary>Displays the retained additive quotation page.</summary>
    public void OnGet()
    {
    }

    /// <summary>Calculates the retained additive item estimate without performing asynchronous FX conversion.</summary>
    /// <param name="material">The requested material name used to select a catalogue or estimate option.</param>
    /// <param name="dimensionZ">The model height used by the additive estimate calculation.</param>
    /// <param name="volume">The model volume used by the additive estimate calculation.</param>
    /// <param name="footprint">The model footprint used by the additive estimate calculation.</param>
    /// <param name="areaProfile">The serialized layer-area profile supplied to the estimate calculator.</param>
    /// <param name="perimeterProfile">The serialized layer-perimeter profile supplied to the estimate calculator.</param>
    /// <param name="currency">The requested display currency; authoritative conversion is applied by the FX-aware handlers.</param>
    /// <param name="quantity">The requested item quantity used by the additive estimate calculation.</param>
    /// <param name="unsupportedAreaProfile">The optional serialized unsupported-area profile used by the estimate calculator.</param>
    /// <returns>The calculator's estimate payload as JSON.</returns>
    [NonHandler]
    public JsonResult OnGetGetEstimate(
        string? material,
        double dimensionZ,
        double volume,
        double footprint,
        string? areaProfile,
        string? perimeterProfile,
        string? currency,
        int quantity,
        string? unsupportedAreaProfile = null) => new(InstantQuotationCalculator.GetEstimate(
            material,
            dimensionZ,
            volume,
            footprint,
            areaProfile,
            perimeterProfile,
            currency,
            quantity,
            unsupportedAreaProfile));

    /// <summary>Calculates the retained order total without performing asynchronous FX conversion.</summary>
    /// <param name="processes">The serialized process selections used to calculate the order total.</param>
    /// <param name="subtotals">The serialized item subtotals used to calculate the order total.</param>
    /// <param name="totalWeightGrams">The aggregate order weight in grams used for shipping calculation.</param>
    /// <param name="totalBoundingCm3">The aggregate bounding volume in cubic centimetres used for shipping calculation.</param>
    /// <param name="currency">The requested display currency; authoritative conversion is applied by the FX-aware handlers.</param>
    /// <param name="destinationCountry">The optional shipping destination used for the order-total calculation.</param>
    /// <returns>The calculator's order-total payload as JSON.</returns>
    [NonHandler]
    public JsonResult OnGetGetOrderTotal(
        string? processes,
        string? subtotals,
        double totalWeightGrams,
        double totalBoundingCm3,
        string? currency,
        string? destinationCountry = null) => new(InstantQuotationCalculator.GetOrderTotal(
            processes,
            subtotals,
            totalWeightGrams,
            totalBoundingCm3,
            currency,
            destinationCountry));

    /// <summary>Calculates an additive estimate and applies the request-scoped authoritative FX conversion.</summary>
    /// <param name="material">The requested material name used to select a catalogue or estimate option.</param>
    /// <param name="dimensionZ">The model height used by the additive estimate calculation.</param>
    /// <param name="volume">The model volume used by the additive estimate calculation.</param>
    /// <param name="footprint">The model footprint used by the additive estimate calculation.</param>
    /// <param name="areaProfile">The serialized layer-area profile supplied to the estimate calculator.</param>
    /// <param name="perimeterProfile">The serialized layer-perimeter profile supplied to the estimate calculator.</param>
    /// <param name="currency">The requested display currency; authoritative conversion is applied by the FX-aware handlers.</param>
    /// <param name="quantity">The requested item quantity used by the additive estimate calculation.</param>
    /// <param name="unsupportedAreaProfile">The optional serialized unsupported-area profile used by the estimate calculator.</param>
    /// <returns>The converted estimate payload or controlled FX failure with its HTTP status.</returns>
    public async Task<JsonResult> OnGetGetEstimateAsync(
        string? material,
        double dimensionZ,
        double volume,
        double footprint,
        string? areaProfile,
        string? perimeterProfile,
        string? currency,
        int quantity,
        string? unsupportedAreaProfile = null)
    {
        var payload = OnGetGetEstimate(material, dimensionZ, volume, footprint, areaProfile, perimeterProfile, currency, quantity, unsupportedAreaProfile).Value!;
        var result = await HttpContext.RequestServices.GetRequiredService<InstantQuotationFxHandler>().ConvertAsync(HttpContext, payload, currency);
        return new JsonResult(result.Payload) { StatusCode = result.StatusCode };
    }

    /// <summary>Calculates the order total and applies the request-scoped authoritative FX conversion.</summary>
    /// <param name="processes">The serialized process selections used to calculate the order total.</param>
    /// <param name="subtotals">The serialized item subtotals used to calculate the order total.</param>
    /// <param name="totalWeightGrams">The aggregate order weight in grams used for shipping calculation.</param>
    /// <param name="totalBoundingCm3">The aggregate bounding volume in cubic centimetres used for shipping calculation.</param>
    /// <param name="currency">The requested display currency; authoritative conversion is applied by the FX-aware handlers.</param>
    /// <param name="destinationCountry">The optional shipping destination used for the order-total calculation.</param>
    /// <returns>The converted order-total payload or controlled FX failure with its HTTP status.</returns>
    public async Task<JsonResult> OnGetGetOrderTotalAsync(
        string? processes,
        string? subtotals,
        double totalWeightGrams,
        double totalBoundingCm3,
        string? currency,
        string? destinationCountry = null)
    {
        var payload = OnGetGetOrderTotal(processes, subtotals, totalWeightGrams, totalBoundingCm3, currency, destinationCountry).Value!;
        var result = await HttpContext.RequestServices.GetRequiredService<InstantQuotationFxHandler>().ConvertAsync(HttpContext, payload, currency);
        return new JsonResult(result.Payload) { StatusCode = result.StatusCode };
    }

    /// <summary>Validates trusted customer details and session ownership before submitting the quotation with controlled retry and terminal outcomes.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A controlled JSON result or local redirect carrying the submission outcome.</returns>
    public async Task<IActionResult> OnPostSubmitRequestAsync(CancellationToken cancellationToken)
    {
        var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        InstantQuotationAuthenticatedPreparation? prepared = null;
        if (User.Identity?.IsAuthenticated is true)
        {
            var trustedSession = User.FindFirstValue(InstantQuotationSessionIdentityClaim.Type);
            var trustedOwner = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!IsValidSessionIdentity(trustedSession) || string.IsNullOrWhiteSpace(trustedOwner))
            {
                StoreRejected(InstantQuotationProblemCategory.Authorization);
                return TerminalOrRedirect(wantsJson);
            }
            var profilePreparation = HttpContext.RequestServices?.GetService<IInstantQuotationAuthenticatedPreparationService>();
            if (profilePreparation is null)
            {
                StoreRejected(InstantQuotationProblemCategory.DependencyUnavailable);
                return TerminalOrRedirect(wantsJson);
            }
            Company = NormalizeOptionalCustomerText(Company);
            TaxNumber = NormalizeOptionalCustomerText(TaxNumber);
            prepared = await profilePreparation.PrepareAsync(trustedSession!, trustedOwner, PostedProfile(), Description, cancellationToken);
            if (prepared.Customer is null)
            {
                if (prepared.TerminalResult is not null) StoreResult(prepared.TerminalResult);
                else StoreRejected(prepared.ProblemCategory);
                if (prepared.SafeToRetry && wantsJson)
                {
                    var localizer = HttpContext.RequestServices?.GetService<Microsoft.Extensions.Localization.IStringLocalizer<ThreeDimensionalPrintingEstimateContent>>();
                    var message = prepared.ProblemCategory == InstantQuotationProblemCategory.Authorization
                        ? "Please sign in again to securely load your current customer details."
                        : "We could not securely load your customer profile. Please try again.";
                    ModelState.AddModelError(string.Empty, localizer?[message] ?? message);
                    return RetryJson();
                }
                return TerminalOrRedirect(wantsJson);
            }
            ApplyProfile(prepared.Details!);
            Description = prepared.Customer.Description;
            ModelState.Remove(nameof(Description));
            ValidateProfileProperty(nameof(Description));
        }
        if (prepared is null) NormalizeOptionalCustomerFields();
        ValidateConditionalCustomerFields();
        ValidateBuildingFields(prepared?.LockedFields);
        if (!ModelState.IsValid)
        {
            if (prepared?.TerminalResult is not null)
            {
                StoreResult(prepared.TerminalResult);
                return TerminalOrRedirect(wantsJson);
            }
            if (wantsJson)
            {
                return RetryJson();
            }

            var invalidFields = ModelState
                .Where(static item => item.Value?.Errors.Count > 0 && ControlledValidationFields.Contains(item.Key))
                .Select(static item => item.Key)
                .Order(StringComparer.Ordinal)
                .ToArray();
            TempData[ValidationFieldsTempDataKey] = JsonSerializer.Serialize(invalidFields);
            var overlengthBuildingFields = invalidFields
                .Where(field => field switch
                {
                    nameof(BillingBuilding) => BillingBuilding?.Length > 256,
                    nameof(ShippingBuilding) => ShippingBuilding?.Length > 256,
                    _ => false,
                })
                .ToArray();
            if (overlengthBuildingFields.Length > 0)
            {
                TempData[OverlengthBuildingFieldsTempDataKey] = JsonSerializer.Serialize(overlengthBuildingFields);
            }
            StoreRejected(InstantQuotationProblemCategory.Validation);
            return LocalRedirect("/InstantQuotation/3D-Printing");
        }

        var sessionId = User.FindFirstValue(InstantQuotationSessionIdentityClaim.Type);
        if (!IsValidSessionIdentity(sessionId))
        {
            StoreRejected(InstantQuotationProblemCategory.Authorization);
            return TerminalOrRedirect(wantsJson);
        }

        var isAuthenticated = User.Identity?.IsAuthenticated is true;
        var ownerIdentity = isAuthenticated
            ? NormalizeOptional(User.FindFirstValue(ClaimTypes.NameIdentifier))
            : null;
        if (isAuthenticated && ownerIdentity is null)
        {
            StoreRejected(InstantQuotationProblemCategory.Authorization);
            return TerminalOrRedirect(wantsJson);
        }
        if (submissionService is null)
        {
            StoreRejected(InstantQuotationProblemCategory.DependencyUnavailable);
            return TerminalOrRedirect(wantsJson);
        }

        InstantQuotationSubmissionResult result;
        try
        {
            result = await submissionService.SubmitAsync(
                sessionId!,
                ownerIdentity,
                new InstantQuotationCustomerSubmission(
                    FirstName.Trim(),
                    LastName.Trim(),
                    Email.Trim(),
                    NormalizeOptional(Telephone),
                    Country.Trim(),
                    Company,
                    FormattedTaxNumber(),
                    NormalizeOptional(Description),
                    Mobile.Trim(),
                    NormalizeOptional(BillingBuilding),
                    BillingStreet1.Trim(),
                    NormalizeOptional(BillingStreet2),
                    BillingCity.Trim(),
                    BillingProvince.Trim(),
                    BillingPostalCode.Trim(),
                    ShipToBillingAddress,
                    NormalizeOptional(ShippingBuilding),
                    NormalizeOptional(ShippingStreet1),
                    NormalizeOptional(ShippingStreet2),
                    NormalizeOptional(ShippingCity),
                    NormalizeOptional(ShippingProvince),
                    NormalizeOptional(ShippingPostalCode),
                    NormalizeOptional(ShippingCountry)) with
                { ProfileCompletion = prepared?.Customer?.ProfileCompletion },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError("Instant Quotation submission failed before a controlled result was returned.");
            if (prepared?.TerminalResult is not null)
            {
                StoreResult(prepared.TerminalResult);
                return TerminalOrRedirect(wantsJson);
            }
            StoreRejected(InstantQuotationProblemCategory.Unexpected);
            return TerminalOrRedirect(wantsJson);
        }

        if (prepared?.TerminalResult is not null && result.RequestReference is null)
            result = prepared.TerminalResult;

        // Only validation is known to have failed before a request could be created.
        // A downstream timeout can hide a persisted request, so it is never a safe retry.
        if (wantsJson && result.Outcome == InstantQuotationSubmissionOutcome.Rejected
            && result.ProblemCategory == InstantQuotationProblemCategory.Validation)
        {
            return RetryJson();
        }

        StoreResult(result);
        return TerminalOrRedirect(wantsJson);
    }

    private InstantQuotationProfileDetails PostedProfile() => new()
    {
        FirstName = FirstName ?? string.Empty,
        LastName = LastName ?? string.Empty,
        Email = Email ?? string.Empty,
        Mobile = Mobile ?? string.Empty,
        Telephone = Telephone ?? string.Empty,
        Company = Company ?? string.Empty,
        TaxNumber = TaxNumber ?? string.Empty,
        TaxBranch = TaxBranch ?? string.Empty,
        TaxBranchCode = TaxBranchCode ?? string.Empty,
        BillingBuilding = BillingBuilding ?? string.Empty,
        BillingStreet1 = BillingStreet1 ?? string.Empty,
        BillingStreet2 = BillingStreet2 ?? string.Empty,
        BillingCity = BillingCity ?? string.Empty,
        BillingProvince = BillingProvince ?? string.Empty,
        BillingPostalCode = BillingPostalCode ?? string.Empty,
        Country = Country ?? string.Empty,
        ShippingBuilding = ShippingBuilding ?? string.Empty,
        ShippingStreet1 = ShippingStreet1 ?? string.Empty,
        ShippingStreet2 = ShippingStreet2 ?? string.Empty,
        ShippingCity = ShippingCity ?? string.Empty,
        ShippingProvince = ShippingProvince ?? string.Empty,
        ShippingPostalCode = ShippingPostalCode ?? string.Empty,
        ShippingCountry = ShippingCountry ?? string.Empty,
        ShipToBillingAddress = ShipToBillingAddress,
    };

    private void ApplyProfile(InstantQuotationProfileDetails details)
    {
        FirstName = details.FirstName;
        ModelState.Remove(nameof(FirstName));
        LastName = details.LastName;
        ModelState.Remove(nameof(LastName));
        Email = details.Email;
        ModelState.Remove(nameof(Email));
        Mobile = details.Mobile;
        ModelState.Remove(nameof(Mobile));
        Telephone = details.Telephone;
        ModelState.Remove(nameof(Telephone));
        Company = details.Company;
        ModelState.Remove(nameof(Company));
        TaxNumber = details.TaxNumber;
        ModelState.Remove(nameof(TaxNumber));
        TaxBranch = details.TaxBranch;
        ModelState.Remove(nameof(TaxBranch));
        TaxBranchCode = details.TaxBranchCode;
        ModelState.Remove(nameof(TaxBranchCode));
        BillingBuilding = details.BillingBuilding;
        ModelState.Remove(nameof(BillingBuilding));
        BillingStreet1 = details.BillingStreet1;
        ModelState.Remove(nameof(BillingStreet1));
        BillingStreet2 = details.BillingStreet2;
        ModelState.Remove(nameof(BillingStreet2));
        BillingCity = details.BillingCity;
        ModelState.Remove(nameof(BillingCity));
        BillingProvince = details.BillingProvince;
        ModelState.Remove(nameof(BillingProvince));
        BillingPostalCode = details.BillingPostalCode;
        ModelState.Remove(nameof(BillingPostalCode));
        Country = details.Country;
        ModelState.Remove(nameof(Country));
        ShippingBuilding = details.ShippingBuilding;
        ModelState.Remove(nameof(ShippingBuilding));
        ShippingStreet1 = details.ShippingStreet1;
        ModelState.Remove(nameof(ShippingStreet1));
        ShippingStreet2 = details.ShippingStreet2;
        ModelState.Remove(nameof(ShippingStreet2));
        ShippingCity = details.ShippingCity;
        ModelState.Remove(nameof(ShippingCity));
        ShippingProvince = details.ShippingProvince;
        ModelState.Remove(nameof(ShippingProvince));
        ShippingPostalCode = details.ShippingPostalCode;
        ModelState.Remove(nameof(ShippingPostalCode));
        ShippingCountry = details.ShippingCountry;
        ModelState.Remove(nameof(ShippingCountry));
        ShipToBillingAddress = details.ShipToBillingAddress;
        ModelState.Remove(nameof(ShipToBillingAddress));
        foreach (var field in typeof(InstantQuotationProfileDetails).GetProperties())
        {
            ValidateProfileProperty(field.Name);
        }
    }

    private void ValidateProfileProperty(string field)
    {
        var property = GetType().GetProperty(field);
        if (property is null) return;
        var errors = new List<ValidationResult>();
        Validator.TryValidateProperty(property.GetValue(this), new ValidationContext(this) { MemberName = field }, errors);
        foreach (var error in errors)
            ModelState.AddModelError(field, error.ErrorMessage ?? "Please correct this field.");
    }


    private IActionResult TerminalOrRedirect(bool wantsJson) => wantsJson
        ? new JsonResult(new { outcome = "terminal", redirectUrl = "/InstantQuotation/3D-Printing" })
        : LocalRedirect("/InstantQuotation/3D-Printing");

    private JsonResult RetryJson() => new(new
    {
        outcome = "retry",
        errors = ModelState.Values.SelectMany(static state => state.Errors)
            .Select(static error => error.ErrorMessage)
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .ToArray(),
        invalidFields = ModelState.Keys
            .Where(key => ControlledValidationFields.Contains(key) && ModelState[key]?.Errors.Count > 0)
            .Order(StringComparer.Ordinal)
            .ToArray(),
    });

    private static bool IsValidSessionIdentity(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal void NormalizeOptionalCustomerFields()
    {
        Company = NormalizeOptionalCustomerText(Company);
        TaxNumber = NormalizeOptionalCustomerText(TaxNumber);
        TaxBranchCode = NormalizeOptionalCustomerText(TaxBranchCode);
        RevalidateOptionalCustomerField(nameof(Company));
        RevalidateOptionalCustomerField(nameof(TaxNumber));
        RevalidateOptionalCustomerField(nameof(TaxBranchCode));
    }

    private void ValidateConditionalCustomerFields()
    {
        if (!string.IsNullOrWhiteSpace(TaxNumber)
            && string.Equals(TaxBranch, "branch", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(TaxBranchCode))
        {
            ModelState.AddModelError(nameof(TaxBranchCode), "Branch code is required.");
        }

        if (ShipToBillingAddress)
        {
            return;
        }

        Require(nameof(ShippingStreet1), ShippingStreet1);
        Require(nameof(ShippingCity), ShippingCity);
        Require(nameof(ShippingProvince), ShippingProvince);
        Require(nameof(ShippingPostalCode), ShippingPostalCode);
        Require(nameof(ShippingCountry), ShippingCountry);
    }

    internal void ValidateBuildingFields(IReadOnlySet<string>? lockedFields = null)
    {
        const string error = "This field contains address details. Move them to the separate address fields.";

        if (lockedFields?.Contains(nameof(BillingBuilding)) != true && BuildingContainsAddressComponents(
            BillingBuilding,
            BillingStreet1,
            BillingStreet2,
            BillingCity,
            BillingProvince,
            BillingPostalCode))
        {
            ModelState.AddModelError(nameof(BillingBuilding), error);
        }

        if (!ShipToBillingAddress && lockedFields?.Contains(nameof(ShippingBuilding)) != true
            && BuildingContainsAddressComponents(
                ShippingBuilding,
                ShippingStreet1,
                ShippingStreet2,
                ShippingCity,
                ShippingProvince,
                ShippingPostalCode))
        {
            ModelState.AddModelError(nameof(ShippingBuilding), error);
        }
    }

    internal static bool BuildingContainsAddressComponents(
        string? building,
        params string?[]? addressComponents)
    {
        var normalizedBuilding = NormalizeAddressForComparison(building);
        if (normalizedBuilding.Length == 0 || addressComponents is null)
        {
            return false;
        }

        var matches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in addressComponents)
        {
            var normalizedComponent = NormalizeAddressForComparison(component);
            if (normalizedComponent.Length < 3)
            {
                continue;
            }

            if (normalizedBuilding.Contains(normalizedComponent, StringComparison.Ordinal)
                && matches.Add(normalizedComponent)
                && matches.Count >= 2)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeAddressForComparison(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Normalize(NormalizationForm.FormC)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private void Require(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ModelState.AddModelError(field, "This shipping field is required.");
        }
    }

    private string? FormattedTaxNumber()
    {
        if (string.IsNullOrWhiteSpace(TaxNumber))
        {
            return null;
        }

        var branch = string.Equals(TaxBranch, "branch", StringComparison.OrdinalIgnoreCase)
            ? $"สาขาที่ {TaxBranchCode}"
            : "สำนักงานใหญ่";
        return $"{TaxNumber} ({branch})";
    }

    private void RevalidateOptionalCustomerField(string propertyName)
    {
        ModelState.Remove(propertyName);
        var property = GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Customer field '{propertyName}' was not found.");
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateProperty(
            property.GetValue(this),
            new ValidationContext(this) { MemberName = propertyName },
            validationResults);
        foreach (var validationResult in validationResults)
        {
            ModelState.AddModelError(
                propertyName,
                validationResult.ErrorMessage ?? "The customer field is invalid.");
        }
    }

    private static string? NormalizeOptionalCustomerText(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.All(static character => character is '-'
                or '\u2010'
                or '\u2011'
                or '\u2012'
                or '\u2013'
                or '\u2014'
                or '\u2015'
                or '\u2212'))
        {
            return null;
        }

        return normalized;
    }

    private void StoreResult(InstantQuotationSubmissionResult result)
    {
        if (result.Outcome == InstantQuotationSubmissionOutcome.Completed
            && result.RequestReference is > 0)
        {
            TempData[SubmissionStatusTempDataKey] = SubmissionStatusCompleted;
            TempData[RequestReferenceTempDataKey] = result.RequestReference.Value;
            if (result.TransactionId is { Length: > 0 } && result.JourneyId is Guid journeyId)
            {
                _ = LeadAnalyticsEventQueue.TryQueueInstantQuotation(
                    TempData,
                    result.TransactionId,
                    hasFiles: true,
                    journeyId.ToString(),
                    out _);
            }
            return;
        }

        if (result.Outcome is InstantQuotationSubmissionOutcome.Partial or InstantQuotationSubmissionOutcome.Persisted
            && result.RequestReference is > 0)
        {
            TempData[SubmissionStatusTempDataKey] = SubmissionStatusPartial;
            TempData[RequestReferenceTempDataKey] = result.RequestReference.Value;
            if (result.TransactionId is { Length: > 0 } && result.JourneyId is Guid partialJourneyId)
            {
                _ = LeadAnalyticsEventQueue.TryQueueInstantQuotation(
                    TempData,
                    result.TransactionId,
                    hasFiles: true,
                    partialJourneyId.ToString(),
                    out _);
            }
            return;
        }

        StoreRejected(result.Outcome == InstantQuotationSubmissionOutcome.Rejected
            ? result.ProblemCategory
            : InstantQuotationProblemCategory.Unexpected);
    }

    private void StoreRejected(InstantQuotationProblemCategory category)
    {
        TempData[SubmissionStatusTempDataKey] = SubmissionStatusRejected;
        TempData[ProblemCategoryTempDataKey] = category switch
        {
            InstantQuotationProblemCategory.DependencyUnavailable => "dependency_unavailable",
            InstantQuotationProblemCategory.Authorization => "authorization",
            InstantQuotationProblemCategory.Validation => "validation",
            InstantQuotationProblemCategory.Conflict => "conflict",
            _ => "unexpected",
        };
    }

    private static readonly IReadOnlySet<string> ControlledValidationFields = new HashSet<string>(
        [nameof(FirstName), nameof(LastName), nameof(Email), nameof(Mobile), nameof(Telephone), nameof(Country), nameof(Company), nameof(TaxNumber), nameof(TaxBranchCode), nameof(BillingBuilding), nameof(BillingStreet1), nameof(BillingCity), nameof(BillingProvince), nameof(BillingPostalCode), nameof(ShippingBuilding), nameof(ShippingStreet1), nameof(ShippingCity), nameof(ShippingProvince), nameof(ShippingPostalCode), nameof(ShippingCountry), nameof(Description)],
        StringComparer.Ordinal);
}
