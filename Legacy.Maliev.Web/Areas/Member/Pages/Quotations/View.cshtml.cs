using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Quotations;

/// <summary>Loads an owned quotation and records an accept or decline decision using its expected operation identifier.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="quotationClient">The typed client reading or submitting quotation operations for this page.</param>
/// <param name="memberDetailClient">The typed client supplying associated member detail data.</param>
[Authorize]
public sealed class View(
    IAccountSessionManager sessionManager,
    ICustomerQuotationClient quotationClient,
    ICustomerMemberDetailClient memberDetailClient) : PageModel
{
    /// <summary>The owned quotation details and safe decision feedback.</summary>
    public MemberQuotationDetailDisplayModel DisplayModel { get; private set; } = MemberQuotationDetailDisplayModel.Empty;

    /// <summary>The operation identifier used to correlate and validate this owned submission.</summary>
    public string OperationId { get; private set; } = string.Empty;

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>Loads the requested quotation through the member ownership boundary.</summary>
    /// <param name="id">The identifier of the owned quotation to display.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The quotation detail page, a not-found result, or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        return await LoadAsync(id, cancellationToken);
    }

    /// <summary>Validates the expected operation identifier and records the customer's quotation decision.</summary>
    /// <param name="quotationId">The identifier of the owned quotation receiving the decision.</param>
    /// <param name="accepted">Whether the customer accepted rather than declined the quotation.</param>
    /// <param name="operationId">The submitted operation identifier that must match the expected quotation-decision operation.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A detail redirect after success, the detail page with errors, a bad-request result, or an authentication challenge.</returns>
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

        var expectedOperationId = CreateDecisionOperationId(quotationId);
        if (parsedOperationId != expectedOperationId)
        {
            return BadRequest();
        }

        OperationId = expectedOperationId.ToString("D");
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
        OperationId = CreateDecisionOperationId(quotationId).ToString("D");
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

    internal static Guid CreateDecisionOperationId(int quotationId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"legacy-web-customer-quotation-decision:{quotationId}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}
