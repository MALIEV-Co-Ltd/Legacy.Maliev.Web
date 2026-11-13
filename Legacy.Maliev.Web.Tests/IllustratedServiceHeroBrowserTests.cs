using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class IllustratedServiceHeroBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("/services/3d-printing", "printing/printing-hero-prototypes-transparent")]
    [InlineData("/services/3d-scanning", "hero-illustrations/scanning-hero-raptor-x-unbranded")]
    [InlineData("/services/cnc-machining", "hero-illustrations/cnc-hero-light")]
    [InlineData("/services/custom-manufacturing", "hero-illustrations/custom-hero-light")]
    [InlineData("/services/3d-design", "hero-illustrations/design-hero-light")]
    [InlineData("/services/finishing-and-color", "hero-illustrations/finishing-hero-light")]
    [InlineData("/services/low-volume-injection-molding", "hero-illustrations/injection-hero-light")]
    [InlineData("/services/silicone-casting", "hero-illustrations/silicone-hero-light")]
    public async Task Hero_RendersIllustrationAndKeepsControlsUsableOnMobileAndDesktop(string route, string stem)
    {
        var origin = fixture.Origin.ToString().TrimEnd('/');
        foreach (var culture in new[] { "en", "th" })
            foreach (var width in new[] { 390, 1440 })
            {
                await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
                {
                    Locale = culture == "th" ? "th-TH" : "en-US",
                    ViewportSize = new ViewportSize { Width = width, Height = 900 },
                    DeviceScaleFactor = 1,
                });
                await context.RouteAsync("**/*", async request =>
                {
                    if (request.Request.Url.StartsWith(origin + "/", StringComparison.Ordinal)) await request.ContinueAsync();
                    else await request.AbortAsync();
                });
                await using var page = await context.NewPageAsync();
                var response = await page.GotoAsync($"{origin}{route}?culture={culture}&tracking=excluded",
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
                Assert.NotNull(response);
                Assert.True(response.Ok);
                var consent = page.Locator("#cookieConsent");
                if (await consent.IsVisibleAsync()) await consent.Locator("[data-consent-action='reject']").ClickAsync();
                var image = page.Locator(".service-hero-media");
                await image.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
                await page.WaitForFunctionAsync("() => { const image = document.querySelector('.service-hero-media'); return image?.complete && image.naturalWidth > 0; }");
                Assert.Contains($"/src/images/services/{stem}", await image.EvaluateAsync<string>("image => image.currentSrc"), StringComparison.Ordinal);
                Assert.Equal("(max-width: 767px) 100vw, 50vw", await image.GetAttributeAsync("sizes"));
                Assert.False(string.IsNullOrWhiteSpace(await image.GetAttributeAsync("alt")));
                Assert.True(await page.Locator(".service-hero h1").IsVisibleAsync());
                var action = page.Locator(".service-hero .service-actions a").First;
                Assert.True(await action.IsVisibleAsync());
                await action.FocusAsync();
                Assert.True(await action.EvaluateAsync<bool>("element => element === document.activeElement"));
                await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('.service-hero-copy > *')).filter(e => e.getClientRects().length).every(e => Number(getComputedStyle(e).opacity) > .98)");
                var positions = await page.Locator(".service-hero").EvaluateAsync<double[]>("hero => { const a = hero.querySelector('.service-hero-media').getBoundingClientRect(); const b = hero.querySelector('.service-hero-copy').getBoundingClientRect(); return [a.left,a.top,a.right,a.bottom,b.left,b.top,b.right,b.bottom]; }");
                if (width == 390) Assert.True(positions[1] >= positions[7] - 1, $"{route} illustration overlaps mobile controls.");
                else Assert.True(positions[0] >= positions[4] + (positions[6] - positions[4]) * .42, $"{route} illustration overlaps desktop copy.");
                if (route == "/services/low-volume-injection-molding")
                {
                    Assert.Equal(width == 390 ? "flex" : "grid", await page.Locator(".service-hero").EvaluateAsync<string>("element => getComputedStyle(element).display"));
                    Assert.Equal(width == 390 ? "relative" : "absolute", await image.EvaluateAsync<string>("element => getComputedStyle(element).position"));
                    Assert.Equal("contain", await image.EvaluateAsync<string>("element => getComputedStyle(element).objectFit"));
                }
                var screenshots = Environment.GetEnvironmentVariable("MALIEV_WEB790_SCREENSHOTS");
                if (!string.IsNullOrWhiteSpace(screenshots))
                    await page.Locator(".service-hero").ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(screenshots, $"{route.Split('/').Last()}-{culture}-{width}.png") });
            }
    }
}
