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

            if (path.EndsWith("cnc-machining", StringComparison.Ordinal))
            {
                foreach (ILocator image in await page.Locator(".docs-figure img").AllAsync())
                {
                    await image.ScrollIntoViewIfNeededAsync();
                    await image.EvaluateAsync("image => { image.loading = 'eager'; }");
                    await page.WaitForFunctionAsync("image => image.complete && image.naturalWidth > 0", await image.ElementHandleAsync());
                    Assert.True(await image.EvaluateAsync<bool>("async image => { await image.decode(); return image.naturalWidth > 0; }"));
                }

                var diagram = page.Locator("#files .docs-figure.knowledge-diagram");
                Assert.Equal("/src/images/knowledge/visual-cnc-preparation.svg",
                    await diagram.Locator("img").GetAttributeAsync("src"));
                await diagram.ScrollIntoViewIfNeededAsync();
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
        var finalGroup = page.Locator("#knowledge-navigation details.knowledge-nav-group").Last;
        var finalSummary = finalGroup.Locator("summary");
        Assert.Equal("Finishing and inspection", (await finalSummary.InnerTextAsync()).Trim());
        Assert.False(await finalGroup.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await finalSummary.IsVisibleAsync());
        Assert.False(await finalGroup.Locator("a").Last.IsVisibleAsync());
        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await finalSummary.EvaluateAsync<bool>("element => document.activeElement === element"));
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


    /// <summary>Directory hit areas retain source spacing and native hover/focus without dividers.</summary>
    /// <param name="culture">The requested public language.</param>
    /// <param name="width">The actual browser viewport width.</param>
    /// <returns>The rendered source-spacing verification task.</returns>
    [Theory]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    [InlineData("en", 375)]
    [InlineData("th", 375)]
    public async Task KnowledgeDirectory_Source785SpacingPreservesWholeLinkHoverAndKeyboardFocus(string culture, int width)
    {
        await using var fixture = await PublicKnowledgeFixture.CreateAsync(width);
        IPage page = fixture.Page;
        var response = await page.GotoAsync($"{fixture.Origin}/knowledges?culture={culture}");
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await RejectOptionalCookiesAsync(page);
        await page.WaitForFunctionAsync("""
            () => {
                const styles = Array.from(document.querySelectorAll('link[rel="stylesheet"]'));
                return styles.some(link => new URL(link.href).pathname === '/dist/site.min.css')
                    && styles.every(link => link.sheet !== null);
            }
            """);
        await page.EvaluateAsync("async () => { await document.fonts.ready; }");
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        var lists = page.Locator(".knowledge-directory-list");
        Assert.Equal(5, await lists.CountAsync());
        foreach (var list in await lists.AllAsync())
        {
            var spacing = await list.EvaluateAsync<JsonElement>("""
                element => {
                    const rem = parseFloat(getComputedStyle(document.documentElement).fontSize);
                    const style = getComputedStyle(element);
                    return { rem, rowGap: parseFloat(style.rowGap), columnGap: parseFloat(style.columnGap) };
                }
                """);
            double rem = spacing.GetProperty("rem").GetDouble();
            Assert.Equal(.25 * rem, spacing.GetProperty("rowGap").GetDouble(), 3);
            Assert.Equal(1.75 * rem, spacing.GetProperty("columnGap").GetDouble(), 3);
        }

        var links = page.Locator(".knowledge-directory-list li > a");
        Assert.Equal(18, await links.CountAsync());
        foreach (var link in await links.AllAsync())
        {
            var size = await link.EvaluateAsync<JsonElement>("""
                element => {
                    const rem = parseFloat(getComputedStyle(document.documentElement).fontSize);
                    const style = getComputedStyle(element);
                    const row = getComputedStyle(element.parentElement);
                    return { rem, padding: [style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft].map(parseFloat),
                        divider: parseFloat(row.borderBottomWidth), height: element.getBoundingClientRect().height };
                }
                """);
            foreach (var padding in size.GetProperty("padding").EnumerateArray())
                Assert.Equal(.75 * size.GetProperty("rem").GetDouble(), padding.GetDouble(), 3);
            Assert.Equal(0, size.GetProperty("divider").GetDouble());
            Assert.True(size.GetProperty("height").GetDouble() >= 44);
        }

        var first = links.Nth(0);
        var second = links.Nth(1);
        await first.HoverAsync(new() { Position = new() { X = 3, Y = 3 } });
        Assert.Equal("rgb(240, 241, 243)",
            await first.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        await first.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await second.EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.Equal("rgb(240, 241, 243)",
            await second.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        Assert.Equal("2px", await second.EvaluateAsync<string>("element => getComputedStyle(element).outlineWidth"));
        Assert.Equal("solid", await second.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

        if (width < 1152)
            await page.Locator("[data-workspace-open]").ClickAsync();
        var search = page.Locator(".knowledge-search input");
        await search.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var searchSize = await search.EvaluateAsync<JsonElement>("""
            element => ({ font: parseFloat(getComputedStyle(element).fontSize),
                rem: parseFloat(getComputedStyle(document.documentElement).fontSize),
                desktop: matchMedia('(min-width: 72rem)').matches,
                height: element.getBoundingClientRect().height })
            """);
        Assert.Equal(width >= 1152, searchSize.GetProperty("desktop").GetBoolean());
        double scale = searchSize.GetProperty("desktop").GetBoolean() ? .875 : 1;
        Assert.Equal(scale * searchSize.GetProperty("rem").GetDouble(),
            searchSize.GetProperty("font").GetDouble(), 3);
        Assert.True(searchSize.GetProperty("height").GetDouble() >= 44);
    }


    /// <summary>Final source diagrams really decode and remain stacked/contained in both locales.</summary>
    /// <param name="culture">The requested public language.</param>
    /// <param name="width">The actual browser viewport width.</param>
    /// <returns>The responsive technical-figure verification task.</returns>
    [Theory]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    [InlineData("en", 375)]
    [InlineData("th", 375)]
    public async Task ProcessIllustrations_FinalSourceV3DecodesWithoutSupersededSideBySideLayout(string culture, int width)
    {
        await using var fixture = await PublicKnowledgeFixture.CreateAsync(width);
        IPage page = fixture.Page;
        var response = await page.GotoAsync(fixture.Origin + "/knowledges/manufacturing/process-selection?culture=" + culture);
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        await RejectOptionalCookiesAsync(page);
        await page.EvaluateAsync("async () => { await document.fonts.ready; }");
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        var figures = page.Locator(".knowledge-process-illustration");
        Assert.Equal(3, await figures.CountAsync());
        var selectedImages = new List<string>();
        foreach (var figure in await figures.AllAsync())
        {
            var image = figure.Locator("img");
            await image.ScrollIntoViewIfNeededAsync();
            await image.EvaluateAsync("image => { image.loading = 'eager'; }");
            await page.WaitForFunctionAsync("image => image.complete && image.naturalWidth > 0",
                await image.ElementHandleAsync());
            Assert.True(await image.EvaluateAsync<bool>("async image => { await image.decode(); return image.naturalWidth > 0; }"));
            var measured = await figure.EvaluateAsync<JsonElement>("""
                element => {
                    const viewport = element.querySelector('.knowledge-diagram-viewport');
                    const image = element.querySelector('img');
                    const caption = element.querySelector('figcaption');
                    const box = element.getBoundingClientRect();
                    const imageStyle = getComputedStyle(image);
                    return { columns: getComputedStyle(element).gridTemplateColumns.split(' ').length,
                        width: innerWidth, left: box.left, right: box.right,
                        imageBottom: viewport.getBoundingClientRect().bottom, captionTop: caption.getBoundingClientRect().top,
                        contained: viewport.scrollWidth <= viewport.clientWidth + 1,
                        fit: imageStyle.objectFit, aspect: imageStyle.aspectRatio,
                        naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight,
                        selected: new URL(image.currentSrc).pathname, full: element.querySelector('.knowledge-diagram-link').getAttribute('href') };
                }
                """);
            Assert.Equal(1, measured.GetProperty("columns").GetInt32());
            Assert.True(measured.GetProperty("left").GetDouble() >= 0);
            Assert.True(measured.GetProperty("right").GetDouble() <= measured.GetProperty("width").GetDouble() + 1);
            Assert.True(measured.GetProperty("captionTop").GetDouble() >= measured.GetProperty("imageBottom").GetDouble());
            Assert.True(measured.GetProperty("contained").GetBoolean());
            Assert.Equal("contain", measured.GetProperty("fit").GetString());
            Assert.Equal("3 / 2", measured.GetProperty("aspect").GetString());
            string full = measured.GetProperty("full").GetString()!;
            Assert.StartsWith("/src/images/knowledge/diagrams/process-", full);
            Assert.EndsWith("-v3.webp", full);
            string small = full.Replace(".webp", "-small.webp", StringComparison.Ordinal);
            string selected = measured.GetProperty("selected").GetString()!;
            Assert.Contains(selected, new[] { small, full });
            Assert.True(measured.GetProperty("naturalWidth").GetInt32() > 0);
            Assert.True(measured.GetProperty("naturalHeight").GetInt32() > 0);
            selectedImages.Add(selected);
        }


        string[] publicImages =
        [
            "/src/images/knowledge/diagrams/process-air-bending-v3-small.webp",
            "/src/images/knowledge/diagrams/process-air-bending-v3.webp",
            "/src/images/knowledge/diagrams/process-fdm-v3-small.webp",
            "/src/images/knowledge/diagrams/process-fdm-v3.webp",
            "/src/images/knowledge/diagrams/process-milling-v3-small.webp",
            "/src/images/knowledge/diagrams/process-milling-v3.webp",
            "/src/images/knowledge/photos/process-air-bending-v2-small.webp",
            "/src/images/knowledge/photos/process-air-bending-v2.webp",
            "/src/images/knowledge/photos/process-fdm-v2-small.webp",
            "/src/images/knowledge/photos/process-fdm-v2.webp",
            "/src/images/knowledge/photos/process-milling-v2-small.webp",
            "/src/images/knowledge/photos/process-milling-v2.webp",
        ];
        await using var decoded = await page.WaitForFunctionAsync("""
            async urls => Promise.all(urls.map(async url => {
                const response = await fetch(url, { signal: AbortSignal.timeout(10000) });
                if (!response.ok || response.headers.get('Content-Type')?.split(';')[0] !== 'image/webp')
                    throw new Error('technical-image-response');
                const bitmap = await createImageBitmap(await response.blob());
                try { return { url, width: bitmap.width, height: bitmap.height }; }
                finally { bitmap.close(); }
            }))
            """, publicImages, new() { Timeout = 30_000 });
        var receipts = await decoded.JsonValueAsync<JsonElement[]>();
        Assert.Equal(12, receipts.Length);
        Assert.Equal<string?>(publicImages.AsEnumerable(), receipts.Select(receipt => receipt.GetProperty("url").GetString()));
        Assert.Equal(3, selectedImages.Count);
        foreach (string selected in selectedImages)
        {
            // Width-descriptor images report density-corrected natural sizes; use the same URL's bitmap for raw pixels.
            var selectedReceipt = Assert.Single(receipts, receipt => receipt.GetProperty("url").GetString() == selected);
            bool small = selected.EndsWith("-small.webp", StringComparison.Ordinal);
            Assert.Equal(small ? 768 : 1536, selectedReceipt.GetProperty("width").GetInt32());
            Assert.Equal(small ? 512 : 1024, selectedReceipt.GetProperty("height").GetInt32());
        }
        foreach (var receipt in receipts)
        {
            bool small = receipt.GetProperty("url").GetString()!.EndsWith("-small.webp", StringComparison.Ordinal);
            Assert.Equal(small ? 768 : 1536, receipt.GetProperty("width").GetInt32());
            Assert.Equal(small ? 512 : 1024, receipt.GetProperty("height").GetInt32());
        }

        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
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
