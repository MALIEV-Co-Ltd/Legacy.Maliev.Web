using Legacy.Maliev.Web.Components.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages;

public sealed class ErrorModel : PageModel
{
    public int? ErrorStatusCode { get; private set; }

    public string? IncidentId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public ErrorDisplayModel DisplayModel { get; private set; } = new(false, null);

    public IActionResult OnGet(int? code = null)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var resolved = ErrorDisplayModelResolver.Resolve(HttpContext, code);
        if (resolved is null)
        {
            return Redirect("/");
        }

        DisplayModel = resolved;
        ErrorStatusCode = resolved.StatusCode;
        IncidentId = resolved.IncidentId;
        OccurredAtUtc = resolved.OccurredAtUtc;
        Response.StatusCode = resolved.StatusCode!.Value;
        return Page();
    }

    public IActionResult OnPost(int? code = null) => OnGet(code);
}
