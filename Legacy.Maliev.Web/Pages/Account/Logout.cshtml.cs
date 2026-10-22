using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.Account;

/// <summary>Displays sign-out confirmation and ends the server-managed account session only on POST.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
[Authorize]
public sealed class Logout(IAccountSessionManager sessionManager) : PageModel
{
    /// <summary>Displays sign-out confirmation with non-cacheable and non-referring response headers.</summary>
    public void OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    /// <summary>Ends the server-managed account session and redirects to the home page.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A local redirect to the public home page.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await sessionManager.SignOutAsync(HttpContext, cancellationToken);
        return LocalRedirect("~/");
    }
}
