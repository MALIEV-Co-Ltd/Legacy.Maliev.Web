using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicFooterSocialSourceParityHttpTests
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task SocialMediaEntryPoints_RenderSourceSupportedLocalizedFooter(string culture, bool enabled)
    {
        using var host = OwnedWebSurfaceHost.Create(builder =>
            builder.UseSetting("BlazorRouting:SocialMedia", enabled ? "true" : "false"));
        using var response = await host.Client.GetAsync($"/about/socialmedia?culture={culture}&tracking=excluded");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Equal(enabled, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));

        var footer = Regex.Match(html, "<footer\\b[^>]*data-migration-component=\"public-footer\"[^>]*>(.*?)</footer>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(footer.Success, "Expected the actual public footer in the HTTP response.");
        var social = Regex.Match(footer.Groups[1].Value, "<div\\b[^>]*data-migration-component=\"social-links\"[^>]*>(.*?)</div>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(social.Success, "Expected the footer's actual social-links component.");
        var anchors = Regex.Matches(social.Groups[1].Value, "<a\\b(?<attributes>[^>]*)>", RegexOptions.CultureInvariant);
        Assert.Equal(9, anchors.Count);
        var links = anchors.Cast<Match>().ToDictionary(
            anchor => Attribute(anchor.Groups["attributes"].Value, "href"),
            anchor => anchor.Groups["attributes"].Value,
            StringComparer.Ordinal);

        string[] destinations =
        [
            "https://www.youtube.com/channel/UCCosquPSUed6UPlMcRCq0Ig",
            "https://www.tiktok.com/@maliev.marketing",
            "https://www.instagram.com/maliev.manufacturing/",
            "https://www.threads.com/@maliev.manufacturing",
            "https://www.facebook.com/maliev.manufacturing/",
            "https://line.me/ti/p/@maliev",
            "https://m.me/maliev.manufacturing/",
            "https://wa.me/66898950690",
            "https://goo.gl/maps/AXqLnqUu5dM2",
        ];
        Assert.Equal(destinations.Order(StringComparer.Ordinal), links.Keys.Order(StringComparer.Ordinal));
        foreach (var attributes in links.Values)
        {
            Assert.Equal("_blank", Attribute(attributes, "target"));
            Assert.Contains("noopener", Attribute(attributes, "rel").Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Assert.False(string.IsNullOrWhiteSpace(Attribute(attributes, "aria-label")));
        }

        Assert.Equal("MALIEV on Facebook", Attribute(links[destinations[4]], "aria-label"));
        AssertContact(links[destinations[5]], culture == "th" ? "ติดต่อ MALIEV ผ่าน LINE Official Account" : "Contact MALIEV through LINE Official Account");
        AssertContact(links[destinations[6]], culture == "th" ? "ติดต่อ MALIEV ผ่าน Messenger" : "Contact MALIEV through Messenger");
        AssertContact(links[destinations[7]], culture == "th" ? "ติดต่อ MALIEV ผ่าน WhatsApp" : "Contact MALIEV on WhatsApp");
        Assert.Contains("noreferrer", Attribute(links[destinations[6]], "rel").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("whatsapp_business", Attribute(links[destinations[7]], "data-maliev-contact-destination"));
        Assert.Equal(1, links.Values.Count(attributes => Attribute(attributes, "data-maliev-contact-destination").Length > 0));
    }

    private static void AssertContact(string attributes, string label)
    {
        Assert.Equal(label, Attribute(attributes, "aria-label"));
        Assert.Equal("social", Attribute(attributes, "data-contact-placement"));
    }

    private static string Attribute(string attributes, string name) =>
        Regex.Match(attributes, $"(?:^|\\s){Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.CultureInvariant).Groups[1].Value;
}
