using System.Globalization;

namespace Legacy.Maliev.Web.Components.Layout;

/// <summary>
/// Carries canonical and alternate-language links for the current public or account document.
/// </summary>
/// <param name="CanonicalUrl">The canonical URL in the document's selected culture.</param>
/// <param name="EnglishUrl">The alternate URL for the English document.</param>
/// <param name="ThaiUrl">The alternate URL for the Thai document.</param>
public sealed record PublicDocumentLinksDisplayModel(
    string CanonicalUrl,
    string EnglishUrl,
    string ThaiUrl)
{
    /// <summary>Gets whether the document belongs to the public Knowledge area.</summary>
    public bool IsKnowledgeDocument { get; init; }

    /// <summary>
    /// Builds localized document links, honoring the culture query for private account routes.
    /// </summary>
    /// <param name="context">The current request supplying its document path and private-route culture query.</param>
    /// <returns>The canonical URL and English and Thai alternate URLs.</returns>
    public static PublicDocumentLinksDisplayModel Create(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;
        var isPrivateAccount = path.StartsWithSegments("/account", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/member", StringComparison.OrdinalIgnoreCase);
        var culture = isPrivateAccount
            ? context.Request.Query["culture"].ToString()
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var documentPath = CanonicalUrlPolicy.GetDocumentMetadataPath(path);
        return new PublicDocumentLinksDisplayModel(
            CanonicalUrlPolicy.GetLocalizedUrl(documentPath, culture),
            CanonicalUrlPolicy.GetLocalizedUrl(documentPath, "en"),
            CanonicalUrlPolicy.GetLocalizedUrl(documentPath, "th"))
        {
            IsKnowledgeDocument = path.StartsWithSegments("/knowledges", StringComparison.OrdinalIgnoreCase)
        };
    }
}
