using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.InstantQuotation;

public sealed class ThreeDimensionalPrinting : PageModel
{
    public InstantQuotationDisplayModel DisplayModel => InstantQuotationCalculator.CreateDisplayModel();

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
}
