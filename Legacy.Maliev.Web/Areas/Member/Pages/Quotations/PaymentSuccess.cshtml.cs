using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Quotations;

[Authorize]
public sealed class PaymentSuccess : PageModel
{
    public RedirectToPageResult OnGet() => RedirectToPage("/Quotations/Index");
}
