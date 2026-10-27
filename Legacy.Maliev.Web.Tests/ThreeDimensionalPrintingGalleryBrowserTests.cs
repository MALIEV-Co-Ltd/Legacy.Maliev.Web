using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class ThreeDimensionalPrintingGalleryBrowserTests(PublicContactBrowserFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task PartGallery_RejectingOptionalCookiesAllowsExpansionAndLoadsDeferredImages()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, "/services/3d-printing?culture=en").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");

        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var toggle = page.Locator("[data-part-gallery-toggle]");
        var gallery = page.Locator("#printing-part-gallery-extra");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.False(await gallery.IsVisibleAsync());

        await toggle.ClickAsync();

        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await gallery.IsVisibleAsync());
        Assert.False(await toggle.IsVisibleAsync());
        Assert.Equal(12, await gallery.Locator("[data-part-gallery-extra]").CountAsync());
        Assert.Equal(0, await gallery.Locator("img[data-src], img[data-srcset]").CountAsync());
        Assert.Equal(0, await gallery.Locator("[data-part-gallery-extra][aria-hidden='true']").CountAsync());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ToleranceComparison_ReflowsAndKeepsDetailsAndTocKeyboardOperable(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = culture == "th" ? "th-TH" : "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(
            new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");

        var consent = page.Locator("#cookieConsent");
        if (await consent.IsVisibleAsync())
        {
            await consent.Locator("[data-consent-action='reject']").ClickAsync();
        }
        Assert.Equal(0, await consent.CountAsync());

        var section = page.Locator("#printing-tolerances");
        var grid = section.Locator(".service-tolerance-grid");
        await grid.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        Assert.Equal(4, await grid.Locator(":scope > article.service-tolerance-item").CountAsync());

        foreach (var (width, columns) in new[] { (1280, 4), (390, 2) })
        {
            await page.SetViewportSizeAsync(width, 900);
            Assert.Equal(columns, await grid.EvaluateAsync<int>(
                "element => getComputedStyle(element).gridTemplateColumns.split(' ').length"));
            var rectangles = await grid.EvaluateAsync<double[][]>(
                "grid => [grid, ...grid.querySelectorAll(':scope > article')].map(element => { const r = element.getBoundingClientRect(); return [r.left, r.right, r.top, r.bottom]; })");
            Assert.Equal(5, rectangles.Length);
            for (var index = 1; index < rectangles.Length; index++)
            {
                Assert.True(rectangles[index][1] > rectangles[index][0]);
                Assert.InRange(rectangles[index][0], rectangles[0][0] - 1, rectangles[0][1]);
                Assert.InRange(rectangles[index][1], rectangles[index][0], rectangles[0][1] + 1);
            }
            Assert.InRange(Math.Abs(rectangles[1][2] - rectangles[2][2]), 0, 1);
            if (columns == 4)
            {
                Assert.InRange(Math.Abs(rectangles[1][2] - rectangles[3][2]), 0, 1);
                Assert.InRange(Math.Abs(rectangles[1][2] - rectangles[4][2]), 0, 1);
            }
            else
            {
                Assert.InRange(Math.Abs(rectangles[3][2] - rectangles[4][2]), 0, 1);
                Assert.True(rectangles[3][2] >= rectangles[1][3] - 1);
            }
        }

        var details = section.Locator("details.service-tolerance-details");
        var summary = details.Locator("summary");
        await Assertions.Expect(summary).ToHaveTextAsync(culture == "th"
            ? "อะไรทำให้ค่าคลาดเคลื่อนและการประกอบเปลี่ยนไป?"
            : "What changes these ranges and part fit?");
        Assert.Null(await details.GetAttributeAsync("open"));
        Assert.False(await details.Locator("div > p").First.IsVisibleAsync());
        await summary.FocusAsync();
        Assert.True(await summary.EvaluateAsync<bool>("element => element === document.activeElement"));
        await summary.PressAsync("Enter");
        Assert.NotNull(await details.GetAttributeAsync("open"));
        Assert.True(await details.Locator("div > p").First.IsVisibleAsync());
        await summary.PressAsync("Space");
        Assert.Null(await details.GetAttributeAsync("open"));
        Assert.False(await details.Locator("div > p").First.IsVisibleAsync());

        var toc = page.Locator("nav[data-service-toc]");
        var toggle = toc.Locator("[data-service-toc-toggle]");
        await toggle.ClickAsync();
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        await Assertions.Expect(toc.Locator("[data-service-toc-list] a:focus")).ToHaveCountAsync(1);
        var link = toc.Locator("a[href='#printing-tolerances']");
        await Assertions.Expect(link).ToHaveCountAsync(1);
        await link.PressAsync("Enter");
        Assert.EndsWith("#printing-tolerances", page.Url, StringComparison.Ordinal);
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.Equal("true", await link.GetAttributeAsync("aria-current"));
        try
        {
            await Assertions.Expect(section).ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions
            {
                Timeout = 30000
            });
        }
        catch (PlaywrightException failure)
        {
            // Observe the failed native fragment navigation without scrolling or changing focus.
            try
            {
                failure.Data["ToleranceTocGeometry"] = await section.EvaluateAsync<string>("""
                    section => {
                        const toc = document.querySelector('nav[data-service-toc]');
                        const active = document.activeElement;
                        const rect = element => {
                            const r = element?.getBoundingClientRect();
                            return r ? [r.left, r.top, r.right, r.bottom] : null;
                        };
                        return JSON.stringify({
                            viewport: [window.innerWidth, window.innerHeight],
                            scroll: [window.scrollX, window.scrollY],
                            section: rect(section),
                            toc: rect(toc),
                            tocLink: rect(toc?.querySelector("a[href='#printing-tolerances']")),
                            readyState: document.readyState,
                            panelOpen: toc?.dataset.tocOpen === 'true',
                            targetHash: window.location.hash === '#printing-tolerances',
                            targetFocused: active === section,
                            tocLinkFocused: active === toc?.querySelector("a[href='#printing-tolerances']"),
                            scrollMarginTop: getComputedStyle(section).scrollMarginTop,
                            parentOverflow: getComputedStyle(section.parentElement).overflow
                        });
                    }
                    """);
                output.WriteLine("Tolerance TOC geometry: {0}", failure.Data["ToleranceTocGeometry"]);
            }
            catch (Exception diagnosticFailure)
            {
                output.WriteLine("Tolerance TOC geometry unavailable: {0}", diagnosticFailure.GetType().Name);
            }

            throw;
        }
    }
    [Theory]
    [InlineData("en", false, false)]
    [InlineData("en", false, true)]
    [InlineData("en", true, false)]
    [InlineData("en", true, true)]
    [InlineData("th", false, false)]
    [InlineData("th", false, true)]
    [InlineData("th", true, false)]
    [InlineData("th", true, true)]
    public async Task TocActivation_FocusesThePhysicalDestinationAndPreservesNativeHistory(
        string culture, bool reducedMotion, bool keyboard)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = culture == "th" ? "th-TH" : "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 900 },
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(
            new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent");
        if (await consent.IsVisibleAsync())
        {
            await consent.Locator("[data-consent-action='reject']").ClickAsync();
        }

        var toc = page.Locator("nav[data-service-toc]");
        var toggle = toc.Locator("[data-service-toc-toggle]");
        await toggle.ClickAsync();
        var link = toc.Locator("a[href='#printing-tolerances']");
        await Assertions.Expect(link).ToBeVisibleAsync();
        var beforeUrl = page.Url;
        var historyLength = await page.EvaluateAsync<int>("history.length");
        if (keyboard)
        {
            await link.PressAsync("Enter");
        }
        else
        {
            await link.ClickAsync();
        }

        var destination = page.Locator("#printing-tolerances");
        await Assertions.Expect(destination).ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions
        {
            Timeout = 30000,
        });
        await Assertions.Expect(destination).ToBeFocusedAsync();
        Assert.Equal("-1", await destination.GetAttributeAsync("tabindex"));
        Assert.Equal(beforeUrl + "#printing-tolerances", page.Url);
        Assert.Equal(historyLength + 1, await page.EvaluateAsync<int>("history.length"));
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.Equal("true", await link.GetAttributeAsync("aria-current"));

        await page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page).ToHaveURLAsync(beforeUrl);
        await page.GoForwardAsync(new PageGoForwardOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page).ToHaveURLAsync(beforeUrl + "#printing-tolerances");
        await Assertions.Expect(destination).ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions
        {
            Timeout = 30000,
        });
    }

    [Theory]
    [InlineData("3d-printing", true, "en")]
    [InlineData("3d-printing", true, "th")]
    [InlineData("3d-printing", false, "en")]
    [InlineData("3d-printing", false, "th")]
    [InlineData("3d-scanning", true, "en")]
    [InlineData("3d-scanning", true, "th")]
    [InlineData("3d-scanning", false, "en")]
    [InlineData("3d-scanning", false, "th")]
    [InlineData("cnc-machining", true, "en")]
    [InlineData("cnc-machining", true, "th")]
    [InlineData("cnc-machining", false, "en")]
    [InlineData("cnc-machining", false, "th")]
    [InlineData("custom-manufacturing", true, "en")]
    [InlineData("custom-manufacturing", true, "th")]
    [InlineData("custom-manufacturing", false, "en")]
    [InlineData("custom-manufacturing", false, "th")]
    [InlineData("3d-design", true, "en")]
    [InlineData("3d-design", true, "th")]
    [InlineData("finishing-and-color", true, "en")]
    [InlineData("finishing-and-color", true, "th")]
    [InlineData("low-volume-injection-molding", true, "en")]
    [InlineData("low-volume-injection-molding", true, "th")]
    [InlineData("silicone-casting", true, "en")]
    [InlineData("silicone-casting", true, "th")]
    public async Task TocCompleteSectionInventory_EverySourceSectionRemainsReachable(
        string service, bool servicesEnabled, string culture)
    {
        await using var sourceFactory = new TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        await using var routeFactory = sourceFactory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Services", servicesEnabled ? "true" : "false"));
        var origin = new Uri($"http://127.0.0.1:{BrowserTestServerPort.Reserve()}");
        routeFactory.UseKestrel(origin.Port);
        using var client = routeFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = culture == "th" ? "th-TH" : "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await using var page = await context.NewPageAsync();
        var route = new Uri(origin, $"/services/{service}?culture={culture}").ToString();
        var response = await page.GotoAsync(route,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var main = page.Locator("main.service-page");
        await Assertions.Expect(main).ToHaveCountAsync(1);
        Assert.Equal(servicesEnabled,
            await main.GetAttributeAsync("data-migration-route-owner") == "blazor-static-ssr");
        var consent = page.Locator("#cookieConsent");
        if (await consent.IsVisibleAsync())
        {
            await consent.Locator("[data-consent-action='reject']").ClickAsync();
        }

        var toc = main.Locator("nav[data-service-toc]");
        var toggle = toc.Locator("[data-service-toc-toggle]");
        await Assertions.Expect(toggle).ToBeVisibleAsync();
        var expected = new List<(ILocator Destination, string Id, string Heading)>();
        // Source 1498292f enumerates direct main sections, excluding quick/CTA wrappers and blank h2s.
        foreach (var section in await main.Locator(":scope > section:not(.service-quick):not(.service-cta-wrap)").AllAsync())
        {
            var heading = section.Locator("h2").First;
            if (await heading.CountAsync() == 0)
            {
                continue;
            }

            var text = (await heading.TextContentAsync())?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var sectionId = await section.GetAttributeAsync("id");
            var destination = string.IsNullOrEmpty(sectionId) ? heading : section;
            var id = await destination.GetAttributeAsync("id");
            Assert.False(string.IsNullOrEmpty(id));
            expected.Add((destination, id!, text));
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, expected.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        var links = toc.Locator("[data-service-toc-list] > li > a");
        await Assertions.Expect(links).ToHaveCountAsync(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var destination = expected[index];
            var link = links.Nth(index);
            Assert.Equal("#" + destination.Id, await link.GetAttributeAsync("href"));
            Assert.Equal(destination.Heading, (await link.TextContentAsync())?.Trim());
            await toggle.ClickAsync();
            Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
            await Assertions.Expect(link).ToBeVisibleAsync();
            await link.ScrollIntoViewIfNeededAsync();
            await link.PressAsync("Enter");
            await Assertions.Expect(page).ToHaveURLAsync(route + "#" + destination.Id);
            await Assertions.Expect(destination.Destination).ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions
            {
                Timeout = 30000,
            });
            await Assertions.Expect(destination.Destination).ToBeFocusedAsync();
            Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
            Assert.Equal("true", await link.GetAttributeAsync("aria-current"));
            await Assertions.Expect(toc.Locator("[data-service-toc-current]")).ToHaveTextAsync(destination.Heading);
            await using var headingVisible = await page.WaitForFunctionAsync("""
                id => {
                    const main = document.querySelector('main.service-page');
                    const target = Array.from(main?.querySelectorAll('[id]') || []).find(element => element.id === id);
                    const heading = target?.matches('h2') ? target : target?.querySelector('h2');
                    const toc = main?.querySelector('nav[data-service-toc]');
                    if (!heading || !toc || toc.dataset.tocOpen === 'true') return false;
                    const rect = heading.getBoundingClientRect();
                    const tocBottom = Math.max(0, toc.getBoundingClientRect().bottom);
                    return rect.width > 0 && rect.height > 0
                        && rect.left >= -1 && rect.right <= window.innerWidth + 1
                        && rect.top >= tocBottom - 1 && rect.bottom <= window.innerHeight + 1;
                }
                """, destination.Id, new PageWaitForFunctionOptions { Timeout = 30000 });
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task TocModifiedActivation_OpensTheNativeDestinationWithoutChangingTheCurrentDocument(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = culture == "th" ? "th-TH" : "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 900 },
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(
            new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent");
        if (await consent.IsVisibleAsync())
        {
            await consent.Locator("[data-consent-action='reject']").ClickAsync();
        }

        var toc = page.Locator("nav[data-service-toc]");
        var toggle = toc.Locator("[data-service-toc-toggle]");
        await toggle.ClickAsync();
        var link = toc.Locator("a[href='#printing-tolerances']");
        await Assertions.Expect(link).ToBeVisibleAsync();
        var beforeUrl = page.Url;
        var beforeScroll = await page.EvaluateAsync<double>("scrollY");
        var beforeHistory = await page.EvaluateAsync<int>("history.length");
        var popupTask = context.WaitForPageAsync(new BrowserContextWaitForPageOptions { Timeout = 30000 });
        await link.ClickAsync(new LocatorClickOptions { Modifiers = [KeyboardModifier.Control] });
        var popup = await popupTask;
        try
        {
            await Assertions.Expect(popup).ToHaveURLAsync(beforeUrl + "#printing-tolerances");
            Assert.Equal(beforeUrl, page.Url);
            Assert.Equal(beforeHistory, await page.EvaluateAsync<int>("history.length"));
            Assert.Equal(beforeScroll, await page.EvaluateAsync<double>("scrollY"));
            Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        }
        finally
        {
            await popup.CloseAsync();
        }
    }

}
