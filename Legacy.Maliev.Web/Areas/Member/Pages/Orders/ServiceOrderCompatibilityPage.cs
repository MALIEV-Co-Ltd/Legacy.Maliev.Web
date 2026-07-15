using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Orders;

public abstract class ServiceOrderCompatibilityPage(string quotationItem) : PageModel
{
    public RedirectToPageResult OnGet()
    {
        return RedirectToPage(
            "/Quotation/Index",
            new
            {
                area = string.Empty,
                item = quotationItem,
            });
    }
}
