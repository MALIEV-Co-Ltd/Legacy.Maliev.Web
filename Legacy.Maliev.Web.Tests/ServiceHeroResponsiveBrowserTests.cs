using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class ServiceHeroResponsiveBrowserTests(PublicContactBrowserFixture fixture)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var route in new[] { "3d-design", "3d-printing", "3d-scanning", "cnc-machining", "custom-manufacturing", "finishing-and-color", "low-volume-injection-molding", "silicone-casting" })
            foreach (var culture in new[] { "en", "th" })
                foreach (var viewport in new[] { (768, 600), (860, 900), (861, 600), (1440, 900), (1920, 600), (1920, 1080) })
                    yield return [route, culture, viewport.Item1, viewport.Item2];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Hero_UsesResponsiveGeometryWithoutClipping(string route, string culture, int width, int height)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/*", request => new Uri(request.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? request.ContinueAsync() : request.AbortAsync());
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services/{route}?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) await consent.ClickAsync();
        var hero = page.Locator("section.service-hero");
        var title = hero.Locator("h1");
        var action = hero.Locator(".service-actions a").First;
        var image = hero.Locator("img.service-hero-media");
        Assert.True(await title.IsVisibleAsync());
        Assert.True(await action.IsVisibleAsync());
        Assert.True(await image.IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        var minHeight = await hero.EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).minHeight)");
        Assert.InRange(minHeight, Math.Min(height - 72, 928) - 1, Math.Min(height - 72, 928) + 1);
        var copyWidth = await hero.Locator(".service-hero-copy").EvaluateAsync<double>("element => element.getBoundingClientRect().width");
        Assert.InRange(copyWidth, Math.Min(width * .92, width >= 1920 ? 1536 : 1344) - 2, Math.Min(width * .92, width >= 1920 ? 1536 : 1344) + 2);
        var imageMaxHeight = await image.EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).maxHeight)");
        var expectedMaxHeight = width <= 860 ? Math.Clamp(height * .34, 192, 368) : Math.Min(height * .75, 608);
        Assert.InRange(imageMaxHeight, expectedMaxHeight - 1, expectedMaxHeight + 1);
        var screenshots = Environment.GetEnvironmentVariable("MALIEV_RESPONSIVE_HERO_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(screenshots) && (route is "3d-printing" or "low-volume-injection-molding" or "silicone-casting") && (width is 768 or 1920))
            await hero.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"{route}-{culture}-{width}x{height}.png") });
        var quick = hero.Locator(".service-quick");
        if (await quick.CountAsync() > 0) await quick.ScrollIntoViewIfNeededAsync();
        var geometry = await hero.EvaluateAsync<string>("""
            element => {
                const image = element.querySelector('.service-hero-media').getBoundingClientRect();
                const blocks = [element.querySelector('h1'), element.querySelector('.service-actions'), element.querySelector('.service-quick')].filter(Boolean);
                return JSON.stringify(blocks.map(block => {
                    const r = block.getBoundingClientRect();
                    const overlap = !(r.right <= image.left + 1 || image.right <= r.left + 1 || r.bottom <= image.top + 1 || image.bottom <= r.top + 1);
                    const x = (Math.max(r.left, image.left, 0) + Math.min(r.right, image.right, innerWidth)) / 2;
                    const y = (Math.max(r.top, image.top, 0) + Math.min(r.bottom, image.bottom, innerHeight)) / 2;
                    const top = overlap ? document.elementFromPoint(x, y) : null;
                    const safe = !overlap || (block.classList.contains('service-quick') && top && block.contains(top));
                    return { block: block.className || block.tagName, safe, overlap, blockRect: r.toJSON(), imageRect: image.toJSON(), top: top?.outerHTML.slice(0, 200) };
                }));
            }
            """);
        Assert.DoesNotContain("\"safe\":false", geometry, StringComparison.Ordinal);
        await action.FocusAsync();
        Assert.True(await action.EvaluateAsync<bool>("element => document.activeElement === element"));
        if (route == "3d-printing")
        {
            Assert.Equal("rgb(244, 247, 251)", await page.Locator("main.service-page").EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
            Assert.Equal("rgb(244, 247, 251)", await page.Locator("nav[data-service-toc]").EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
            Assert.Equal("rgb(244, 247, 251)", await page.Locator("#printing-quote-guide").EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        }
    }
}
