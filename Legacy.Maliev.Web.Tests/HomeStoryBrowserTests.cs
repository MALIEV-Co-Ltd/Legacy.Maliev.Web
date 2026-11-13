using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class HomeStoryBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", 390)]
    [InlineData("th", 390)]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    public async Task AssemblyDefault_IsResponsiveReadableAndKeyboardAccessible(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 1000 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/?culture={culture}&hero=partnership&preview=1").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }
        Assert.Equal(1, await page.Locator("h1").CountAsync());
        Assert.True(await page.Locator(".hero-assembly").IsVisibleAsync());
        Assert.Equal(0, await page.Locator("[data-home-hero-carousel], .hero-review-bar, .hero-partnership, .hero-journey").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        await page.Locator(".hero-assembly a").First.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.EvaluateAsync<bool>("document.querySelector('.hero-assembly').contains(document.activeElement)"));
        Assert.Equal(4, await page.Locator("#landing-process img.landing-step-photo").CountAsync());
        await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('.hero-assembly img')).every(image => image.complete && image.naturalWidth > 0)");
        Assert.True(await page.Locator(".hero-tech-leaders path").EvaluateAllAsync<bool>("paths => paths.length === 4 && paths.every(path => /^M[-.0-9 ]+ L[.0-9 ]+$/.test(path.getAttribute('d')))"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task WithoutJavascript_AssemblyAndOrderedProcessRemainUsable(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.True(await page.Locator(".hero-assembly").IsVisibleAsync());
        Assert.Equal(0, await page.Locator("[data-home-hero-carousel], .hero-partnership, .hero-journey").CountAsync());
        Assert.Equal(4, await page.Locator("#landing-process ol > li").CountAsync());
        Assert.Equal($"/Contact?culture={culture}", await page.Locator(".hero-assembly a").First.GetAttributeAsync("href"));
        var directoryLink = page.Locator(".landing-service-card--directory-link");
        var directoryHref = culture == "en" ? "/services?culture=en" : "/services";
        Assert.Equal(directoryHref, await directoryLink.GetAttributeAsync("href"));
        await directoryLink.ClickAsync();
        Assert.Equal(new Uri(fixture.Origin, directoryHref).ToString(), page.Url);
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        await Assertions.Expect(page.Locator("main[data-service-finder]")).ToBeVisibleAsync();
        Assert.Equal(culture == "th" ? "บริการผลิตชิ้นส่วน" : "Manufacturing services", await page.Locator("main[data-service-finder] h1").InnerTextAsync());
    }
}
