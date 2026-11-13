using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

/// <summary>Loads an owned member order and records eligible cancellation requests.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="orderClient">The typed client reading or updating orders owned by the current customer.</param>
/// <param name="memberDetailClient">The typed client supplying associated member detail data.</param>
[Authorize]
public sealed class View(
    IAccountSessionManager sessionManager,
    ICustomerOrderClient orderClient,
    ICustomerMemberDetailClient memberDetailClient) : PageModel
{
    /// <summary>The owned order details and safe action feedback.</summary>
    public MemberOrderDetailDisplayModel DisplayModel { get; private set; } = MemberOrderDetailDisplayModel.Empty;

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>Loads the requested order through the member ownership boundary.</summary>
    /// <param name="itemID">The identifier of the owned order to display.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The order detail page, a not-found result, or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(int itemID, CancellationToken cancellationToken)
    {
        if (itemID <= 0)
        {
            return NotFound();
        }

        return await LoadAsync(itemID, cancellationToken);
    }

    /// <summary>Records a cancellation request for an eligible order owned by the current customer.</summary>
    /// <param name="orderId">The identifier of the owned order to cancel.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A detail redirect after success, the detail page with errors, a not-found result, or an authentication challenge.</returns>
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
