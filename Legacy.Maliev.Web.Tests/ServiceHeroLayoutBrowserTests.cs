using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class ServiceHeroLayoutBrowserTests(PublicContactBrowserFixture fixture)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var route in new[] { "3d-design", "3d-printing", "3d-scanning", "cnc-machining", "custom-manufacturing", "finishing-and-color", "low-volume-injection-molding", "silicone-casting" })
            foreach (var culture in new[] { "en", "th" })
                foreach (var width in new[] { 390, 1440 })
                    yield return [route, culture, width];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Hero_RendersBreadcrumbQuickFactsAndNavigableContents(string route, string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/*", intercepted => new Uri(intercepted.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? intercepted.ContinueAsync() : intercepted.AbortAsync());
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services/{route}?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) await consent.ClickAsync();
        var hero = page.Locator("section.service-hero");
        Assert.Equal(1, await hero.CountAsync());
        Assert.True(await hero.Locator("nav.service-breadcrumb").IsVisibleAsync());
        Assert.Equal(1, await hero.Locator("nav.service-breadcrumb [aria-current='page']").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.True(await page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('script[type="application/ld+json"]')).some(element => {
                try { return JSON.parse(element.textContent)['@type'] === 'BreadcrumbList'; } catch { return false; }
            })
            """));
        var facts = hero.Locator(".service-quick article");
        Assert.Equal(route == "finishing-and-color" ? 0 : 4, await facts.CountAsync());
        for (var i = 0; i < await facts.CountAsync(); i++) Assert.True(await facts.Nth(i).IsVisibleAsync());
        Assert.Equal(await facts.CountAsync(), await facts.Locator("h2").CountAsync());
        var screenshots = Environment.GetEnvironmentVariable("MALIEV_SERVICE_LAYOUT_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(screenshots) && route is "3d-printing" or "low-volume-injection-molding")
        {
            await hero.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(screenshots, $"{route}-{culture}-{width}.png") });
        }
        var toc = page.Locator("nav[data-service-toc]");
        await Assertions.Expect(toc).ToBeVisibleAsync();
        Assert.True(await hero.EvaluateAsync<bool>("e => !!(e.compareDocumentPosition(document.querySelector('nav[data-service-toc]')) & Node.DOCUMENT_POSITION_FOLLOWING)"));
        await toc.Locator("[data-service-toc-toggle]").ClickAsync();
        var firstLink = toc.Locator("a[href^='#']").First;
        Assert.True(await firstLink.CountAsync() > 0);
        var target = await firstLink.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(target));
        Assert.Equal(0, await toc.Locator("a[href^='#']").EvaluateAllAsync<int>("links => links.filter(link => document.querySelector('.service-hero')?.contains(document.querySelector(link.getAttribute('href')))).length"));
        await firstLink.ClickAsync();
        Assert.Equal(target, new Uri(page.Url).Fragment);
        Assert.True(await page.Locator(target!).IsVisibleAsync());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Printing_WithoutJavascript_RetainsFallbackContentsLinks(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await context.RouteAsync("**/*", intercepted => new Uri(intercepted.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? intercepted.ContinueAsync() : intercepted.AbortAsync());
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var fallback = page.Locator("nav[data-service-page-toc]");
        Assert.True(await fallback.IsVisibleAsync());
        var link = fallback.Locator("a[href='#printing-processes']");
        Assert.True(await link.IsVisibleAsync());
        await link.ClickAsync();
        Assert.Equal("#printing-processes", new Uri(page.Url).Fragment);
        Assert.True(await page.Locator("#printing-processes").IsVisibleAsync());
    }
}
