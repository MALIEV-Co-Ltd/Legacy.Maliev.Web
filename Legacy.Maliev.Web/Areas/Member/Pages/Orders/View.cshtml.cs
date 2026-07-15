using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

[Authorize]
public sealed class View(
    IAccountSessionManager sessionManager,
    ICustomerOrderClient orderClient) : PageModel
{
    public CustomerOrderDetails? Details { get; private set; }

    [TempData]
    public string? Notification { get; set; }

    public async Task<IActionResult> OnGetAsync(int itemID, CancellationToken cancellationToken)
    {
        if (itemID <= 0)
        {
            return NotFound();
        }

        return await LoadAsync(itemID, cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            return NotFound();
        }

        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        var result = await orderClient.CancelAsync(customerId.Value, orderId, cancellationToken);
        if (result.Succeeded)
        {
            Notification = "Your order cancellation was recorded.";
            return RedirectToPage(new { itemID = orderId });
        }

        ModelState.AddModelError(
            string.Empty,
            result.Conflict
                ? "This order can no longer be cancelled."
                : result.ServiceAvailable
                    ? "Your order could not be cancelled."
                    : "Order service is temporarily unavailable.");
        return await LoadAsync(orderId, cancellationToken);
    }

    private async Task<IActionResult> LoadAsync(int orderId, CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        var result = await orderClient.GetAsync(customerId.Value, orderId, cancellationToken);
        if (result.Details is null)
        {
            if (result.ServiceAvailable && result.Authorized)
            {
                return NotFound();
            }

            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "Your order could not be loaded."
                    : "Order service is temporarily unavailable.");
        }

        Details = result.Details;
        return Page();
    }
}
