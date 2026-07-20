using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.Career;

internal static partial class CareerOfferPresentation
{
    private static readonly DateTime LocalAspireCreatedDate =
        new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    public static bool IsVisibleOpenOffer(CareerOffer offer, bool hideLocalAspireFixture) =>
        offer.IsFilled == false
        && (!hideLocalAspireFixture || !IsLocalAspireFixture(offer));

    public static bool IsLocalAspireFixture(CareerOffer offer) =>
        offer.IsFilled == false
        && offer.CreatedDate == LocalAspireCreatedDate
        && string.Equals(offer.Title, "Local Manufacturing Engineer", StringComparison.Ordinal)
        && string.Equals(offer.Introduction, "Local Aspire career boundary verification", StringComparison.Ordinal)
        && string.Equals(offer.Description, "Support digital manufacturing projects.", StringComparison.Ordinal)
        && string.Equals(offer.Prerequisites, "Manufacturing experience", StringComparison.Ordinal)
        && string.Equals(offer.WhatWeOffer, "Independent engineering work", StringComparison.Ordinal)
        && string.Equals(offer.Location, "Nonthaburi", StringComparison.Ordinal);

    public static string ToSafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutExecutableContent = ScriptOrStyleBlock().Replace(value, string.Empty);
        var withLineBreaks = BlockBoundary().Replace(withoutExecutableContent, Environment.NewLine);
        var withoutTags = HtmlTag().Replace(withLineBreaks, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = decoded
            .Split('\n')
            .Select(static line => Whitespace().Replace(line, " ").Trim())
            .Where(static line => line.Length > 0);
        return string.Join(Environment.NewLine, lines);
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptOrStyleBlock();

    [GeneratedRegex(@"<br\s*/?>|</(?:p|div|li|ul|ol|h[1-6])\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockBoundary();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"[\t\f\v ]+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
