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
    public async Task ServicesDirectory_UsesResponsiveFinderHeadingAndBreadcrumb(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        var intro = page.Locator(".service-finder-intro");
        var heading = intro.Locator("h1#service-finder-title");
        var breadcrumb = intro.Locator("nav.service-breadcrumb");
        Assert.Equal(1, await page.Locator("main h1").CountAsync());
        Assert.True(await heading.IsVisibleAsync());
        Assert.True(await breadcrumb.IsVisibleAsync());
        Assert.Equal(culture == "th" ? "ค้นหาบริการผลิตที่เหมาะกับโปรเจ็คของคุณ" : "Find the right manufacturing service for your project", await heading.InnerTextAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        var choice = page.Locator("[data-finder-step='0'] .service-finder-option").First;
        await choice.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("true", await choice.GetAttributeAsync("aria-pressed"));

        var screenshots = Path.Combine(AppContext.BaseDirectory, "services-directory-screenshots");
        Directory.CreateDirectory(screenshots);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(screenshots, $"services-{culture}-{width}.png") });
    }

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
    public async Task HomeSecondaryAction_PreservesHoverAndKeyboardFocusContrast(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/*", route => route.Request.Method == "GET" && new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.IsVisibleAsync()) await consent.ClickAsync();

        var button = page.Locator(".landing-cta-section .landing-button-light");
        await button.HoverAsync();
        await Assertions.Expect(button).ToHaveCSSAsync("background-color", "rgb(255, 255, 255)");
        await Assertions.Expect(button).ToHaveCSSAsync("border-top-color", "rgb(255, 255, 255)");
        await Assertions.Expect(button).ToHaveCSSAsync("color", "rgb(7, 24, 45)");
        await page.Mouse.MoveAsync(0, 0);
        await page.Locator(".landing-cta-section .landing-button-yellow").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(button).ToBeFocusedAsync();
        Assert.True(await button.EvaluateAsync<bool>("element => element.matches(':focus-visible')"));
        await Assertions.Expect(button).ToHaveCSSAsync("background-color", "rgb(255, 255, 255)");
        await Assertions.Expect(button).ToHaveCSSAsync("border-top-color", "rgb(255, 255, 255)");
        await Assertions.Expect(button).ToHaveCSSAsync("color", "rgb(7, 24, 45)");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task WithoutJavascript_AssemblyAndOrderedProcessRemainUsable(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await context.RouteAsync("**/*", route => route.Request.Method == "GET" && new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
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
        var preview = directoryLink.Locator(".landing-service-directory-preview");
        Assert.Equal("true", await preview.GetAttributeAsync("aria-hidden"));
        Assert.Equal(5, await preview.Locator(".landing-service-directory-progress > li").CountAsync());
        Assert.Equal(1, await preview.Locator(".landing-service-directory-progress > li.is-active").CountAsync());
        var progress = preview.Locator(".landing-service-directory-progress");
        await Assertions.Expect(progress).ToBeVisibleAsync();
        Assert.True(await progress.EvaluateAsync<bool>("""
            list => {
                const listBox = list.getBoundingClientRect();
                const style = getComputedStyle(list);
                const track = getComputedStyle(list, "::before");
                const steps = Array.from(list.children);
                const centers = steps.map(step => {
                    const box = step.getBoundingClientRect();
                    return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
                });
                const close = (a, b) => Math.abs(a - b) < 1;
                return listBox.width > 0 && style.display === "grid"
                    && style.gridTemplateColumns.split(/\s+/).length === 5
                    && track.content === '""' && track.position === "absolute" && parseFloat(track.height) === 1
                    && close(listBox.left + parseFloat(track.left), centers[0].x)
                    && close(listBox.right - parseFloat(track.right), centers[4].x)
                    && centers.every((center, i) => close(center.y, centers[0].y)
                        && close(center.x - centers[0].x, i * listBox.width / 5))
                    && steps.every(step =>
                        ["::before", "::after"].every(pseudo => getComputedStyle(step, pseudo).content === "none"));
            }
            """), "The rendered preview must have five aligned steps and a single track connecting their centers.");
        Assert.Equal(0, await preview.Locator("a, button, input, [data-service-finder]").CountAsync());
        var directoryHref = culture == "en" ? "/services?culture=en" : "/services";
        Assert.Equal(directoryHref, await directoryLink.GetAttributeAsync("href"));
        await directoryLink.ClickAsync();
        Assert.Equal(new Uri(fixture.Origin, directoryHref).ToString(), page.Url);
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        await Assertions.Expect(page.Locator("main[data-service-finder]")).ToBeVisibleAsync();
        Assert.Equal(culture == "th" ? "ค้นหาบริการผลิตที่เหมาะกับโปรเจ็คของคุณ" : "Find the right manufacturing service for your project", await page.Locator("main[data-service-finder] h1").InnerTextAsync());
    }
}
