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
    ICustomerQuotationClient quotationClient,
    ICustomerMemberDetailClient memberDetailClient) : PageModel
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
        if (customerId is null)
        {
            return Challenge();
        }

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
        var loaded = await MemberDetailLoaders.LoadQuotationAsync(
            HttpContext,
            sessionManager,
            quotationClient,
            memberDetailClient,
            quotationId,
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
