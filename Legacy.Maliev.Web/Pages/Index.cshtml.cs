using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.Web.Pages;

/// <summary>Provides the public home page and the retained local language-preference POST.</summary>
public sealed class IndexModel : PageModel
{
    /// <summary>Displays the public home page.</summary>
    public void OnGet()
    {
    }

    /// <summary>Stores a supported language preference and redirects to a safe localized local path.</summary>
    /// <param name="culture">The requested supported UI culture.</param>
    /// <param name="returnUrl">The requested local path to visit after completing the account workflow.</param>
    /// <returns>A local redirect with the normalized culture preference.</returns>
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
