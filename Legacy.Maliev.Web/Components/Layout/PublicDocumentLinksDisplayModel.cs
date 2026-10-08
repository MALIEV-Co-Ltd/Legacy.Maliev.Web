using System.Globalization;

namespace Legacy.Maliev.Web.Components.Layout;

public sealed record PublicDocumentLinksDisplayModel(
    string CanonicalUrl,
    string EnglishUrl,
    string ThaiUrl)
{
    public static PublicDocumentLinksDisplayModel Create(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;
        var isPrivateAccount = path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/member", StringComparison.OrdinalIgnoreCase);
        var culture = isPrivateAccount
            ? context.Request.Query["culture"].ToString()
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        return new PublicDocumentLinksDisplayModel(
            CanonicalUrlPolicy.GetLocalizedUrl(path, culture),
            CanonicalUrlPolicy.GetLocalizedUrl(path, "en"),
            CanonicalUrlPolicy.GetLocalizedUrl(path, "th"));
    }
}
