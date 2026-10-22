using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

/// <summary>Loads the signed-in customer's paged and filtered order history.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="orderClient">The typed client reading or updating orders owned by the current customer.</param>
[Authorize]
public sealed class History(
    IAccountSessionManager sessionManager,
    ICustomerOrderClient orderClient) : PageModel
{
    private const string InvalidQueryValuesMessage = "One or more query values are invalid.";

    /// <summary>The current page of orders owned by the signed-in customer.</summary>
    public CustomerOrderPage Orders { get; private set; } = new([], 1, 0, 0);

    /// <summary>The order-history rows, paging state, filters, and safe service feedback.</summary>
    public MemberOrderHistoryDisplayModel DisplayModel { get; private set; } = MemberOrderHistoryDisplayModel.Empty;

    /// <summary>The optional text filter submitted for the member history listing.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>The requested ordering for the member history listing.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>The one-based page requested for the member history listing.</summary>
    [BindProperty(SupportsGet = true, Name = "index")]
    public int PageIndex { get; set; } = 1;

    /// <summary>The number of results requested per page, subject to the supported page-size policy.</summary>
    [BindProperty(SupportsGet = true, Name = "size")]
    public int PageSize { get; set; } = 25;

    /// <summary>Validates paging and loads the customer's filtered order history.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The history page, a bad-request result for invalid paging, or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !PageSizeQuery.TryResolve(PageSize, out var pageSize))
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

        DisplayModel = MemberListLoaders.CreateOrderDisplayModel(
            Orders,
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
