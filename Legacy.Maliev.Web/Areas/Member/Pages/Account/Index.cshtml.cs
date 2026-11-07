using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account;

/// <summary>Provides the authenticated member account landing page.</summary>
[Authorize]
public sealed class Index : PageModel
{
}
