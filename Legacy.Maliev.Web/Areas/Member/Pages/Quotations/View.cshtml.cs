using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
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

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        var result = await quotationClient.GetAsync(
            customerId.Value,
            id,
            cancellationToken);
        if (result.Details is null)
        {
            if (result.ServiceAvailable && result.Authorized)
            {
                return NotFound();
            }

            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "Your quotation could not be loaded."
                    : "Quotation service is temporarily unavailable.");
        }

        DisplayModel = CreateDisplayModel(result.Details, ModelState
            .SelectMany(entry => entry.Value?.Errors ?? [])
            .Where(error => error.Exception is null && !string.IsNullOrWhiteSpace(error.ErrorMessage))
            .Select(error => error.ErrorMessage)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        return Page();
    }

    private static MemberQuotationDetailDisplayModel CreateDisplayModel(
        CustomerQuotationDetails? details,
        IReadOnlyList<string> errors)
    {
        if (details is null)
        {
            return MemberQuotationDetailDisplayModel.Empty with { Errors = errors };
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
