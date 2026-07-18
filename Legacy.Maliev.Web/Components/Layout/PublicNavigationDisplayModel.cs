using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Components.Layout;

public sealed record PublicNavigationDisplayModel(
    bool SuppressIdentityNavigation,
    bool IsAuthenticated,
    string? DisplayName,
    string AntiforgeryFieldName,
    string AntiforgeryToken,
    string LanguageAction,
    IReadOnlyList<PublicNavigationCultureOption> Cultures)
{
    public static PublicNavigationDisplayModel Create(HttpContext context, bool suppressIdentityNavigation)
    {
        ArgumentNullException.ThrowIfNull(context);

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        var localization = context.RequestServices
            .GetRequiredService<IOptions<RequestLocalizationOptions>>()
            .Value;
        var currentCulture = context.Features.Get<IRequestCultureFeature>()
            ?.RequestCulture.UICulture.Name ?? "th";
        var cultures = (localization.SupportedUICultures ?? Array.Empty<CultureInfo>())
            .Select(culture => new PublicNavigationCultureOption(
                culture.Name,
                culture.TwoLetterISOLanguageName == "th" ? "ไทย" : "EN",
                culture.Name == currentCulture))
            .ToArray();
        var returnUrl = string.IsNullOrEmpty(context.Request.Path)
            ? "~/"
            : $"~{context.Request.Path.Value}";
        var languageAction = QueryHelpers.AddQueryString(
            "/",
            new Dictionary<string, string?>
            {
                ["returnUrl"] = returnUrl,
                ["handler"] = "SetLanguage"
            });

        return new PublicNavigationDisplayModel(
            suppressIdentityNavigation,
            context.User.Identity?.IsAuthenticated == true,
            context.User.Identity?.Name,
            tokens.FormFieldName,
            tokens.RequestToken ?? throw new InvalidOperationException("The antiforgery request token was not generated."),
            languageAction,
            cultures);
    }
}

public sealed record PublicNavigationCultureOption(string Name, string Label, bool IsSelected);
