using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

[Authorize]
public sealed class View(
    IAccountSessionManager sessionManager,
    ICustomerOrderClient orderClient,
    ICustomerMemberDetailClient memberDetailClient) : PageModel
{
    public MemberOrderDetailDisplayModel DisplayModel { get; private set; } = MemberOrderDetailDisplayModel.Empty;

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
        var loaded = await MemberDetailLoaders.LoadOrderAsync(
            HttpContext,
            sessionManager,
            orderClient,
            memberDetailClient,
            orderId,
            Notification,
            cancellationToken);
        if (loaded.IsUnauthorized)
        {
            return Challenge();
        }

        if (loaded.IsNotFound)
        {
            return NotFound();
        }

        var pageErrors = ModelState
            .SelectMany(entry => entry.Value?.Errors ?? [])
            .Where(error => error.Exception is null && !string.IsNullOrWhiteSpace(error.ErrorMessage))
            .Select(error => error.ErrorMessage);
        DisplayModel = loaded.Model with
        {
            Errors = loaded.Model.Errors.Concat(pageErrors).Distinct(StringComparer.Ordinal).ToArray(),
        };
        return Page();
    }
}
