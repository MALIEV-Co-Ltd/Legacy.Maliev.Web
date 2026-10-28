using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Components.Layout;

/// <summary>
/// Carries identity, language-selection, and antiforgery data for shared public navigation.
/// </summary>
/// <param name="SuppressIdentityNavigation">Whether account navigation is hidden for this rendering.</param>
/// <param name="IsAuthenticated">Whether the current request has an authenticated identity.</param>
/// <param name="DisplayName">The authenticated identity name, or null when unavailable.</param>
/// <param name="AntiforgeryFieldName">The form field name required by the language-selection POST.</param>
/// <param name="AntiforgeryToken">The request token paired with the stored antiforgery cookie.</param>
/// <param name="LanguageAction">The language-selection endpoint with the current document return path.</param>
/// <param name="Cultures">The configured UI cultures with the current culture marked as selected.</param>
public sealed record PublicNavigationDisplayModel(
    bool SuppressIdentityNavigation,
    bool IsAuthenticated,
    string? DisplayName,
    string AntiforgeryFieldName,
    string AntiforgeryToken,
    string LanguageAction,
    IReadOnlyList<PublicNavigationCultureOption> Cultures)
{
    /// <summary>
    /// Builds navigation state and obtains antiforgery tokens for the language-selection form.
    /// </summary>
    /// <param name="context">The current request containing identity, localization services, and document path.</param>
    /// <param name="suppressIdentityNavigation">Whether the rendered navigation should omit identity links.</param>
    /// <returns>Navigation state containing supported cultures and a protected language-selection action.</returns>
    public static PublicNavigationDisplayModel Create(HttpContext context, bool suppressIdentityNavigation)
    {
        ArgumentNullException.ThrowIfNull(context);

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        var localization = context.RequestServices
            .GetRequiredService<IOptions<RequestLocalizationOptions>>()
            .Value;
        var currentCulture = context.Features.Get<IRequestCultureFeature>()
            ?.RequestCulture.UICulture.Name ?? CultureInfo.CurrentUICulture.Name;
        var cultures = (localization.SupportedUICultures ?? Array.Empty<CultureInfo>())
            .Select(culture => new PublicNavigationCultureOption(
                culture.Name,
                culture.TwoLetterISOLanguageName.Equals("th", StringComparison.OrdinalIgnoreCase) ? "ไทย" : "English",
                string.Equals(culture.Name, currentCulture, StringComparison.OrdinalIgnoreCase)))
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

/// <summary>
/// Represents one configured language choice in the shared navigation form.
/// </summary>
/// <param name="Name">The culture name submitted when the visitor selects this language.</param>
/// <param name="Label">The user-facing English or Thai language label.</param>
/// <param name="IsSelected">Whether this option matches the current request UI culture.</param>
public sealed record PublicNavigationCultureOption(string Name, string Label, bool IsSelected);
