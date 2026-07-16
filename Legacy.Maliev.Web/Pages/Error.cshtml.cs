using System.Diagnostics;
using Legacy.Maliev.Web.Components.Pages;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages;

public sealed class ErrorModel : PageModel
{
    public int? ErrorStatusCode { get; private set; }

    public string? RequestId { get; private set; }

    public bool ShowRequestId => !string.IsNullOrWhiteSpace(RequestId);

    public ErrorDisplayModel DisplayModel => new(
        ErrorStatusCode == StatusCodes.Status404NotFound,
        ShowRequestId ? RequestId : null);

    public void OnGet(int? code = null)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        ErrorStatusCode = code;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (code is >= 400 and <= 599)
        {
            Response.StatusCode = code.Value;
        }
    }
}
