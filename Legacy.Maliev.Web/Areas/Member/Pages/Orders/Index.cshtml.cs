using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

/// <summary>Provides the authenticated landing page for choosing a member order service.</summary>
[Authorize]
public sealed class Index : PageModel
{
    /// <summary>Displays the member order-service selection page.</summary>
    public void OnGet()
    {
    }
}
