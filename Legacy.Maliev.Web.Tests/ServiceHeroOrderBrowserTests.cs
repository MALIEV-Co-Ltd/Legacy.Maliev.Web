using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class ServiceHeroOrderBrowserTests(PublicContactBrowserFixture fixture)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var route in ServiceHeroOrderStaticSsrTests.Cases())
        {
            foreach (var width in new[] { 390, 1440 })
            {
                yield return [route[0], route[1], route[2], route[3], route[4], width];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ServiceHero_VisibleActionsPrecedeVisibleDetails(
        string route, string primaryHref, string culture, string expectedDetail, bool hasQuickFacts, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 1000 },
        });
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/*", async intercepted =>
        {
            var target = new Uri(intercepted.Request.Url);
            if (target.Scheme == fixture.Origin.Scheme && target.Authority == fixture.Origin.Authority)
            {
                await intercepted.ContinueAsync();
            }
            else
            {
                await intercepted.AbortAsync();
            }
        });
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"{route}?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        Assert.Equal(0, await page.Locator("#cookieConsent").CountAsync());

        var hero = page.Locator("section.service-hero");
        var actions = hero.Locator(".service-actions");
        var details = hero.Locator(".service-hero-points");
        Assert.Equal(1, await hero.CountAsync());
        Assert.Equal(1, await actions.CountAsync());
        Assert.Equal(1, await details.CountAsync());
        Assert.True(await actions.IsVisibleAsync());
        Assert.True(await details.IsVisibleAsync());
        await page.WaitForFunctionAsync("""
            () => ['.service-actions', '.service-hero-points'].every(selector => {
                const element = document.querySelector(`section.service-hero ${selector}`);
                return element && Number(getComputedStyle(element).opacity) >= 0.99;
            })
            """, null, new PageWaitForFunctionOptions { Timeout = 30000 });
        Assert.True(await actions.EvaluateAsync<bool>("element => element.nextElementSibling?.classList.contains('service-hero-points') === true"));
        var primaryAction = actions.Locator("a").First;
        Assert.Equal(primaryHref, await primaryAction.GetAttributeAsync("href"));
        await primaryAction.FocusAsync();
        Assert.True(await primaryAction.EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.True(await details.Locator("li").CountAsync() >= 3);
        Assert.Contains(expectedDetail, await details.Locator("li").First.InnerTextAsync(), StringComparison.Ordinal);
        if (hasQuickFacts)
        {
            Assert.True(await details.EvaluateAsync<bool>("""
                element => {
                    const facts = document.querySelector('section.service-quick');
                    return facts && (element.compareDocumentPosition(facts) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
                }
                """));
        }
        var actionBox = await actions.BoundingBoxAsync();
        var detailsBox = await details.BoundingBoxAsync();
        Assert.NotNull(actionBox);
        Assert.NotNull(detailsBox);
        Assert.True(detailsBox.Y >= actionBox.Y + actionBox.Height - 1,
            $"{route} ({culture}, {width}px) must display the details below the actions.");

        var screenshotDirectory = Environment.GetEnvironmentVariable("MALIEV_WEB780_SCREENSHOT_DIR");
        if (route == "/services/3d-printing" && !string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            await page.WaitForFunctionAsync("() => document.fonts.status === 'loaded'", null,
                new PageWaitForFunctionOptions { Timeout = 30000 });
            await hero.ScreenshotAsync(new()
            {
                Path = Path.Combine(screenshotDirectory, $"web780-printing-hero-{culture}-{width}.png"),
            });
        }
    }
}
