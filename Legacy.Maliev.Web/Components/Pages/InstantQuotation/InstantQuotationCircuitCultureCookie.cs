using Microsoft.AspNetCore.Localization;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal static class InstantQuotationCircuitCultureCookie
{
    public static void PersistControlledQueryCulture(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var culture = context.Request.Query["culture"].ToString();
        if (culture is not ("en" or "th"))
        {
            return;
        }

        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = true,
            });
    }
}
