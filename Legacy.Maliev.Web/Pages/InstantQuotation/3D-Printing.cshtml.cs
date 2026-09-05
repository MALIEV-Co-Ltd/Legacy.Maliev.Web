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

public sealed class ThreeDimensionalPrinting : PageModel
{
    private readonly ILogger<ThreeDimensionalPrinting>? logger;
    private readonly IInstantQuotationSubmissionService? submissionService;

    public ThreeDimensionalPrinting()
    {
    }

    [ActivatorUtilitiesConstructor]
    public ThreeDimensionalPrinting(
        IInstantQuotationSubmissionService submissionService,
        ILogger<ThreeDimensionalPrinting> logger)
    {
        this.submissionService = submissionService;
        this.logger = logger;
    }

    public const string ProblemCategoryTempDataKey = "InstantQuotationProblemCategory";
    public const string RequestReferenceTempDataKey = "InstantQuotationRequestReference";
    public const string SubmissionStatusCompleted = "completed";
    public const string SubmissionStatusPartial = "partial";
    public const string SubmissionStatusRejected = "rejected";
    public const string SubmissionStatusTempDataKey = "InstantQuotationSubmissionStatus";
    public const string ValidationFieldsTempDataKey = "InstantQuotationValidationFields";

    [BindProperty]
    [StringLength(50)]
    public string? Company { get; set; }

    [BindProperty]
    [StringLength(256)]
    public string? BillingBuilding { get; set; }

    [BindProperty]
    [Required]
    [StringLength(256)]
    public string BillingStreet1 { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(256)]
    public string? BillingStreet2 { get; set; }

    [BindProperty]
    [Required]
    [StringLength(256)]
    public string BillingCity { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(256)]
    public string BillingProvince { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(20)]
    public string BillingPostalCode { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(50)]
    public string Country { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(512)]
    public string? Description { get; set; }

    public InstantQuotationDisplayModel DisplayModel => InstantQuotationCalculator.CreateDisplayModel();

    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(50)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(50)]
    public string Mobile { get; set; } = string.Empty;

    [BindProperty]
    [RegularExpression(@"^[0-9]{13}$", ErrorMessage = "Thai tax ID must contain exactly 13 digits.")]
    [StringLength(50)]
    public string? TaxNumber { get; set; }

    [BindProperty]
    public string TaxBranch { get; set; } = "head-office";

    [BindProperty]
    [RegularExpression(@"^[0-9]{5}$")]
    [StringLength(5)]
    public string? TaxBranchCode { get; set; }

    [BindProperty]
    public bool ShipToBillingAddress { get; set; } = true;

    [BindProperty]
    [StringLength(256)]
    public string? ShippingBuilding { get; set; }

    [BindProperty]
    [StringLength(256)]
    public string? ShippingStreet1 { get; set; }

    [BindProperty]
    [StringLength(256)]
    public string? ShippingStreet2 { get; set; }

    [BindProperty]
    [StringLength(256)]
    public string? ShippingCity { get; set; }

    [BindProperty]
    [StringLength(256)]
    public string? ShippingProvince { get; set; }

    [BindProperty]
    [StringLength(20)]
    public string? ShippingPostalCode { get; set; }

    [BindProperty]
    [StringLength(50)]
    public string? ShippingCountry { get; set; }

    [BindProperty]
    [StringLength(50)]
    public string? Telephone { get; set; }

    public void OnGet()
    {
    }

    public JsonResult OnGetGetEstimate(
        string? material,
        double dimensionZ,
        double volume,
        double footprint,
        string? areaProfile,
        string? perimeterProfile,
        string? currency,
        int quantity) => new(InstantQuotationCalculator.GetEstimate(
            material,
            dimensionZ,
            volume,
            footprint,
            areaProfile,
            perimeterProfile,
            currency,
            quantity));

    public JsonResult OnGetGetOrderTotal(
        string? processes,
        string? subtotals,
        double totalWeightGrams,
        double totalBoundingCm3,
        string? currency) => new(InstantQuotationCalculator.GetOrderTotal(
            processes,
            subtotals,
            totalWeightGrams,
            totalBoundingCm3,
            currency));

    public async Task<IActionResult> OnPostSubmitRequestAsync(CancellationToken cancellationToken)
    {
        NormalizeOptionalCustomerFields();
        ValidateConditionalCustomerFields();
        ValidateBuildingFields();
        if (!ModelState.IsValid)
        {
            var invalidFields = ModelState
                .Where(static item => item.Value?.Errors.Count > 0 && ControlledValidationFields.Contains(item.Key))
                .Select(static item => item.Key)
                .Order(StringComparer.Ordinal)
                .ToArray();
            TempData[ValidationFieldsTempDataKey] = JsonSerializer.Serialize(invalidFields);
            StoreRejected(InstantQuotationProblemCategory.Validation);
            return LocalRedirect("/InstantQuotation/3D-Printing");
        }

        var sessionId = User.FindFirstValue(InstantQuotationSessionIdentityClaim.Type);
        if (!IsValidSessionIdentity(sessionId))
        {
            StoreRejected(InstantQuotationProblemCategory.Authorization);
            return LocalRedirect("/InstantQuotation/3D-Printing");
        }

        var isAuthenticated = User.Identity?.IsAuthenticated is true;
        var ownerIdentity = isAuthenticated
            ? NormalizeOptional(User.FindFirstValue(ClaimTypes.NameIdentifier))
            : null;
        if (isAuthenticated && ownerIdentity is null)
        {
            StoreRejected(InstantQuotationProblemCategory.Authorization);
            return LocalRedirect("/InstantQuotation/3D-Printing");
        }
        if (submissionService is null)
        {
            StoreRejected(InstantQuotationProblemCategory.DependencyUnavailable);
            return LocalRedirect("/InstantQuotation/3D-Printing");
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
                    NormalizeOptional(ShippingCountry)),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError("Instant Quotation submission failed before a controlled result was returned.");
            StoreRejected(InstantQuotationProblemCategory.Unexpected);
            return LocalRedirect("/InstantQuotation/3D-Printing");
        }

        StoreResult(result);
        return LocalRedirect("/InstantQuotation/3D-Printing");
    }

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

    internal void ValidateBuildingFields()
    {
        const string error = "This field contains address details. Move them to the separate address fields.";

        if (BuildingContainsAddressComponents(
            BillingBuilding,
            BillingStreet1,
            BillingStreet2,
            BillingCity,
            BillingProvince,
            BillingPostalCode))
        {
            ModelState.AddModelError(nameof(BillingBuilding), error);
        }

        if (!ShipToBillingAddress
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
            _ = LeadAnalyticsEventQueue.TryQueueManualQuotation(
                TempData,
                result.RequestReference.Value,
                "3d_printing",
                hasFiles: true,
                fileUploadCompleted: true,
                out _);
            return;
        }

        if (result.Outcome is InstantQuotationSubmissionOutcome.Partial or InstantQuotationSubmissionOutcome.Persisted
            && result.RequestReference is > 0)
        {
            TempData[SubmissionStatusTempDataKey] = SubmissionStatusPartial;
            TempData[RequestReferenceTempDataKey] = result.RequestReference.Value;
            _ = LeadAnalyticsEventQueue.TryQueueManualQuotation(
                TempData,
                result.RequestReference.Value,
                "3d_printing",
                hasFiles: true,
                fileUploadCompleted: false,
                out _);
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
