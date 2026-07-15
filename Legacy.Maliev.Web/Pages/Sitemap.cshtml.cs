using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages;

public sealed class Sitemap : PageModel
{
    public IActionResult OnGet() =>
        Content(
            SitemapXmlRenderer.Render(PublicSearchRouteCatalog.Routes),
            "application/xml; charset=utf-8");
}
