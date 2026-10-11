using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Quotations;

[Authorize]
public sealed class Index(
    IAccountSessionManager sessionManager,
    ICustomerQuotationClient quotationClient) : PageModel
{
    private const string InvalidQueryValuesMessage = "One or more query values are invalid.";

    public CustomerQuotationPage Quotations { get; private set; } = new([], 1, 0, 0);

    public MemberQuotationsIndexDisplayModel DisplayModel { get; private set; } = MemberQuotationsIndexDisplayModel.Empty;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "index")]
    public int PageIndex { get; set; } = 1;

    [BindProperty(SupportsGet = true, Name = "size")]
    public int PageSize { get; set; } = 25;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !PageSizeQuery.TryResolve(PageSize, out var pageSize)
            || !CustomerQuotationSortQuery.TryResolve(Sort, out var canonicalSort))
        {
            return BadRequest();
        }

        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        PageIndex = Math.Max(PageIndex, 1);
        PageSize = pageSize;
        Sort = canonicalSort;
        var result = await quotationClient.ListAsync(
            customerId.Value,
            canonicalSort,
            Search,
            PageIndex,
            PageSize,
            cancellationToken);
        if (result.Page is not null)
        {
            Quotations = result.Page;
        }
        else
        {
            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "Your quotation history could not be loaded."
                    : "Quotation service is temporarily unavailable.");
        }

        DisplayModel = MemberListLoaders.CreateQuotationDisplayModel(
            Quotations,
            Search,
            Sort,
            PageSize,
            ModelState
                .Where(entry => entry.Value is not null)
                .SelectMany(entry => entry.Value!.Errors.Select(error =>
                    error.Exception is not null || IsPagingKey(entry.Key)
                        ? InvalidQueryValuesMessage
                        : error.ErrorMessage))
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Distinct(StringComparer.Ordinal)
                .ToArray());

        return Page();
    }

    private static bool IsPagingKey(string key) =>
        key.Equals("index", StringComparison.OrdinalIgnoreCase) ||
        key.Equals(nameof(PageIndex), StringComparison.OrdinalIgnoreCase) ||
        key.Equals("size", StringComparison.OrdinalIgnoreCase) ||
        key.Equals(nameof(PageSize), StringComparison.OrdinalIgnoreCase);
}
