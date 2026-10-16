using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class AboutStoryBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", 390)]
    [InlineData("th", 390)]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    public async Task ReducedMotion_KeepsSixCapabilitiesAndFourWorkpiecesKeyboardOperable(
        string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 1000 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/About?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.WaitForFunctionAsync("""
            document.querySelector('[data-capability-carousel]')?.dataset.carouselState === 'ready'
            && document.querySelector('[data-workpiece-gallery]')?.dataset.workpieceReady === 'true'
            && document.querySelector('main.profile-about')?.dataset.presentationState === 'reduced'
            """);

        Assert.Equal(6, await page.Locator(".profile-capability-choices button[data-capability-index]").CountAsync());
        Assert.Equal(6, await page.Locator(".profile-capability-slide").CountAsync());
        Assert.Equal(4, await page.Locator("[data-workpiece-index]").CountAsync());
        Assert.Equal(4, await page.Locator("[data-workpiece-photo]").CountAsync());
        Assert.True(await page.Locator("[data-workpiece-toggle]").IsDisabledAsync());
        Assert.True(await page.Locator("#workpiece-photo-0").IsVisibleAsync());
        Assert.False(await page.Locator("#workpiece-photo-3").IsVisibleAsync());

        var lastCapability = page.Locator("[data-capability-index='5']");
        await lastCapability.ClickAsync();
        Assert.Equal("true", await lastCapability.GetAttributeAsync("aria-pressed"));
        await lastCapability.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        Assert.Equal("true", await page.Locator("[data-capability-index='0']").GetAttributeAsync("aria-pressed"));
        Assert.True(await page.Locator("[data-capability-index='0']").EvaluateAsync<bool>(
            "button => document.activeElement === button"));
        Assert.StartsWith("1 / 6", await page.Locator("[data-capability-status]").InnerTextAsync(), StringComparison.Ordinal);

        var lastWorkpiece = page.Locator("[data-workpiece-index='3']");
        await lastWorkpiece.FocusAsync();
        Assert.True(await lastWorkpiece.EvaluateAsync<bool>(
            "button => document.activeElement === button"));
        await lastWorkpiece.PressAsync("Enter");
        await page.WaitForFunctionAsync("""
            !document.querySelector('#workpiece-photo-3').hidden
            && document.querySelector('#workpiece-photo-0').hidden
            """);
        Assert.Equal("true", await lastWorkpiece.GetAttributeAsync("aria-pressed"));
        Assert.Equal("workpiece-photo-3", await lastWorkpiece.GetAttributeAsync("aria-controls"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task WithoutJavascript_AboutStoryRemainsReadableWithStaticNativeControls(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/About?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.True(await page.Locator("main.profile-about").IsVisibleAsync());
        Assert.Equal(6, await page.Locator(".profile-capability-slide").CountAsync());
        Assert.False(await page.Locator("[data-capability-controls]").IsVisibleAsync());
        Assert.False(await page.Locator("[data-workpiece-controls]").IsVisibleAsync());
        Assert.True(await page.Locator("#workpiece-photo-0").IsVisibleAsync());
        Assert.False(await page.Locator("#workpiece-photo-1").IsVisibleAsync());
        Assert.Equal(4, await page.Locator(".profile-rapid-workflow-grid > article").CountAsync());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ActualAboutBundle_RotatesAfterSixSecondsAndPreservesExplicitPause(string culture)
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
                    if (delay !== 6000) return set(callback, delay, ...args);
                    const id = next--; timers.set(id, { callback: () => callback(...args), due: now + delay }); return id;
                };
                window.clearTimeout = id => { if (!timers.delete(id)) clear(id); };
                window.__aboutClock = {
                    count: () => timers.size,
                    advance: milliseconds => {
                        now += milliseconds;
                        for (const [id, item] of [...timers]) if (item.due <= now) { timers.delete(id); item.callback(); }
                    }
                };
            })();
            """);
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/About?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.WaitForFunctionAsync("""
            document.querySelector('[data-workpiece-gallery]')?.dataset.workpieceReady === 'true'
            && document.querySelector('main.profile-about')?.dataset.presentationState === 'ready'
            """);
        var gallery = page.Locator("[data-workpiece-gallery]");
        await gallery.ScrollIntoViewIfNeededAsync();
        var outside = await gallery.EvaluateAsync<int[]>("gallery => { const r = gallery.getBoundingClientRect(); const corners = [[1, 1], [innerWidth - 1, 1], [1, innerHeight - 1], [innerWidth - 1, innerHeight - 1]]; return corners.find(([x, y]) => x < r.left || x >= r.right || y < r.top || y >= r.bottom) ?? []; }");
        Assert.Equal(2, outside.Length);
        await page.Mouse.MoveAsync(outside[0], outside[1]);
        Assert.False(await gallery.EvaluateAsync<bool>(
            "gallery => gallery.matches(':hover') || gallery.contains(document.activeElement)"));
        await page.WaitForFunctionAsync("window.__aboutClock.count() === 1");
        await page.EvaluateAsync("window.__aboutClock.advance(5999)");
        Assert.True(await page.Locator("#workpiece-photo-0").IsVisibleAsync());
        await page.EvaluateAsync("window.__aboutClock.advance(1)");
        await page.WaitForFunctionAsync("""
            !document.querySelector('#workpiece-photo-1').hidden
            && document.querySelector('#workpiece-photo-0').hidden
            && window.__aboutClock.count() === 1
            """);
        Assert.Equal("true", await page.Locator("[data-workpiece-index='1']").GetAttributeAsync("aria-pressed"));

        var toggle = page.Locator("[data-workpiece-toggle]");
        await toggle.FocusAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__aboutClock.count()"));
        await toggle.PressAsync("Enter");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__aboutClock.count()"));
        await toggle.PressAsync("Enter");
        Assert.Equal("true", await toggle.GetAttributeAsync("data-paused"));
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__aboutClock.count()"));
        await page.EvaluateAsync("window.__aboutClock.advance(12000)");
        Assert.True(await page.Locator("#workpiece-photo-1").IsVisibleAsync());
        await page.EvaluateAsync("document.dispatchEvent(new Event('visibilitychange'))");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__aboutClock.count()"));
        await toggle.PressAsync("Enter");
        Assert.Equal("false", await toggle.GetAttributeAsync("data-paused"));
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__aboutClock.count()"));
        Assert.Equal("static", await page.Locator("main.profile-about").GetAttributeAsync("data-presentation-state"));
        await page.EvaluateAsync("window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }))");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__aboutClock.count()"));
        Assert.Equal("stopped", await page.Locator("[data-workpiece-gallery]").GetAttributeAsync("data-workpiece-ready"));
    }
}
