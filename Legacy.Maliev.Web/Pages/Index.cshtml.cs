using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.Web.Pages;

public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }

    public IActionResult OnPostSetLanguage(string? culture, string? returnUrl)
    {
        culture = CanonicalUrlPolicy.NormalizeSupportedCulture(culture);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = true
            });

        return LocalRedirect(CanonicalUrlPolicy.GetLocalizedReturnUrl(returnUrl, culture));
    }
}
