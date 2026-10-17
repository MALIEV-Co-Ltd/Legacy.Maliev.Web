using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class SocialMediaContactBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", "accept")]
    [InlineData("th", "accept")]
    [InlineData("en", "reject")]
    [InlineData("th", "reject")]
    public async Task SocialCards_KeyboardActivationKeepsDiagnosticPayloadBehindConsent(string culture, string decision)
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/about/socialmedia?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.False(await page.EvaluateAsync<bool>(
            """() => !!document.querySelector('script[src*="googletagmanager.com/gtm.js"]')"""));
        Assert.Empty((await ContactEvents(page)).EnumerateArray());
        await page.Locator($"#cookieConsent [data-consent-action='{decision}']").ClickAsync();
        Assert.Equal(0, await page.Locator("#cookieConsent").CountAsync());

        var cards = page.Locator("a.social-link-card");
        Assert.Equal(8, await cards.CountAsync());
        var names = new[]
        {
            "YouTube", "TikTok", "Instagram", "Threads", "Facebook", "LINE", "WhatsApp", "Messenger",
        };
        await cards.First.FocusAsync();
        for (var index = 0; index < names.Length; index++)
        {
            var card = cards.Nth(index);
            Assert.True(await card.EvaluateAsync<bool>("element => document.activeElement === element"));
            Assert.Equal(names[index], await card.Locator("strong").InnerTextAsync());
            var description = await card.Locator("span").InnerTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(description));
            Assert.Equal(1, await page.GetByRole(AriaRole.Link, new()
            {
                NameRegex = new Regex("^" + Regex.Escape(names[index]) + @"\s*" + Regex.Escape(description) + "$"),
            }).CountAsync());
            if (index + 1 < names.Length)
            {
                await page.Keyboard.PressAsync("Tab");
            }
        }

        // Return to the real WhatsApp card and activate it through the keyboard.
        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await cards.Nth(6).EvaluateAsync<bool>("element => document.activeElement === element"));
        await using var popup = await page.RunAndWaitForPopupAsync(() => page.Keyboard.PressAsync("Enter"));
        var events = await ContactEvents(page);
        Assert.Equal(decision == "accept" ? 1 : 0, events.GetArrayLength());
        if (decision == "accept")
        {
            var item = Assert.Single(events.EnumerateArray());
            Assert.Equal("whatsapp_click", item.GetProperty("event").GetString());
            Assert.Equal("whatsapp", item.GetProperty("channel").GetString());
            Assert.Equal("whatsapp_business", item.GetProperty("destination").GetString());
            Assert.Equal("about", item.GetProperty("context").GetString());
            Assert.Equal(new[] { "channel", "context", "destination", "event" },
                item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.DoesNotContain("66898950690", events.GetRawText(), StringComparison.Ordinal);
        }
        Assert.False(await page.EvaluateAsync<bool>(
            "() => dataLayer.some(item => ['generate_lead', 'maliev_lead_submitted', 'purchase'].includes(item.event))"));
    }

    private static async Task<JsonElement> ContactEvents(IPage page)
    {
        var json = await page.EvaluateAsync<string>(
            "() => JSON.stringify(dataLayer.filter(item => /^(?:whatsapp|line|messenger)_click$/.test(item.event || '')))");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
