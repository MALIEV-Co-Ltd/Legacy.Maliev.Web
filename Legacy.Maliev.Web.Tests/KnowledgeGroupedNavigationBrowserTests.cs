using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class KnowledgeGroupedNavigationBrowserTests(CncNativeBrowserFixture fixture)
{
    /// <summary>The mobile contents drawer traps focus, closes with Escape, and releases desktop controls.</summary>
    /// <returns>The browser verification task.</returns>
    [Fact]
    public async Task ContentsDrawer_RetainsKeyboardFocusAndResetsAtDesktopWidth()
    {
        await using IBrowserContext context = await this.CreateContextAsync(375);
        IPage page = await context.NewPageAsync();
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync($"{origin}/knowledges?culture=en");
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.IsVisibleAsync()) await consent.ClickAsync();
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
        string navigationFocus = await page.EvaluateAsync<string>(@"() => JSON.stringify({ active: document.activeElement?.outerHTML,
            controls: Array.from(document.querySelectorAll('#knowledge-navigation a[href], #knowledge-navigation button, #knowledge-navigation summary'))
                .filter(element => element.checkVisibility()).map(element => ({html: element.outerHTML.slice(0, 160), visible: element.checkVisibility(), closed: !!element.closest('details:not([open])')})) })");
        Assert.True(await page.EvaluateAsync<bool>(@"() => {
            const controls = Array.from(document.querySelectorAll('#knowledge-navigation a[href], #knowledge-navigation button, #knowledge-navigation summary'))
                .filter(element => element.checkVisibility());
            return document.activeElement === controls[controls.length - 1];
        }"), navigationFocus);
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

    /// <summary>Topic disclosures and current-article links work with keyboard and mobile focus.</summary>
    /// <param name="width">The reading viewport width.</param>
    /// <returns>The browser verification task.</returns>
    [Theory]
    [InlineData(375)]
    [InlineData(1440)]
    public async Task GroupedContents_NavigatesToArticleSections(int width)
    {
        await using IBrowserContext context = await this.CreateContextAsync(width);
        IPage page = await context.NewPageAsync();
        string origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync($"{origin}/knowledges/cnc/feeds-and-speeds?culture=en");
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.IsVisibleAsync()) await consent.ClickAsync();
        if (width < 1152)
        {
            await page.Locator("[data-workspace-open]").ClickAsync();
            await page.WaitForFunctionAsync("() => document.activeElement === document.querySelector('[data-workspace-close]')");
        }

        ILocator materialGroup = page.Locator(".knowledge-nav-group").Nth(2);
        ILocator summary = materialGroup.Locator("summary");
        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.NotNull(await materialGroup.GetAttributeAsync("open"));
        Assert.True(await materialGroup.Locator(".workspace-nav-link").First.IsVisibleAsync());
        await page.Keyboard.PressAsync("Enter");
        Assert.Null(await materialGroup.GetAttributeAsync("open"));
        Assert.Equal(7, await page.Locator("[data-knowledge-current-toc] a").CountAsync());
        await page.Locator("[data-knowledge-current-toc] a[href='#milling']").ClickAsync();
        await page.WaitForURLAsync("**#milling");
        Assert.True(await page.Locator("#milling").EvaluateAsync<bool>("section => document.activeElement === section"));
        Assert.False(await page.Locator("#knowledge-content").EvaluateAsync<bool>("element => element.inert"));
        Assert.True(await page.Locator("#milling").EvaluateAsync<bool>("element => element.getBoundingClientRect().top >= 0 && element.getBoundingClientRect().top < innerHeight"));
        if (width < 1152)
        {
            Assert.Equal("false", await page.Locator("[data-workspace-open]").GetAttributeAsync("aria-expanded"));
        }
    }


    [Theory]
    [InlineData("en", 375)]
    [InlineData("th", 375)]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    public async Task NativeGroupsRemainBrowseableWithoutJavaScript(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ViewportSize = new ViewportSize { Width = width, Height = 900 }
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        var response = await page.GotoAsync($"{origin}/knowledges?culture={culture}");
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        var sidebar = page.Locator("#knowledge-navigation");
        Assert.True(await sidebar.IsVisibleAsync());
        Assert.Equal(5, await sidebar.Locator("details").CountAsync());
        Assert.Equal(19, await sidebar.Locator(".workspace-nav-link").CountAsync());
        Assert.False(await page.Locator("[data-knowledge-search]").IsVisibleAsync());
        foreach (var group in await sidebar.Locator("details").AllAsync())
        {
            var summary = group.Locator("summary");
            if (await group.GetAttributeAsync("open") is null)
            {
                await summary.FocusAsync();
                await summary.PressAsync("Enter");
            }
            Assert.True(await group.Locator(".workspace-nav-link").First.IsVisibleAsync());
        }
        Assert.Equal(1, await sidebar.Locator("[aria-current='page']").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    [Fact]
    public async Task PageHideClosesDrawerAndRestoresPreviouslyInertBackground()
    {
        await using var context = await CreateContextAsync(375);
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync($"{origin}/knowledges?culture=en");
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.IsVisibleAsync()) await consent.ClickAsync();
        Assert.Equal(0, await page.Locator("#cookieConsent").CountAsync());
        await page.EvaluateAsync("""
            () => {
                const probe = document.createElement('div');
                probe.id = 'already-inert-probe';
                probe.inert = true;
                document.body.append(probe);
                document.querySelector('[data-workspace-open]').click();
                dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
            }
            """);
        Assert.False(await page.Locator("#knowledge-navigation").EvaluateAsync<bool>("e => e.classList.contains('is-open')"));
        Assert.False(await page.Locator("#knowledge-content").EvaluateAsync<bool>("e => e.inert"));
        Assert.True(await page.Locator("#already-inert-probe").EvaluateAsync<bool>("e => e.inert"));
        Assert.Equal("false", await page.Locator("[data-workspace-open]").GetAttributeAsync("aria-expanded"));
        await page.EvaluateAsync("() => dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");
        await page.Locator("[data-workspace-open]").ClickAsync();
        await page.WaitForFunctionAsync("() => document.activeElement === document.querySelector('[data-workspace-close]')");
        await page.Keyboard.PressAsync("Escape");
        Assert.True(await page.Locator("[data-workspace-open]").EvaluateAsync<bool>("e => e === document.activeElement"));
    }

    private async Task<IBrowserContext> CreateContextAsync(int width)
    {
        IBrowserContext context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.RouteAsync("**/*", route => Uri.TryCreate(route.Request.Url, UriKind.Absolute, out Uri? uri) && uri.IsLoopback
            ? route.ContinueAsync() : route.AbortAsync());
        return context;
    }
}
