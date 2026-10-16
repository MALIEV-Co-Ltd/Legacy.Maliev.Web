// <copyright file="KnowledgeCenterGuidesBrowserTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks the rendered reading and navigation experience in both public languages.</summary>
public sealed class KnowledgeCenterGuidesBrowserTests
{
    /// <summary>Every guide has readable actions, working section links, and contained mobile tables.</summary>
    /// <param name="culture">The requested public language.</param>
    /// <param name="width">The reading viewport width.</param>
    /// <returns>The browser verification task.</returns>
    [Theory]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    [InlineData("en", 375)]
    [InlineData("th", 375)]
    public async Task Guides_HaveReadableActionsAndResponsiveContent(string culture, int width)
    {
        await using var fixture = await PublicKnowledgeFixture.CreateAsync(width);
        IPage page = fixture.Page;
        string origin = fixture.Origin;
        foreach (string path in new[]
        {
            "/knowledges", "/knowledges/guidelines", "/knowledges/workflow", "/knowledges/specifications",
            "/knowledges/specifications/cnc-machining", "/knowledges/specifications/3d-printing", "/knowledges/specifications/3d-scanning",
        })
        {
            var response = await page.GotoAsync($"{origin}{path}?culture={culture}");
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            await RejectOptionalCookiesAsync(page);
            await page.EvaluateAsync("async () => { await document.fonts.ready; }");
            Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
            Assert.Equal(1, await page.Locator(".knowledge-document h1").CountAsync());
            Assert.Equal(1, await page.Locator("#knowledge-navigation [aria-current='page']").CountAsync());
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"), $"Page overflow: {path}, {culture}, {width}px");
            Assert.True(await page.EvaluateAsync<bool>(@"() => Array.from(document.querySelectorAll('.knowledge-document a[href^=""#""]'))
                .every(link => document.getElementById(decodeURIComponent(link.hash.slice(1))))"), $"Broken section link: {path}");

            foreach (ILocator action in await page.Locator(".knowledge-document a.maliev-button").AllAsync())
            {
                await AssertReadableActionAsync(action, path);
                await action.HoverAsync();
                await AssertReadableActionAsync(action, path + " hover");
                await page.Keyboard.PressAsync("Tab");
                await action.FocusAsync();
                await AssertReadableActionAsync(action, path + " keyboard focus");
            }

            Assert.True(await page.EvaluateAsync<bool>(@"() => Array.from(document.querySelectorAll('.docs-table-wrap'))
                .every(region => region.tabIndex === 0 && region.getAttribute('role') === 'region'
                    && !!region.getAttribute('aria-label') && region.getBoundingClientRect().right <= innerWidth + 1)"));
            if (path == "/knowledges")
            {
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                await SaveScreenshotAsync(page, $"knowledge-index-{culture}-{width}.png");
            }

            if (path.EndsWith("cnc-machining", StringComparison.Ordinal) && await page.Locator(".docs-figure").CountAsync() > 0)
            {
                foreach (ILocator image in await page.Locator(".docs-figure img").AllAsync())
                {
                    await image.ScrollIntoViewIfNeededAsync();
                    await image.EvaluateAsync("image => { image.loading = 'eager'; }");
                    await page.WaitForFunctionAsync("image => image.complete && image.naturalWidth > 0", await image.ElementHandleAsync());
                    Assert.True(await image.EvaluateAsync<bool>("async image => { await image.decode(); return image.naturalWidth > 0; }"));
                }

                await page.Locator("#radii-pockets .docs-figure").ScrollIntoViewIfNeededAsync();
                await SaveScreenshotAsync(page, $"knowledge-cnc-{culture}-{width}.png");
            }
        }
    }

    /// <summary>The mobile contents drawer traps focus, closes with Escape, and releases desktop controls.</summary>
    /// <returns>The browser verification task.</returns>
    [Fact]
    public async Task ContentsDrawer_RetainsKeyboardFocusAndResetsAtDesktopWidth()
    {
        await using var fixture = await PublicKnowledgeFixture.CreateAsync(375);
        IPage page = fixture.Page;
        string origin = fixture.Origin;
        await page.GotoAsync($"{origin}/knowledges?culture=en");
        await RejectOptionalCookiesAsync(page);
        ILocator open = page.Locator("[data-workspace-open]");
        ILocator close = page.Locator("[data-workspace-close]");
        Assert.True(await page.Locator("#knowledge-navigation").EvaluateAsync<bool>("element => element.inert"));
        await open.ClickAsync();
        Assert.Equal("true", await open.GetAttributeAsync("aria-expanded"));
        Assert.Equal("true", await page.Locator("#knowledge-navigation").GetAttributeAsync("aria-modal"));
        await page.WaitForFunctionAsync("() => document.activeElement === document.querySelector('[data-workspace-close]')", options: new PageWaitForFunctionOptions { Timeout = 3000 });
        string focusState = await close.EvaluateAsync<string>(@"element => JSON.stringify({
            active: document.activeElement?.outerHTML?.slice(0, 180), visibility: getComputedStyle(element).visibility,
            display: getComputedStyle(element).display, disabled: element.matches(':disabled'), tabIndex: element.tabIndex, modals: Array.from(document.querySelectorAll(':modal')).map(node => node.outerHTML.slice(0, 150)), ancestors: Array.from((function* () { let node=element;
                while(node) { yield {tag:node.tagName,id:node.id,inert:node.inert}; node=node.parentElement; } })()) })");
        Assert.True(await close.EvaluateAsync<bool>("element => document.activeElement === element"), focusState);
        Assert.True(await page.Locator("#knowledge-content").EvaluateAsync<bool>("element => element.inert"));
        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await page.Locator("#knowledge-navigation a").Last.EvaluateAsync<bool>("element => document.activeElement === element"));
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await close.EvaluateAsync<bool>("element => document.activeElement === element"));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("false", await open.GetAttributeAsync("aria-expanded"));
        Assert.True(await open.EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.False(await page.Locator("#knowledge-content").EvaluateAsync<bool>("element => element.inert"));
        await open.ClickAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('workspace-drawer-open')");
        Assert.False(await page.Locator("#knowledge-navigation").EvaluateAsync<bool>("element => element.inert"));
        Assert.False(await page.Locator("#knowledge-content").EvaluateAsync<bool>("element => element.inert"));
        Assert.True(await page.Locator("[data-workspace-backdrop]").EvaluateAsync<bool>("element => element.hidden"));
    }

    [Fact]
    public async Task SharedPublicArrowLinksKeepIconsOutsideTextUnderlines()
    {
        await using var fixture = await PublicKnowledgeFixture.CreateAsync(375);
        var count = 0;
        foreach (var route in new[] { "/", "/about", "/account/login", "/account/signup", "/contact", "/services" })
        {
            // This checks rendered CSS, not completion of unrelated page subresources.
            var response = await fixture.Page.GotoAsync(fixture.Origin + route + "?culture=en",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            Assert.Equal("en", await fixture.Page.Locator("html").GetAttributeAsync("lang"));
            await fixture.Page.WaitForFunctionAsync("""
                () => {
                    const styles = Array.from(document.querySelectorAll('link[rel="stylesheet"]'));
                    return styles.some(link => new URL(link.href).pathname === '/dist/site.min.css')
                        && styles.every(link => link.sheet !== null);
                }
                """);
            await RejectOptionalCookiesAsync(fixture.Page);
            foreach (var link in await fixture.Page.Locator("a:has([class*='fa-arrow-'], svg[data-icon^='arrow-'])").AllAsync())
            {
                if (!await link.IsVisibleAsync())
                {
                    continue;
                }
                count++;
                Assert.Equal("none", await link.EvaluateAsync<string>("element => getComputedStyle(element).textDecorationLine"));
                await link.HoverAsync();
                Assert.Equal("none", await link.EvaluateAsync<string>("element => getComputedStyle(element).textDecorationLine"));
                await link.FocusAsync();
                Assert.Equal("none", await link.EvaluateAsync<string>("element => getComputedStyle(element).textDecorationLine"));
            }
        }

        Assert.True(count > 0, "Existing shared public arrow links must be exercised.");
    }

    private static async Task AssertReadableActionAsync(ILocator action, string description)
    {
        var result = await action.EvaluateAsync<JsonElement>(@"element => {
            const style = getComputedStyle(element);
            const luminance = color => {
                const rgb = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(value => {
                    value /= 255;
                    return value <= 0.04045 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4);
                });
                return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
            };
            const foreground = luminance(style.color);
            const background = luminance(style.backgroundColor);
            return { contrast: (Math.max(foreground, background) + 0.05) / (Math.min(foreground, background) + 0.05),
                height: element.getBoundingClientRect().height, color: style.color, background: style.backgroundColor };
        }");
        Assert.True(result.GetProperty("contrast").GetDouble() >= 4.5, $"Unreadable action in {description}: {result.GetRawText()}");
        Assert.True(result.GetProperty("height").GetDouble() >= 43.5, $"Undersized action in {description}");
    }

    private static async Task SaveScreenshotAsync(IPage page, string name)
    {
        string directory = Path.GetFullPath(Path.Combine(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), "..", "Legacy.Maliev.Web.Tests",
            "TestResults", "knowledge-center-guides"));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(directory, name) });
        }
    }

    private static async Task RejectOptionalCookiesAsync(IPage page)
    {
        var reject = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await reject.IsVisibleAsync())
        {
            await reject.ClickAsync();
        }
    }

    // The shared-arrow check visits Contact for presentation only. Keep its
    // server-side country lookup deterministic, as the contact browser fixture does.
    private sealed class PublicKnowledgeTestingWebApplicationFactory(string contentRoot)
        : TestingWebApplicationFactory(contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        }
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)], true));
    }

    private sealed class PublicKnowledgeFixture : IAsyncDisposable
    {
        private readonly TestingWebApplicationFactory factory = new PublicKnowledgeTestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        private IPlaywright? playwright;
        private IBrowser? browser;
        public IBrowserContext Context { get; private set; } = null!;
        public IPage Page { get; private set; } = null!;
        public string Origin { get; private set; } = string.Empty;

        public static async Task<PublicKnowledgeFixture> CreateAsync(int width)
        {
            var fixture = new PublicKnowledgeFixture();
            try
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                listener.Start();
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                fixture.Origin = $"http://127.0.0.1:{port}";
                fixture.factory.UseKestrel(port);
                using var client = fixture.factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri(fixture.Origin)
                });
                fixture.playwright = await Playwright.CreateAsync();
                fixture.browser = await fixture.playwright.Chromium.LaunchAsync(new() { Headless = true });
                fixture.Context = await fixture.browser.NewContextAsync(new()
                {
                    ViewportSize = new() { Width = width, Height = 900 },
                    ReducedMotion = ReducedMotion.Reduce
                });
                await fixture.Context.RouteAsync("**/*", route =>
                    Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var uri) && uri.IsLoopback
                        ? route.ContinueAsync() : route.AbortAsync());
                fixture.Page = await fixture.Context.NewPageAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Context is not null) await Context.DisposeAsync();
            }
            finally
            {
                try
                {
                    if (browser is not null) await browser.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        playwright?.Dispose();
                    }
                    finally
                    {
                        await factory.DisposeAsync();
                    }
                }
            }
        }
    }
}
