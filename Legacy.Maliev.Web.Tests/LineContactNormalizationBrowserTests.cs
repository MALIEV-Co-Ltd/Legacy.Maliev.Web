using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class LineContactNormalizationBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("https://line.me/ti/p/@maliev")]
    [InlineData("https://line.me/ti/p/%40maliev")]
    [InlineData("https://line.me/ti/p/@@maliev")]
    [InlineData("https://line.me/ti/p/%40%40maliev")]
    [InlineData("https://www.line.me/TI/P/%40MaLiEv/?source=contact")]
    [InlineData("/contact/line")]
    public async Task RealClassifier_OwnedLineLinksEmitOnlyDiagnosticChannelPayload(string destination)
    {
        await using var context = await NewContext();
        await using var page = await context.NewPageAsync();
        await Load(page, "en");
        await page.Locator("#cookieConsent [data-consent-action='accept']").ClickAsync();
        await Click(page, destination);
        var events = await Events(page);
        AssertLineDiagnostic(events);
    }

    [Theory]
    [InlineData("https://line.me/ti/p/@someone_else")]
    [InlineData("https://line.me/ti/p/%40maliev/extra")]
    [InlineData("https://example.test/ti/p/@maliev")]
    [InlineData("https://line.me/ti/p/%zz")]
    [InlineData("https://line.me/ti/p/%40%4Daliev")]
    public async Task RealClassifier_UnownedOrMalformedLinksDoNotEmitLineDiagnostic(string destination)
    {
        await using var context = await NewContext();
        await using var page = await context.NewPageAsync();
        await Load(page, "en");
        await page.Locator("#cookieConsent [data-consent-action='accept']").ClickAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await Click(page, destination);
        Assert.Empty((await Events(page)).EnumerateArray());
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("en", "reject")]
    [InlineData("th", "reject")]
    [InlineData("en", "accept")]
    [InlineData("th", "accept")]
    public async Task RealClassifier_ConsentDecisionControlsQueuedLineClicksWithoutFriendOrLeadPromotion(
        string culture, string decision)
    {
        await using var context = await NewContext();
        await using var page = await context.NewPageAsync();
        await Load(page, culture);
        // Programmatic activation exercises the served listener while the native consent dialog
        // is modal. Playwright pointer input is deliberately reserved for the user's consent choice.
        await Click(page, "https://line.me/ti/p/%40maliev");
        Assert.Empty((await Events(page)).EnumerateArray());
        await page.Locator($"#cookieConsent [data-consent-action='{decision}']").ClickAsync();
        var events = await Events(page);
        Assert.Equal(decision == "accept" ? 1 : 0, events.GetArrayLength());
        if (decision == "accept")
        {
            AssertLineDiagnostic(events);
        }
        Assert.False(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => ['line_friend_confirmed', 'generate_lead'].includes(item.event))"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task LineFallbackButton_RetainsReadableContrastInEveryInteractionState(string culture)
    {
        await using var context = await NewContext();
        await using var page = await context.NewPageAsync();
        await Load(page, culture);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        var button = page.Locator("[data-contact-placement='line_bridge_fallback']");
        Assert.True(await button.IsVisibleAsync());
        Assert.True(await Contrast(button) >= 4.5);
        await button.HoverAsync();
        Assert.True(await Contrast(button) >= 4.5);
        await page.Mouse.MoveAsync(0, 0);
        await button.FocusAsync();
        Assert.True(await Contrast(button) >= 4.5);
    }

    private static Task<double> Contrast(ILocator button) => button.EvaluateAsync<double>("""
        element => {
            const style = getComputedStyle(element);
            const luminance = value => {
                const channels = value.match(/\d+/g).slice(0, 3).map(Number).map(channel => {
                    const normalized = channel / 255;
                    return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
                });
                return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
            };
            const foreground = luminance(style.color);
            const background = luminance(style.backgroundColor);
            return (Math.max(foreground, background) + 0.05) / (Math.min(foreground, background) + 0.05);
        }
        """);

    private async Task<IBrowserContext> NewContext()
    {
        var context = await fixture.Browser.NewContextAsync();
        try
        {
            await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
                == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
            return context;
        }
        catch (Exception original)
        {
            try { await context.DisposeAsync(); }
            catch (Exception cleanup) { OwnedWebSurfaceResources.RecordFailureTypes(original, [cleanup]); }
            throw;
        }
    }

    private async Task Load(IPage page, string culture)
    {
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/contact/line?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.WaitForFunctionAsync("() => document.body.dataset.lineBridgeReady === 'true'");
    }

    private static Task Click(IPage page, string destination) => page.EvaluateAsync("""
        destination => {
            const anchor = document.createElement('a');
            anchor.href = destination;
            anchor.target = '_blank';
            anchor.textContent = 'private@example.test';
            document.body.appendChild(anchor);
            anchor.click();
        }
        """, destination);

    private static void AssertLineDiagnostic(JsonElement events)
    {
        var item = Assert.Single(events.EnumerateArray());
        Assert.Equal("line_click", item.GetProperty("event").GetString());
        Assert.Equal("line", item.GetProperty("channel").GetString());
        Assert.Equal("line_oa", item.GetProperty("destination").GetString());
        Assert.Equal("contact", item.GetProperty("context").GetString());
        Assert.Equal(new[] { "channel", "context", "destination", "event" },
            item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("private@example.test", events.GetRawText(), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> Events(IPage page)
    {
        // Read browser-owned JSON directly; protocol reference metadata is not event payload.
        var json = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.dataLayer.filter(item => item.event === 'line_click'))");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
