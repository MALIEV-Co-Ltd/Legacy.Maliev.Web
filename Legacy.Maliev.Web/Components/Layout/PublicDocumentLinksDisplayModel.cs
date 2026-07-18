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
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        return new PublicDocumentLinksDisplayModel(
            CanonicalUrlPolicy.GetLocalizedUrl(path, culture),
            CanonicalUrlPolicy.GetLocalizedUrl(path, "en"),
            CanonicalUrlPolicy.GetLocalizedUrl(path, "th"));
    }
}
