using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

[Authorize]
public sealed class Index : PageModel
{
    public void OnGet()
    {
    }
}
