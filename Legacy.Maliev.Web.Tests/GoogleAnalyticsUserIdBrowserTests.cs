using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises revocation and ready-event cardinality in the actual served public analytics runtime.</summary>
/// <param name="fixture">The existing owned public-contact browser fixture.</param>
[Collection(PublicContactBrowserCollection.Name)]
public sealed class GoogleAnalyticsUserIdBrowserTests(PublicContactBrowserFixture fixture)
{
    /// <summary>Anonymous consent cannot invent identity, and revocation clears a previously set runtime identity without replay.</summary>
    /// <param name="culture">The real public document culture.</param>
    /// <returns>A task completing the actual consent-button and application-runtime assertions.</returns>
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ActualAnalyticsRuntime_RevocationClearsIdentityAndSuppressesLaterEvents(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await page.RouteAsync("https://www.googletagmanager.com/**", route => route.AbortAsync());
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/contact?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => item[0] === 'set' && item[1]?.user_id)"));
        Assert.Equal(0, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
        Assert.Equal(0, await ReadyCountAsync(page));
        await page.Locator("#cookieConsent [data-consent-action='accept']").ClickAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => item[0] === 'set' && item[1]?.user_id)"));
        Assert.Equal(1, await ReadyCountAsync(page));
        Assert.Equal(1, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
        // This synthetic runtime assignment tests clearing only; normal server identity is covered by the joined login test.
        await page.EvaluateAsync("""
            () => {
                window.gtag('set', { user_id: 'customer:1' });
                window.malievAnalytics.emit({ event: 'before_revocation' });
                window.malievAnalytics.setConsent('denied');
                window.malievAnalytics.emit({ event: 'after_revocation' });
                window.malievAnalytics.setConsent('granted');
                window.malievLoadGoogleTagManager();
            }
            """);
        Assert.True(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => item.event === 'before_revocation')"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => item.event === 'after_revocation')"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.dataLayer.filter(item => item[0] === 'set').at(-1)[1].user_id === null"));
        Assert.Equal(1, await ReadyCountAsync(page));
        Assert.Equal(1, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
    }

    private static Task<int> ReadyCountAsync(IPage page) => page.EvaluateAsync<int>(
        "() => window.dataLayer.filter(item => item.event === 'maliev_analytics_ready').length");
}
