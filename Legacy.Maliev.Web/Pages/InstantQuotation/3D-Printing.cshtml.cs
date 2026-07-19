using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
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

    [BindProperty]
    [StringLength(50)]
    public string? Company { get; set; }

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
    [StringLength(50)]
    public string? TaxNumber { get; set; }

    [BindProperty]
    [Required]
    [StringLength(50)]
    public string Telephone { get; set; } = string.Empty;

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
        if (!ModelState.IsValid)
        {
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
                    Telephone.Trim(),
                    Country.Trim(),
                    NormalizeOptional(Company),
                    NormalizeOptional(TaxNumber),
                    NormalizeOptional(Description)),
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

    private void StoreResult(InstantQuotationSubmissionResult result)
    {
        if (result.Outcome == InstantQuotationSubmissionOutcome.Completed
            && result.RequestReference is > 0)
        {
            TempData[SubmissionStatusTempDataKey] = SubmissionStatusCompleted;
            TempData[RequestReferenceTempDataKey] = result.RequestReference.Value;
            return;
        }

        if (result.Outcome is InstantQuotationSubmissionOutcome.Partial or InstantQuotationSubmissionOutcome.Persisted
            && result.RequestReference is > 0)
        {
            TempData[SubmissionStatusTempDataKey] = SubmissionStatusPartial;
            TempData[RequestReferenceTempDataKey] = result.RequestReference.Value;
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
}
