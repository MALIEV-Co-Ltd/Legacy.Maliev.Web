using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
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
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        PageIndex = Math.Max(PageIndex, 1);
        PageSize = Math.Clamp(PageSize, 1, 100);
        var result = await quotationClient.ListAsync(
            customerId.Value,
            string.IsNullOrWhiteSpace(Sort) ? "QuotationCreatedDate_Descending" : Sort,
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

        DisplayModel = new MemberQuotationsIndexDisplayModel(
            Search,
            Sort,
            PageSize,
            ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .ToArray(),
            Quotations.Items.Select(quotation => new MemberQuotationListItemDisplayModel(
                quotation.Id,
                quotation.Accepted,
                quotation.QuotedAmount?.ToString("N2") ?? "-",
                quotation.CurrencyId,
                quotation.ExpirationDate.ToString("yyyy-MM-dd"),
                quotation.CreatedDate?.ToString("yyyy-MM-dd") ?? "-")).ToArray(),
            Quotations.HasPreviousPage
                ? BuildPageHref(Quotations.PageIndex - 1, PageSize, Sort, Search)
                : null,
            Quotations.HasNextPage
                ? BuildPageHref(Quotations.PageIndex + 1, PageSize, Sort, Search)
                : null);

        return Page();
    }

    private static string BuildPageHref(int pageIndex, int pageSize, string? sort, string? search)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("index", pageIndex.ToString(CultureInfo.InvariantCulture)),
            new("size", pageSize.ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(sort))
        {
            values.Add(new("sort", sort));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            values.Add(new("search", search));
        }

        return "/member/quotations" + QueryString.Create(values);
    }
}
