using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

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
        await AssertFinalCustomerRosterAsync(page, culture);
        await Expect(page.Locator("[data-capability-carousel]")).ToHaveAttributeAsync("data-carousel-state", "ready");
        await Expect(page.Locator("[data-workpiece-gallery]")).ToHaveAttributeAsync("data-workpiece-ready", "true");
        await Expect(page.Locator("main.profile-about")).ToHaveAttributeAsync("data-presentation-state", "reduced");
        await DismissConsentAsync(page);
        Assert.True(await page.Locator("main.profile-about img").EvaluateAllAsync<bool>("""
            async images => {
                if (images.length === 0) return false;
                for (const image of images) image.loading = 'eager';
                const decoded = await Promise.all(images.map(async image => {
                    await image.decode();
                    return image.complete && image.naturalWidth > 0 && image.naturalHeight > 0;
                }));
                return decoded.every(Boolean);
            }
            """).WaitAsync(TimeSpan.FromSeconds(30)));

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
        await Expect(page.Locator("[data-capability-index='0']")).ToBeFocusedAsync();
        Assert.StartsWith("1 / 6", await page.Locator("[data-capability-status]").InnerTextAsync(), StringComparison.Ordinal);

        var lastWorkpiece = page.Locator("[data-workpiece-index='3']");
        await lastWorkpiece.FocusAsync();
        await Expect(lastWorkpiece).ToBeFocusedAsync();
        await lastWorkpiece.PressAsync("Enter");
        await Expect(page.Locator("#workpiece-photo-3")).ToHaveJSPropertyAsync("hidden", false);
        await Expect(page.Locator("#workpiece-photo-0")).ToHaveJSPropertyAsync("hidden", true);
        Assert.Equal("true", await lastWorkpiece.GetAttributeAsync("aria-pressed"));
        Assert.Equal("workpiece-photo-3", await lastWorkpiece.GetAttributeAsync("aria-controls"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
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
        await AssertFinalCustomerRosterAsync(page, culture);
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
                const publishCount = () => document.documentElement?.setAttribute('data-about-test-timer-count', String(timers.size));
                window.setTimeout = (callback, delay, ...args) => {
                    if (delay !== 6000) return set(callback, delay, ...args);
                    const id = next--; timers.set(id, { callback: () => callback(...args), due: now + delay }); publishCount(); return id;
                };
                window.clearTimeout = id => { if (!timers.delete(id)) clear(id); publishCount(); };
                window.__aboutClock = {
                    count: () => timers.size,
                    advance: milliseconds => {
                        now += milliseconds;
                        for (const [id, item] of [...timers]) if (item.due <= now) { timers.delete(id); item.callback(); }
                        publishCount();
                    }
                };
            })();
            """);
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/About?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await AssertFinalCustomerRosterAsync(page, culture);
        await Expect(page.Locator("[data-workpiece-gallery]")).ToHaveAttributeAsync("data-workpiece-ready", "true");
        await Expect(page.Locator("main.profile-about")).ToHaveAttributeAsync("data-presentation-state", "ready");
        await DismissConsentAsync(page);
        var gallery = page.Locator("[data-workpiece-gallery]");
        await gallery.ScrollIntoViewIfNeededAsync();
        var outside = await gallery.EvaluateAsync<int[]>("gallery => { const r = gallery.getBoundingClientRect(); const corners = [[1, 1], [innerWidth - 1, 1], [1, innerHeight - 1], [innerWidth - 1, innerHeight - 1]]; return corners.find(([x, y]) => x < r.left || x >= r.right || y < r.top || y >= r.bottom) ?? []; }");
        Assert.Equal(2, outside.Length);
        await page.Mouse.MoveAsync(outside[0], outside[1]);
        Assert.False(await gallery.EvaluateAsync<bool>(
            "gallery => gallery.matches(':hover') || gallery.contains(document.activeElement)"));
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-about-test-timer-count", "1");
        await page.EvaluateAsync("() => window.__aboutClock.advance(5999)");
        Assert.True(await page.Locator("#workpiece-photo-0").IsVisibleAsync());
        await page.EvaluateAsync("() => window.__aboutClock.advance(1)");
        await Expect(page.Locator("#workpiece-photo-1")).ToHaveJSPropertyAsync("hidden", false);
        await Expect(page.Locator("#workpiece-photo-0")).ToHaveJSPropertyAsync("hidden", true);
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-about-test-timer-count", "1");
        Assert.Equal("true", await page.Locator("[data-workpiece-index='1']").GetAttributeAsync("aria-pressed"));

        var toggle = page.Locator("[data-workpiece-toggle]");
        await toggle.FocusAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__aboutClock.count()"));
        await toggle.PressAsync("Enter");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__aboutClock.count()"));
        await toggle.PressAsync("Enter");
        Assert.Equal("true", await toggle.GetAttributeAsync("data-paused"));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__aboutClock.count()"));
        await page.EvaluateAsync("() => window.__aboutClock.advance(12000)");
        Assert.True(await page.Locator("#workpiece-photo-1").IsVisibleAsync());
        await page.EvaluateAsync("() => document.dispatchEvent(new Event('visibilitychange'))");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__aboutClock.count()"));
        await toggle.PressAsync("Enter");
        Assert.Equal("false", await toggle.GetAttributeAsync("data-paused"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__aboutClock.count()"));
        Assert.Equal("static", await page.Locator("main.profile-about").GetAttributeAsync("data-presentation-state"));
        await page.EvaluateAsync("() => window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }))");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.__aboutClock.count()"));
        Assert.Equal("stopped", await page.Locator("[data-workpiece-gallery]").GetAttributeAsync("data-workpiece-ready"));
    }

    private static async Task AssertFinalCustomerRosterAsync(IPage page, string culture)
    {
        string[] expected = [
            "ptt", "pttep", "bangchak", "siemens-mobility", "mitsubishi-materials", "fabrinet",
            "minor-food", "kohler", "grohe", "murata", "olympus", "air-liquide", "lixil", "kobelco",
            "siam-motors", "mektec", "thai-asahi-denso", "yg-1", "sp-syndicate", "singha-kameda",
            "aeon-fantasy", "lenso", "mfec", "thai-coconut", "khon-kaen-sugar", "bem", "crown-seal",
            "thai-optical", "kumwell", "tpi-polene-power",
            "alten-korea", "forth", "do-day-dream", "thai-agri-foods",
            "sahamitr-pressure-container", "crown-ceramics"
        ];
        Assert.Equal(expected, await page.Locator("[data-customer]").EvaluateAllAsync<string[]>(
            "cards => cards.map(card => card.dataset.customer)"));
        Assert.Equal(0, await page.Locator("[data-customer='index-creative-village']").CountAsync());
        var crown = page.Locator("[data-customer='crown-ceramics']");
        await Expect(crown).ToContainTextAsync(culture == "th" ? "คราวน์ เซรามิคส์" : "Crown Ceramics");
        var image = crown.Locator("img");
        foreach (var (name, value) in new[]
        {
            ("src", "/src/images/customers/crown-ceramics.webp"),
            ("width", "234"),
            ("height", "119"),
            ("loading", "lazy"), ("decoding", "async"), ("alt", "")
        })
            await Expect(image).ToHaveAttributeAsync(name, value);
        foreach (var slug in new[] { "sahamitr-pressure-container", "crown-ceramics" })
            await Expect(page.Locator($"[data-customer='{slug}'] > .profile-customer-logo"))
                .ToHaveAttributeAsync("class", "profile-customer-logo");
    }

    private static async Task DismissConsentAsync(IPage page)
    {
        var dialog = page.Locator("#cookieConsent");
        await dialog.Locator("[data-consent-action='reject']").ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
    }
}
