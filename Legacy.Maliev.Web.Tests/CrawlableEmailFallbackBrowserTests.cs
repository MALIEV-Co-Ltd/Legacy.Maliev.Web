using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class CrawlableEmailFallbackBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", "/legal", ".landing-footer-contact-row", "info@maliev.com")]
    [InlineData("th", "/legal", ".landing-footer-contact-row", "info@maliev.com")]
    [InlineData("en", "/legal/nondisclosureagreement", ".legal-document a", "nda@maliev.com")]
    [InlineData("th", "/legal/nondisclosureagreement", ".legal-document a", "nda@maliev.com")]
    [InlineData("en", "/contact", ".contact-method-card a", "info@maliev.com")]
    [InlineData("th", "/contact", ".contact-method-card a", "info@maliev.com")]
    public async Task NoJavascript_PublicEmailKeepsCrawlableContactFallback(
        string culture, string path, string selector, string recipient)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"{path}?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var anchor = page.Locator(selector).Filter(new() { HasText = recipient }).First;
        Assert.Equal(recipient, await anchor.InnerTextAsync());
        Assert.Equal("/contact#contact-us", await anchor.GetAttributeAsync("href"));
        Assert.Equal(recipient, await anchor.GetAttributeAsync("data-contact-email"));
        await anchor.ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/contact" && new Uri(url).Fragment == "#contact-us");
        Assert.Equal(1, await page.Locator("#contact-us").CountAsync());
    }

    [Theory]
    [InlineData("en", "info@maliev.com")]
    [InlineData("th", "info@maliev.com")]
    [InlineData("en", "manufacturing@maliev.com")]
    [InlineData("th", "nda@maliev.com")]
    [InlineData("en", "career@maliev.com")]
    [InlineData("th", "support@maliev.com")]
    public async Task RenderedContactScript_EnhancesKnownRecipientWithoutAnalyticsPii(string culture, string recipient)
    {
        var result = await ExecuteRenderedScript(culture, recipient);
        Assert.True(result.GetProperty("prevented").GetBoolean());
        Assert.Equal("mailto:" + recipient, result.GetProperty("destinations")[0].GetString());
        var diagnostic = Assert.Single(result.GetProperty("events").EnumerateArray());
        Assert.Equal("email_click", diagnostic.GetProperty("event").GetString());
        Assert.Equal("email", diagnostic.GetProperty("channel").GetString());
        Assert.Equal("business_email", diagnostic.GetProperty("destination").GetString());
        Assert.Equal("contact", diagnostic.GetProperty("context").GetString());
        Assert.Equal(new[] { "channel", "context", "destination", "event" },
            diagnostic.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(recipient, diagnostic.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.GetProperty("dataLayerWrites").GetInt32());
    }

    [Theory]
    [InlineData("unknown@example.test")]
    [InlineData("info@maliev.com?subject=private")]
    [InlineData("__proto__")]
    public async Task RenderedContactScript_UnknownMarkerLeavesFallbackUntouched(string marker)
    {
        var result = await ExecuteRenderedScript("en", marker);
        Assert.False(result.GetProperty("prevented").GetBoolean());
        Assert.Empty(result.GetProperty("destinations").EnumerateArray());
        Assert.Empty(result.GetProperty("events").EnumerateArray());
        Assert.Equal(0, result.GetProperty("dataLayerWrites").GetInt32());
    }

    [Theory]
    [InlineData(" INFO@MALIEV.COM ")]
    [InlineData("\tCareer@Maliev.Com\n")]
    public async Task RenderedContactScript_NormalizesKnownRecipient(string marker)
    {
        var result = await ExecuteRenderedScript("en", marker);
        Assert.True(result.GetProperty("prevented").GetBoolean());
        Assert.Equal("mailto:" + marker.Trim().ToLowerInvariant(),
            Assert.Single(result.GetProperty("destinations").EnumerateArray()).GetString());
        Assert.Single(result.GetProperty("events").EnumerateArray());
    }

    [Theory]
    [InlineData("ctrl")]
    [InlineData("meta")]
    [InlineData("shift")]
    [InlineData("alt")]
    [InlineData("middle")]
    [InlineData("prevented")]
    [InlineData("download")]
    public async Task RenderedContactScript_PreservesUnownedClick(string mode)
    {
        var result = await ExecuteRenderedScript("en", "info@maliev.com", mode);
        Assert.False(result.GetProperty("prevented").GetBoolean());
        Assert.Empty(result.GetProperty("destinations").EnumerateArray());
        Assert.Empty(result.GetProperty("events").EnumerateArray());
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("denied")]
    public async Task RenderedContactScript_AnalyticsUnavailableDoesNotBlockMailto(string mode)
    {
        var result = await ExecuteRenderedScript("en", "info@maliev.com", mode);
        Assert.True(result.GetProperty("prevented").GetBoolean());
        Assert.Equal("mailto:info@maliev.com",
            Assert.Single(result.GetProperty("destinations").EnumerateArray()).GetString());
        Assert.Empty(result.GetProperty("events").EnumerateArray());
        Assert.Equal(0, result.GetProperty("dataLayerWrites").GetInt32());
    }

    private async Task<JsonElement> ExecuteRenderedScript(string culture, string marker, string mode = "normal")
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var response = await context.APIRequest.GetAsync(new Uri(fixture.Origin, $"/contact?culture={culture}").ToString());
        Assert.True(response.Ok);
        var html = await response.TextAsync();
        var match = Regex.Match(html,
            "<script[^>]*data-migration-component=\"public-contact-channel-analytics\"[^>]*>(?<script>[\\s\\S]*?)</script>");
        Assert.True(match.Success);
        await using var page = await context.NewPageAsync();
        await page.SetContentAsync($"<base href='{fixture.Origin}contact'><a href='/contact#contact-us'>Public contact</a>");
        var json = await page.EvaluateAsync<string>("""
            argument => {
                const events = [], destinations = [], listeners = [];
                const anchor = document.querySelector('a');
                anchor.setAttribute('data-contact-email', argument.marker);
                if (argument.mode === 'download') anchor.setAttribute('download', '');
                const testDocument = {
                    baseURI: document.baseURI,
                    addEventListener: (name, callback) => {
                        if (name === 'click') listeners.push(callback);
                    }
                };
                let dataLayerWrites = 0, prevented = false;
                const testWindow = {
                    location: { hostname: new URL(document.baseURI).hostname,
                        pathname: '/contact', assign: target => destinations.push(target) },
                    setTimeout: callback => callback(),
                    malievAnalytics: { emit: value => events.push(value) },
                    dataLayer: { push: () => dataLayerWrites++ }
                };
                if (argument.mode === 'unavailable') delete testWindow.malievAnalytics;
                if (argument.mode === 'denied') testWindow.malievAnalytics.emit = () => false;
                new Function('window', 'document', argument.script)(testWindow, testDocument);
                if (listeners.length !== 1) throw new Error('Expected one rendered contact handler.');
                listeners[0]({ target: anchor, button: argument.mode === 'middle' ? 1 : 0,
                    defaultPrevented: argument.mode === 'prevented',
                    metaKey: argument.mode === 'meta', ctrlKey: argument.mode === 'ctrl',
                    shiftKey: argument.mode === 'shift', altKey: argument.mode === 'alt',
                    preventDefault: () => { prevented = true; } });
                return JSON.stringify({ events, destinations, prevented, dataLayerWrites });
            }
            """, new { marker, mode, script = match.Groups["script"].Value });
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
