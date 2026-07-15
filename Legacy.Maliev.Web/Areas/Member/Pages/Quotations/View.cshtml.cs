using Legacy.Maliev.Web.Application;
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
    public CustomerQuotationDetails? Details { get; private set; }

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

        Details = result.Details;
        return Page();
    }
}
