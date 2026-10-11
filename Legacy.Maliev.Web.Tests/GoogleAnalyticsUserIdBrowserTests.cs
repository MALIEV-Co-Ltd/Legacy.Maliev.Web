using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class GoogleAnalyticsUserIdBrowserTests(PublicContactBrowserFixture fixture)
{
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
        Assert.False(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => item[0] === 'set' && item[1]?.user_id)"));
        await page.Locator("#cookieConsent [data-consent-action='accept']").ClickAsync();
        await page.EvaluateAsync("""
            () => {
                window.gtag('set', { user_id: 'customer:1' });
                window.malievAnalytics.emit({ event: 'before_revocation' });
                window.malievAnalytics.setConsent('denied');
                window.malievAnalytics.emit({ event: 'after_revocation' });
            }
            """);
        Assert.True(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => item.event === 'before_revocation')"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.dataLayer.some(item => item.event === 'after_revocation')"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.dataLayer.filter(item => item[0] === 'set').at(-1)[1].user_id === null"));
    }
}
