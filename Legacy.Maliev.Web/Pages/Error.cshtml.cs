using Legacy.Maliev.Web.Components.Pages;
using Legacy.Maliev.Web.Middleware;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages;

public sealed class ErrorModel : PageModel
{
    public int? ErrorStatusCode { get; private set; }

    public string? IncidentId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public ErrorDisplayModel DisplayModel => new(
        ErrorStatusCode == StatusCodes.Status404NotFound,
        IncidentId,
        OccurredAtUtc,
        ErrorStatusCode);

    public void OnGet(int? code = null)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        ErrorStatusCode = code;
        var incident = ErrorIncidentHandler.GetOrCreate(HttpContext, DateTimeOffset.UtcNow);
        IncidentId = incident.IncidentId;
        OccurredAtUtc = incident.OccurredAtUtc;

        if (code is >= 400 and <= 599)
        {
            Response.StatusCode = code.Value;
        }
    }
}
