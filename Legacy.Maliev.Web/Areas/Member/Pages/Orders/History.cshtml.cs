using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

[Authorize]
public sealed class History(
    IAccountSessionManager sessionManager,
    ICustomerOrderClient orderClient) : PageModel
{
    private const string InvalidQueryValuesMessage = "One or more query values are invalid.";

    public CustomerOrderPage Orders { get; private set; } = new([], 1, 0, 0);

    public MemberOrderHistoryDisplayModel DisplayModel { get; private set; } = MemberOrderHistoryDisplayModel.Empty;

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
        var result = await orderClient.ListAsync(
            customerId.Value,
            string.IsNullOrWhiteSpace(Sort) ? "OrderCreatedDate_Descending" : Sort,
            Search,
            PageIndex,
            PageSize,
            cancellationToken);
        if (result.Page is not null)
        {
            Orders = result.Page;
        }
        else
        {
            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "Your order history could not be loaded."
                    : "Order service is temporarily unavailable.");
        }

        DisplayModel = new MemberOrderHistoryDisplayModel(
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
                .ToArray(),
            Orders.Items.Select(order => new MemberOrderHistoryItemDisplayModel(
                order.Id,
                order.Name,
                order.Description,
                order.Quantity,
                order.Subtotal?.ToString("N2") ?? "-",
                order.CreatedDate?.ToString("yyyy-MM-dd") ?? "-")).ToArray(),
            Orders.PageIndex > 1
                ? BuildPageHref(Orders.PageIndex - 1, PageSize, Sort, Search)
                : null,
            Orders.PageIndex < Orders.TotalPages
                ? BuildPageHref(Orders.PageIndex + 1, PageSize, Sort, Search)
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

        return "/member/orders/history" + QueryString.Create(values);
    }

    private static bool IsPagingKey(string key) =>
        key.Equals("index", StringComparison.OrdinalIgnoreCase) ||
        key.Equals(nameof(PageIndex), StringComparison.OrdinalIgnoreCase) ||
        key.Equals("size", StringComparison.OrdinalIgnoreCase) ||
        key.Equals(nameof(PageSize), StringComparison.OrdinalIgnoreCase);
}
