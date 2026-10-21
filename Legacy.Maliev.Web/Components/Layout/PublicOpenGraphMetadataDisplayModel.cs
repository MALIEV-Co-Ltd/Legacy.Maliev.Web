using System.Globalization;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Components.Layout;

/// <summary>
/// Carries normalized social-preview metadata and the localized canonical page URL.
/// </summary>
/// <param name="Image">The social-preview image URL, with the default public hero image used when no image is supplied.</param>
/// <param name="Title">The localized document title, or null when no title is supplied.</param>
/// <param name="Description">The localized document description, or null when not supplied.</param>
/// <param name="Locale">The Open Graph locale corresponding to the selected English or Thai culture.</param>
/// <param name="Url">The localized canonical URL represented by this preview.</param>
public sealed record PublicOpenGraphMetadataDisplayModel(
    string? Image,
    string? Title,
    string? Description,
    string Locale,
    string Url)
{
    /// <summary>
    /// Resolves localized preview text and supplies the default image when page metadata omits one.
    /// </summary>
    /// <param name="context">The request providing the document path for the canonical URL.</param>
    /// <param name="title">The page title as localized text or another display value.</param>
    /// <param name="description">The page description as localized text or another display value.</param>
    /// <param name="image">The supplied image URL, or a blank value to select the default image.</param>
    /// <returns>Normalized social-preview metadata for the current page.</returns>
    public static PublicOpenGraphMetadataDisplayModel Create(
        HttpContext context,
        object? title,
        object? description,
        object? image)
    {
        ArgumentNullException.ThrowIfNull(context);

        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        culture = string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "th";
        var imageText = GetText(image);

        return new PublicOpenGraphMetadataDisplayModel(
            string.IsNullOrWhiteSpace(imageText)
                ? "https://www.maliev.com/src/images/landing/landing-hero-cnc.webp"
                : imageText,
            GetText(title),
            GetText(description),
            string.Equals(culture, "th", StringComparison.OrdinalIgnoreCase) ? "th_TH" : "en_US",
            CanonicalUrlPolicy.GetLocalizedUrl(context.Request.Path, culture));
    }

    private static string? GetText(object? value) => value switch
    {
        LocalizedHtmlString localizedHtml => localizedHtml.Value,
        LocalizedString localized => localized.Value,
        _ => value?.ToString()
    };
}
