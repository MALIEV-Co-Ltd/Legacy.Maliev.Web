using Legacy.Maliev.Web.Components.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages;

/// <summary>Projects a controlled HTTP error and incident reference without exposing internal exception details.</summary>
public sealed class ErrorModel : PageModel
{
    /// <summary>The controlled HTTP status represented by the resolved error page, or null before resolution.</summary>
    public int? ErrorStatusCode { get; private set; }

    /// <summary>The safe incident reference exposed to support without internal exception details.</summary>
    public string? IncidentId { get; private set; }

    /// <summary>The UTC occurrence timestamp supplied by the resolved error display model.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>The controlled error presentation containing only safe status and incident information.</summary>
    public ErrorDisplayModel DisplayModel { get; private set; } = new(false, null);

    /// <summary>Resolves the controlled HTTP error context and disables response caching and referrers.</summary>
    /// <param name="code">The optional requested error status, resolved against the current HTTP error context.</param>
    /// <returns>The status-preserving error page, or a home redirect when no error context can be resolved.</returns>
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

    /// <summary>Uses the same controlled error projection for a POST error response.</summary>
    /// <param name="code">The optional requested error status, resolved against the current HTTP error context.</param>
    /// <returns>The status-preserving error page, or a home redirect when no error context can be resolved.</returns>
    public IActionResult OnPost(int? code = null) => OnGet(code);
}
