using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Quotations;

[Authorize]
public sealed class View(
    IAccountSessionManager sessionManager,
    ICustomerQuotationClient quotationClient) : PageModel
{
    public MemberQuotationDetailDisplayModel DisplayModel { get; private set; } = MemberQuotationDetailDisplayModel.Empty;

    public string OperationId { get; private set; } = Guid.NewGuid().ToString("D");

    [TempData]
    public string? Notification { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        return await LoadAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnPostDecisionAsync(
        int quotationId,
        bool accepted,
        string? operationId,
        CancellationToken cancellationToken)
    {
        if (quotationId <= 0 || !Guid.TryParse(operationId, out var parsedOperationId) || parsedOperationId == Guid.Empty)
        {
            return BadRequest();
        }

        OperationId = parsedOperationId.ToString("D");
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null) return Challenge();

        var result = await quotationClient.DecideAsync(
            customerId.Value,
            quotationId,
            accepted,
            parsedOperationId,
            cancellationToken);
        if (result.Succeeded)
        {
            CustomerJourneyAnalyticsEventQueue.TryQueueQuotationDecision(
                TempData,
                quotationId,
                accepted ? "accepted" : "declined",
                out _);
            Notification = accepted
                ? "You have successfully accepted the quotation. A payable invoice has been generated."
                : "You have successfully declined the quotation.";
            return RedirectToPage(new { id = quotationId });
        }

        ModelState.AddModelError(
            string.Empty,
            result.Conflict
                ? "The quotation changed while your decision was being recorded. Please review it and try again."
                : result.ServiceAvailable
                    ? "Your quotation decision could not be recorded."
                    : "Quotation processing is temporarily unavailable.");
        return await LoadAsync(quotationId, cancellationToken);
    }

    private async Task<IActionResult> LoadAsync(int quotationId, CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null) return Challenge();

        var result = await quotationClient.GetAsync(customerId.Value, quotationId, cancellationToken);
        if (result.Details is null && result.ServiceAvailable && result.Authorized) return NotFound();
        if (result.Details is null)
        {
            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "Your quotation could not be loaded."
                    : "Quotation service is temporarily unavailable.");
        }

        DisplayModel = CreateDisplayModel(
            result.Details,
            Notification,
            ModelState
                .SelectMany(entry => entry.Value?.Errors ?? [])
                .Where(error => error.Exception is null && !string.IsNullOrWhiteSpace(error.ErrorMessage))
                .Select(error => error.ErrorMessage)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        return Page();
    }

    private static MemberQuotationDetailDisplayModel CreateDisplayModel(
        CustomerQuotationDetails? details,
        string? notification,
        IReadOnlyList<string> errors)
    {
        if (details is null)
        {
            return MemberQuotationDetailDisplayModel.Empty with { Notification = notification, Errors = errors };
        }

        var quotation = details.Quotation;
        return new MemberQuotationDetailDisplayModel(
            quotation.Id,
            quotation.Accepted,
            quotation.Period,
            quotation.ExpirationDate.ToString("yyyy-MM-dd"),
            quotation.Subtotal.ToString("N2"),
            quotation.Vat.ToString("N2"),
            quotation.Total.ToString("N2"),
            quotation.WithholdingTax?.ToString("N2") ?? "-",
            quotation.QuotedAmount?.ToString("N2") ?? "-",
            quotation.CurrencyId,
            string.IsNullOrWhiteSpace(quotation.ShippedVia) ? "-" : quotation.ShippedVia,
            string.IsNullOrWhiteSpace(quotation.Fob) ? "-" : quotation.Fob,
            string.IsNullOrWhiteSpace(quotation.Terms) ? "-" : quotation.Terms,
            quotation.Comment,
            quotation.Accepted is null && quotation.ExpirationDate.Date >= DateTime.UtcNow.Date,
            notification,
            errors,
            details.OrderItems.Select(item => new MemberQuotationLineDisplayModel(
                item.Description ?? "-",
                item.Quantity?.ToString() ?? "-",
                item.UnitPrice?.ToString("N2") ?? "-",
                item.Subtotal?.ToString("N2") ?? "-")).ToArray(),
            details.Orders.Select(order => new MemberQuotationOrderDisplayModel(
                order.OrderId,
                $"/member/orders/view?itemID={order.OrderId}")).ToArray(),
            details.Files.Select(file => GetDisplayFileName(file.ObjectName)).ToArray());
    }

    private static string GetDisplayFileName(string objectName) =>
        objectName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "-";
}
