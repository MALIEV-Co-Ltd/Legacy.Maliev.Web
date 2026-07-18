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

        var culture = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;

        return new PublicOpenGraphMetadataDisplayModel(
            GetText(image),
            GetText(title),
            GetText(description),
            culture,
            CanonicalUrlPolicy.GetLocalizedUrl(context.Request.Path, culture));
    }

    private static string? GetText(object? value) => value switch
    {
        LocalizedHtmlString localizedHtml => localizedHtml.Value,
        LocalizedString localized => localized.Value,
        _ => value?.ToString()
    };
}
