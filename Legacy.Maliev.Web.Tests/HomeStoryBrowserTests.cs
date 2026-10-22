using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class HomeStoryBrowserTests(PublicContactBrowserFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
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
        var readinessClock = System.Diagnostics.Stopwatch.StartNew();
        float RemainingReadinessMilliseconds() => (float)Math.Max(1, 30000 - readinessClock.Elapsed.TotalMilliseconds);
        await page.WaitForFunctionAsync("() => document.querySelector('[data-home-hero-carousel]')?.dataset.carouselReady === 'true'",
            null, new() { Timeout = RemainingReadinessMilliseconds() });
        // A viewport coordinate can move under the hero as the document scrolls.
        // Use the existing fixed skip link through real focus/hover, never click it.
        async Task ParkPointerOutsideHeroAsync(System.Diagnostics.Stopwatch? sharedBudget = null)
        {
            var budget = sharedBudget ?? System.Diagnostics.Stopwatch.StartNew();
            float RemainingMilliseconds()
            {
                var remaining = 30000 - budget.Elapsed.TotalMilliseconds;
                if (remaining <= 0)
                    throw new TimeoutException("Home outside-focus precondition exceeded its 30-second budget.");
                return (float)remaining;
            }

            var parking = page.Locator(".maliev-skip-link[href='#main-content']");
            await parking.FocusAsync(new() { Timeout = RemainingMilliseconds() });
            await parking.HoverAsync(new() { Timeout = RemainingMilliseconds() });
            await Assertions.Expect(parking).ToBeVisibleAsync(new() { Timeout = RemainingMilliseconds() });
            await Assertions.Expect(parking).ToBeFocusedAsync(new() { Timeout = RemainingMilliseconds() });
            await page.WaitForFunctionAsync("""
                () => {
                    const parking = document.querySelector(".maliev-skip-link[href='#main-content']");
                    const root = document.querySelector('[data-home-hero-carousel]');
                    if (!parking || !root) return false;
                    const box = parking.getBoundingClientRect();
                    const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
                    return document.activeElement === parking && parking.matches(':hover')
                        && parking.contains(hit) && !root.contains(hit)
                        && !root.matches(':hover') && !root.contains(document.activeElement);
                }
                """, null, new() { Timeout = RemainingMilliseconds() });
        }

        await ParkPointerOutsideHeroAsync(readinessClock);
        Assert.True(await page.EvaluateAsync<bool>("!document.hidden"));
        Assert.True(await page.Locator("[data-home-hero-carousel]").EvaluateAsync<bool>(
            """
            root => {
                const parking = document.querySelector(".maliev-skip-link[href='#main-content']");
                const box = parking.getBoundingClientRect();
                const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
                return parking.contains(hit) && !root.contains(hit)
                    && !root.matches(':hover') && !root.contains(document.activeElement);
            }
            """,
            null, new() { Timeout = RemainingReadinessMilliseconds() }));
        await page.WaitForFunctionAsync("() => window.__homeClock.count() === 1 && document.querySelector('[data-home-hero-carousel]')?.dataset.carouselReady === 'true'",
            null, new() { Timeout = RemainingReadinessMilliseconds() });
        await page.EvaluateAsync("window.__homeClock.advance(7999)");
        Assert.True(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        await page.EvaluateAsync("window.__homeClock.advance(1)");
        var diagnostics = new HomeClockDiagnosticCapture();
        EventHandler<IConsoleMessage> capture = (_, message) => diagnostics.Capture(message.Type, message.Text);
        page.Console += capture;
        try
        {
            await page.WaitForFunctionAsync("""
                () => {
                    const root = document.querySelector('[data-home-hero-carousel]');
                    const clock = window.__homeClock;
                    const box = root?.getBoundingClientRect();
                    const animations = document.getAnimations();
                    const state = {
                        rootPresent: !!root,
                        ready: root?.dataset.carouselReady === 'true',
                        stopped: root?.dataset.carouselReady === 'stopped',
                        documentHidden: document.hidden,
                        rootVisible: !!box && box.width > 0 && box.height > 0 && box.bottom > 0 && box.right > 0 && box.top < innerHeight && box.left < innerWidth,
                        rootHidden: root?.hidden === true,
                        focus: root?.contains(document.activeElement) === true,
                        hover: root?.matches(':hover') === true,
                        reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches,
                        assemblyHidden: document.querySelector('#home-hero-assembly')?.hidden === true,
                        partnershipHidden: document.querySelector('#home-hero-partnership')?.hidden === true,
                        journeyHidden: document.querySelector('#home-hero-journey')?.hidden === true,
                        timers: clock.count(),
                        scenes: root?.querySelectorAll('[data-hero-scene]').length ?? 0,
                        animationsRunning: animations.filter(animation => animation.playState === 'running').length,
                        animationsPaused: animations.filter(animation => animation.playState === 'paused').length,
                        animationsFinished: animations.filter(animation => animation.playState === 'finished').length,
                        animationsIdle: animations.filter(animation => animation.playState === 'idle').length,
                        animationsPending: animations.filter(animation => animation.pending).length
                    };
                    const serialized = JSON.stringify(state);
                    if (clock.diagnosticLast !== serialized && (clock.diagnosticCount ?? 0) < 32) {
                        clock.diagnosticLast = serialized;
                        clock.diagnosticCount = (clock.diagnosticCount ?? 0) + 1;
                        console.debug('HOME_CLOCK_STATE_V1 ' + serialized);
                    }
                    return !document.querySelector('#home-hero-partnership').hidden && window.__homeClock.count() === 1;
                }
                """);
        }
        catch
        {
            try { diagnostics.WriteFailure(output); }
            catch (Exception) { /* Diagnostic output must not replace the primary wait failure. */ }
            throw;
        }
        finally
        {
            page.Console -= capture;
        }
        Assert.False(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        await page.Locator("#home-hero-partnership a").First.FocusAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        await page.EvaluateAsync("window.__homeClock.advance(16000)");
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        await ParkPointerOutsideHeroAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        await page.EvaluateAsync("window.__homeClock.advance(7999)");
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        var toggle = page.Locator("[data-hero-toggle]");
        Assert.Equal(culture == "th" ? "หยุดเปลี่ยนสไลด์อัตโนมัติ" : "Pause automatic slides", await toggle.InnerTextAsync());
        await toggle.FocusAsync();
        await toggle.PressAsync("Enter");
        Assert.Equal(culture == "th" ? "เล่นสไลด์อัตโนมัติต่อ" : "Resume automatic slides", await toggle.InnerTextAsync());
        await ParkPointerOutsideHeroAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeClock.count()"));
        await page.EvaluateAsync("window.__homeClock.advance(24000)");
        Assert.True(await page.Locator("#home-hero-partnership").IsVisibleAsync());
        await toggle.FocusAsync();
        await toggle.PressAsync(" ");
        await ParkPointerOutsideHeroAsync();
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
    public async Task ActualHomeBundle_RevealsDecodedAssemblyBeforeItsFullReadableDwell(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            ReducedMotion = ReducedMotion.NoPreference,
        });
        await context.AddInitScriptAsync("""
            (() => {
                const nativeSet = window.setTimeout.bind(window), nativeClear = window.clearTimeout.bind(window);
                const timers = new Map(); let now = 0, next = -1;
                window.setTimeout = (callback, delay, ...args) => {
                    if (delay !== 8000) return nativeSet(callback, delay, ...args);
                    const id = next--; timers.set(id, { callback: () => callback(...args), due: now + delay }); return id;
                };
                window.clearTimeout = id => { if (!timers.delete(id)) nativeClear(id); };
                const complete = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'complete');
                const nativeDecode = HTMLImageElement.prototype.decode;
                const nativeAnimate = Element.prototype.animate;
                const calls = [];
                let held = true, releaseDecode;
                const target = image => image.getAttribute('src') === '/src/images/landing/profile-hybrid-gripper-cutout.webp';
                Object.defineProperty(HTMLImageElement.prototype, 'complete', {
                    configurable: true,
                    get() { return target(this) && held ? false : complete.get.call(this); }
                });
                HTMLImageElement.prototype.decode = function () {
                    if (!target(this) || !held) return nativeDecode.call(this);
                    return new Promise((resolve, reject) => {
                        releaseDecode = () => { held = false; nativeDecode.call(this).then(resolve, reject); };
                    });
                };
                Element.prototype.animate = function (frames, timing) {
                    const animation = nativeAnimate.call(this, frames, timing);
                    if (this.closest('[data-home-hero-carousel]')) calls.push({ element: this, animation });
                    return animation;
                };
                window.__homeReveal = {
                    calls, ready: () => typeof releaseDecode === 'function',
                    release: () => releaseDecode(),
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
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        var parking = page.Locator(".maliev-skip-link[href='#main-content']");
        await parking.FocusAsync();
        await parking.HoverAsync();
        await page.WaitForFunctionAsync("""
            () => window.__homeReveal.ready()
                && document.querySelector('[data-home-hero-carousel]')?.dataset.carouselReady === 'true'
                && !document.querySelector('[data-home-hero-carousel]').matches(':hover')
                && !document.querySelector('[data-home-hero-carousel]').contains(document.activeElement)
                && document.querySelector('#home-hero-assembly img').naturalWidth > 0
            """, null, new() { Timeout = 30000 });
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeReveal.count()"));
        Assert.True(await page.EvaluateAsync<bool>("window.__homeReveal.calls.length > 0 && window.__homeReveal.calls.every(call => call.animation.playState === 'paused')"));
        await page.EvaluateAsync("window.__homeReveal.advance(24000)");
        Assert.True(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeReveal.count()"));
        Assert.True(await page.EvaluateAsync<bool>("""
            (() => {
                const calls = window.__homeReveal.calls;
                const timing = selector => calls.find(call => call.element.matches(selector))?.animation.effect.getTiming();
                if (timing('.hero-assembly-annotated > img')?.duration !== 600) return false;
                return ['fdm', 'resin', 'nylon', 'cnc'].every((key, i) => {
                    const line = timing('[data-tech-leader="' + key + '"]');
                    const label = timing('.hero-tech-' + key), point = timing('[data-tech-point="' + key + '"]');
                    return line?.delay === 650 + i * 360 && line.duration === 280
                        && label?.delay === line.delay + 120 && label.duration === 200
                        && point?.delay === line.delay + 220 && point.duration === 100;
                }) && timing('.hero-material-summary')?.delay === 2100;
            })()
            """));
        await page.EvaluateAsync("window.__homeReveal.release()");
        await page.WaitForFunctionAsync("() => window.__homeReveal.count() === 1", null, new() { Timeout = 30000 });
        Assert.True(await page.Locator("#home-hero-assembly").EvaluateAsync<bool>("""
            panel => ['fdm', 'resin', 'nylon', 'cnc'].every(key =>
                [panel.querySelector('.hero-tech-' + key), panel.querySelector('[data-tech-point="' + key + '"]'),
                 panel.querySelector('[data-tech-leader="' + key + '"]')].every(element => getComputedStyle(element).opacity === '1'))
            """));
        await page.EvaluateAsync("window.__homeReveal.advance(7999)");
        Assert.True(await page.Locator("#home-hero-assembly").IsVisibleAsync());
        await page.EvaluateAsync("window.__homeReveal.advance(1)");
        await page.WaitForFunctionAsync("() => document.querySelector('#home-hero-partnership')?.hidden === false",
            null, new() { Timeout = 30000 });
        await page.EvaluateAsync("window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }))");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__homeReveal.count()"));
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
