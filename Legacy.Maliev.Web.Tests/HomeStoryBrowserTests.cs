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
    public async Task ReducedMotion_PreservesReadableResponsiveScenesAndKeyboardLinks(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 1000 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent");
        await consent.Locator("[data-consent-action='reject']").ClickAsync();
        Assert.Equal(0, await consent.CountAsync());
        await page.WaitForFunctionAsync("() => document.querySelector('[data-home-hero-carousel]')?.dataset.carouselReady === 'true'");
        Assert.Equal(1, await page.Locator("h1").CountAsync());
        Assert.Equal(3, await page.Locator("[data-hero-scene]").CountAsync());
        Assert.True(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        Assert.False(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        Assert.False(await page.Locator("#home-hero-journey").IsVisibleAsync());
        Assert.Equal(1, await page.Locator("[data-hero-scene]").EvaluateAllAsync<int>("panels => panels.filter(panel => !panel.inert).length"));
        var toggle = page.Locator("[data-hero-toggle]");
        Assert.True(await toggle.IsDisabledAsync());
        Assert.Equal(culture == "th" ? "หยุดการเปลี่ยนสไลด์อัตโนมัติแล้ว" : "Automatic slides paused", await toggle.InnerTextAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        await page.Locator("#home-hero-assembly a").First.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.EvaluateAsync<bool>("document.querySelector('#home-hero-assembly').contains(document.activeElement)"));
        Assert.Equal(4, await page.Locator("#landing-process img.landing-step-photo").CountAsync());
        await page.WaitForFunctionAsync("() => Array.from(document.querySelectorAll('#home-hero-assembly img')).every(image => image.complete && image.naturalWidth > 0)");
        Assert.True(await page.Locator(".hero-tech-leaders path").EvaluateAllAsync<bool>("paths => paths.length === 4 && paths.every(path => /^M[-.0-9 ]+ L[.0-9 ]+$/.test(path.getAttribute('d')))"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ActualHomeBundle_UsesExactClockFocusPauseAndOwnedTeardown(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            ReducedMotion = ReducedMotion.NoPreference,
        });
        await context.AddInitScriptAsync("""
            (() => {
                const set = window.setTimeout.bind(window), clear = window.clearTimeout.bind(window);
                const timers = new Map(); let now = 0, next = -1;
                window.setTimeout = (callback, delay, ...args) => {
                    if (delay !== 8000) return set(callback, delay, ...args);
                    const id = next--; timers.set(id, { callback: () => callback(...args), due: now + delay }); return id;
                };
                window.clearTimeout = id => { if (!timers.delete(id)) clear(id); };
                window.__homeClock = {
                    count: () => timers.size,
                    advance: milliseconds => {
                        now += milliseconds;
                        for (const [id, item] of [...timers]) if (item.due <= now) { timers.delete(id); item.callback(); }
                    }
                };
            })();
            """);
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent");
        await consent.Locator("[data-consent-action='reject']").ClickAsync();
        Assert.Equal(0, await consent.CountAsync());
        await page.WaitForFunctionAsync("() => window.__homeClock.count() === 1 && document.querySelector('[data-home-hero-carousel]')?.dataset.carouselReady === 'true'");
        await page.EvaluateAsync("window.__homeClock.advance(7999)");
        Assert.True(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        await page.EvaluateAsync("window.__homeClock.advance(1)");
        await page.WaitForFunctionAsync("() => !document.querySelector('#home-hero-partnership').hidden && window.__homeClock.count() === 1");
        Assert.False(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        await page.Locator("#home-hero-partnership a").First.FocusAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        await page.EvaluateAsync("window.__homeClock.advance(16000)");
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        await page.Locator("#main-content").FocusAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        await page.EvaluateAsync("window.__homeClock.advance(7999)");
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        var toggle = page.Locator("[data-hero-toggle]");
        Assert.Equal(culture == "th" ? "หยุดเปลี่ยนสไลด์อัตโนมัติ" : "Pause automatic slides", await toggle.InnerTextAsync());
        await toggle.FocusAsync();
        await toggle.PressAsync("Enter");
        Assert.Equal(culture == "th" ? "เล่นสไลด์อัตโนมัติต่อ" : "Resume automatic slides", await toggle.InnerTextAsync());
        await page.Locator("#main-content").FocusAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        await page.EvaluateAsync("window.__homeClock.advance(24000)");
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        await toggle.FocusAsync();
        await toggle.PressAsync(" ");
        await page.Locator("#main-content").FocusAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        await page.EvaluateAsync("window.__homeClock.advance(7999)");
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        await page.EvaluateAsync("window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }))");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        Assert.Equal("stopped", await page.Locator("[data-home-hero-carousel]").GetAttributeAsync("data-carousel-ready"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task WithoutJavascript_FirstSceneAndOrderedProcessRemainUsable(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.True(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        Assert.False(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        Assert.False(await page.Locator("#home-hero-journey").IsVisibleAsync());
        Assert.False(await page.Locator("[data-hero-controls]").IsVisibleAsync());
        Assert.Equal(4, await page.Locator("#landing-process ol > li").CountAsync());
        Assert.Equal($"/Contact?culture={culture}", await page.Locator("#home-hero-assembly a").First.GetAttributeAsync("href"));
    }
}
