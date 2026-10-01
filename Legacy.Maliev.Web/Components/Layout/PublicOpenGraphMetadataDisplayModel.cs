using System.Globalization;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Web.Components.Layout;

public sealed record PublicOpenGraphMetadataDisplayModel(
    string? Image,
    string? Title,
    string? Description,
    string Locale,
    string Url)
{
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
