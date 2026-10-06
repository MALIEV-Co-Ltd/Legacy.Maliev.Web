using System.Security.Cryptography;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Retains final historical scanning presentation contracts on the actual owned active renderer.</summary>
[Collection(PublicContactBrowserCollection.Name)]
public sealed class ScanningComparisonBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task OutputTabs_PreserveOrderApplicationsAndKeyboardAccess(string culture)
    {
        await using IBrowserContext context = await CreateContextAsync(390);
        IPage page = await OpenPageAsync(context, culture);
        ILocator sample = page.Locator("#scanning-sample");
        ILocator tabs = sample.Locator("[data-proof-tab]");
        Assert.Equal(3, await tabs.CountAsync());
        Assert.Equal("Mesh / CAD", await tabs.Nth(0).InnerTextAsync());
        Assert.Equal(0, await sample.Locator(".scanning-proof-number").CountAsync());
        Assert.True(await sample.Locator("[data-proof-tablist]").IsVisibleAsync());
        Assert.Equal("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"));
        Assert.True(await sample.Locator("#scanning-cad-panel").IsVisibleAsync());
        Assert.False(await sample.Locator("#scanning-report-panel").IsVisibleAsync());
        Assert.False(await sample.Locator("#scanning-color-panel").IsVisibleAsync());
        Assert.Contains(culture == "en" ? "manufacturing drawings" : "เขียนแบบผลิต", await sample.Locator("#scanning-cad-panel .scanning-applications").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.NoPreference });
        await tabs.Nth(0).FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("true", await tabs.Nth(1).GetAttributeAsync("aria-selected"));
        Assert.True(await sample.Locator("#scanning-report-panel").IsVisibleAsync());
        Assert.False(await sample.Locator("#scanning-cad-panel").IsVisibleAsync());
        Assert.Equal("scanning-proof-fade", await sample.Locator("#scanning-report-panel").EvaluateAsync<string>("panel => getComputedStyle(panel).animationName"));
        Assert.DoesNotContain("02 ·", await sample.Locator("#scanning-report-panel .scanning-report-kicker").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Contains(culture == "en" ? "wear or deformation" : "การสึกหรอหรือเสียรูป", await sample.Locator("#scanning-report-panel .scanning-applications").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await page.Keyboard.PressAsync("End");
        Assert.Equal("true", await tabs.Nth(2).GetAttributeAsync("aria-selected"));
        Assert.True(await sample.Locator("#scanning-color-panel").IsVisibleAsync());
        Assert.False(await sample.Locator("#scanning-report-panel").IsVisibleAsync());
        Assert.Equal("none", await sample.Locator("#scanning-color-panel").EvaluateAsync<string>("panel => getComputedStyle(panel).animationName"));
        Assert.Contains(culture == "en" ? "animation" : "แอนิเมชัน", await sample.Locator("#scanning-color-panel .scanning-applications").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    /// <summary>
    /// The in-image handle retains keyboard behavior, language and full-image alternatives.
    /// </summary>
    /// <param name="culture">The requested culture; unsupported cultures retain the site's Thai fallback.</param>
    /// <param name="width">The viewport width.</param>
    /// <returns>A task representing browser verification.</returns>
    [Theory]
    [InlineData("en", 320)]
    [InlineData("th", 320)]
    [InlineData("en", 390)]
    [InlineData("en", 1440)]
    [InlineData("th", 390)]
    [InlineData("th", 1440)]
    [InlineData("ru", 390)]
    public async Task Comparison_SupportsKeyboardModesAndExistingCultures(string culture, int width)
    {
        await using IBrowserContext context = await CreateContextAsync(width);
        IPage page = await OpenPageAsync(context, culture);
        ILocator sample = page.Locator("#scanning-sample");
        ILocator comparison = sample.Locator("[data-proof='color']");
        ILocator tabs = sample.Locator("[data-proof-tab]");
        Assert.Equal(3, await tabs.CountAsync());
        Assert.True(await sample.Locator("#scanning-cad-panel").IsVisibleAsync());
        Assert.False(await sample.Locator("#scanning-color-panel").IsVisibleAsync());
        await tabs.Nth(2).ClickAsync();
        ILocator slider = comparison.GetByRole(AriaRole.Slider);
        await sample.ScrollIntoViewIfNeededAsync();
        Assert.Contains(culture == "en" ? "outputs of 3D scanning" : "ผลลัพธ์จากการสแกน 3D", await sample.Locator("h2").First.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await sample.Locator("[data-scanning-comparison]").CountAsync());
        Assert.Equal("compare", await comparison.GetAttributeAsync("data-mode"));
        Assert.Equal(1, await comparison.Locator("button[aria-pressed='true']").CountAsync());
        Assert.Equal(0, await sample.Locator("input[type=range], [data-comparison-range]").CountAsync());
        Assert.Equal(culture == "en" ? "Raw scan" : "สแกนแบบไม่มีสี", await sample.Locator(".scanning-comparison-raw figcaption").InnerTextAsync());
        Assert.Equal(culture == "en" ? "Color scan" : "สแกนสี", await sample.Locator(".scanning-comparison-color figcaption").InnerTextAsync());
        var handleBounds = Assert.IsType<LocatorBoundingBoxResult>(await slider.Locator("span").BoundingBoxAsync());
        Assert.Equal(28, handleBounds.Width);
        Assert.Equal(28, handleBounds.Height);
        Assert.Equal(44, Assert.IsType<LocatorBoundingBoxResult>(await slider.BoundingBoxAsync()).Width);
        Assert.True(await comparison.Locator(".scanning-comparison-buttons").EvaluateAsync<bool>("group => { const boxes = Array.from(group.children, child => child.getBoundingClientRect()); return boxes.every((box, i) => i === 0 || (Math.abs(box.left - boxes[i - 1].right) < 1 && box.top === boxes[0].top)); }"));
        Assert.Equal("rgb(220, 224, 228)", await comparison.Locator("button[aria-pressed='true']").EvaluateAsync<string>("button => getComputedStyle(button).backgroundColor"));

        await slider.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        Assert.Equal("0", await slider.GetAttributeAsync("aria-valuenow"));
        await page.Keyboard.PressAsync("End");
        Assert.Equal("100", await slider.GetAttributeAsync("aria-valuenow"));
        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.Equal("99", await slider.GetAttributeAsync("aria-valuenow"));
        Assert.Contains("99%", await slider.GetAttributeAsync("aria-valuetext"));
        Assert.NotEqual("none", await slider.Locator("span").EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));

        foreach (string mode in new[] { "raw", "color", "side", "compare" })
        {
            await comparison.Locator("[data-comparison-mode='" + mode + "']").EvaluateAsync("button => button.click()");
            Assert.Equal(mode, await comparison.GetAttributeAsync("data-mode"));
            Assert.Equal(mode == "compare", await slider.IsVisibleAsync());
            Assert.Equal(mode != "color", await sample.Locator(".scanning-comparison-raw").IsVisibleAsync());
            Assert.Equal(mode != "raw", await sample.Locator(".scanning-comparison-color").IsVisibleAsync());
        }

        await slider.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        for (int i = 0; i < 5; i++) { await page.Keyboard.PressAsync("PageUp"); }
        await page.WaitForFunctionAsync("Array.from(document.querySelectorAll('#scanning-color-panel img')).every(image => image.complete && image.naturalWidth > 0)");
        Assert.Equal(7, await sample.Locator("img[loading='lazy'][decoding='async']").CountAsync());
        Assert.Equal(3, await sample.Locator("[data-deviation-report] figure").CountAsync());
        await tabs.Nth(1).ClickAsync();
        string reportText = await sample.Locator("[data-deviation-report]").InnerTextAsync();
        await tabs.Nth(2).ClickAsync();
        string comparisonNote = await sample.Locator("#scanning-color-panel .scanning-comparison-note").First.InnerTextAsync();
        Assert.Contains(culture == "en" ? "separate anonymized project" : "อีกโครงการหนึ่ง", reportText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(culture == "en" ? "true surface color" : "สีพื้นผิวจริง", comparisonNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(culture == "en" ? "deviation color map" : "แผนที่สีความคลาดเคลื่อน", comparisonNote, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await sample.Locator("canvas, model-viewer, iframe, object, a[download]").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.Equal(0, await sample.Locator("a.service-button").CountAsync());
        await sample.ScreenshotAsync(new LocatorScreenshotOptions { Path = ScreenshotPath($"scanning-comparison-{culture}-{width}.png") });
    }

    /// <summary>
    /// The restored cast-housing comparison remains independent of the color-scan comparison.
    /// </summary>
    /// <param name="culture">The requested page culture.</param>
    /// <returns>A task representing rendered comparison verification.</returns>
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task MeshAndCadComparison_RetainsBothViewsAndIndependentControls(string culture)
    {
        await using IBrowserContext context = await CreateContextAsync(390);
        IPage page = await OpenPageAsync(context, culture);
        ILocator sample = page.Locator("#scanning-sample");
        ILocator color = sample.Locator("[data-proof='color']");
        ILocator cad = sample.Locator("[data-proof='cad']");
        await cad.ScrollIntoViewIfNeededAsync();
        Assert.Contains(culture == "en" ? "scan mesh and reconstructed CAD" : "Mesh กับ CAD", await sample.Locator("#scanning-cad-title").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await cad.Locator("img").CountAsync());
        Assert.Equal("compare", await cad.GetAttributeAsync("data-mode"));
        Assert.Equal(culture == "en" ? "Scan mesh" : "เมชสแกน", await cad.Locator(".scanning-comparison-scan figcaption").InnerTextAsync());

        foreach (string mode in new[] { "scan", "cad", "side", "compare" })
        {
            await cad.Locator("[data-comparison-mode='" + mode + "']").EvaluateAsync("button => button.click()");
            Assert.Equal(mode, await cad.GetAttributeAsync("data-mode"));
            Assert.Equal(mode != "cad", await cad.Locator(".scanning-comparison-scan").IsVisibleAsync());
            Assert.Equal(mode != "scan", await cad.Locator(".scanning-comparison-cad").IsVisibleAsync());
        }

        ILocator slider = cad.GetByRole(AriaRole.Slider);
        await slider.FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("1", await slider.GetAttributeAsync("aria-valuenow"));
        Assert.Contains("CAD", await slider.GetAttributeAsync("aria-valuetext"));
        Assert.Equal("compare", await color.GetAttributeAsync("data-mode"));
        Assert.Equal("50", await color.Locator("[data-comparison-handle]").GetAttributeAsync("aria-valuenow"));
        await page.WaitForFunctionAsync("Array.from(document.querySelectorAll('#scanning-sample [data-proof=cad] img')).every(image => image.complete && image.naturalWidth > 0)");
    }

    /// <summary>
    /// Without JavaScript the two labeled images remain readable and controls stay hidden.
    /// </summary>
    /// <param name="culture">The supported culture.</param>
    /// <returns>A task representing browser verification.</returns>
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Comparison_WithoutJavaScriptKeepsBothImages(string culture)
    {
        await using IBrowserContext context = await CreateContextAsync(390, false);
        IPage page = await OpenPageAsync(context, culture, javaScript: false);
        ILocator sample = page.Locator("#scanning-sample");
        Assert.Equal(2, await sample.Locator("[data-scanning-comparison]").CountAsync());
        Assert.False(await sample.Locator("[data-proof-tablist]").IsVisibleAsync());
        Assert.Equal(3, await sample.Locator("[data-proof-panel]:visible").CountAsync());
        foreach (ILocator comparison in await sample.Locator("[data-scanning-comparison]").AllAsync())
        {
            Assert.Equal("side", await comparison.GetAttributeAsync("data-mode"));
            Assert.False(await comparison.Locator("[data-comparison-controls]").IsVisibleAsync());
            Assert.False(await comparison.Locator("[data-comparison-handle]").IsVisibleAsync());
        }
        Assert.Equal(0, await sample.Locator("input[type=range], [data-comparison-range]").CountAsync());
        foreach (ILocator figure in await sample.Locator("figure").AllAsync())
        {
            Assert.True(await figure.IsVisibleAsync());
            Assert.NotEmpty(await figure.Locator("figcaption").InnerTextAsync());
            Assert.NotEmpty(await figure.Locator("img").GetAttributeAsync("alt"));
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    /// <summary>
    /// Pointer dragging changes the same range value while full-view modes remain stable.
    /// </summary>
    /// <returns>A task representing browser verification.</returns>
    [Fact]
    public async Task Comparison_PointerDragChangesDividerOnlyInCompareMode()
    {
        await using IBrowserContext context = await CreateContextAsync(1440);
        IPage page = await OpenPageAsync(context, "en");
        ILocator comparison = page.Locator("[data-proof='color']");
        await page.Locator("#scanning-tab-color").ClickAsync();
        ILocator stage = comparison.Locator("[data-comparison-stage]");
        await stage.ScrollIntoViewIfNeededAsync();
        var bounds = Assert.IsType<LocatorBoundingBoxResult>(await stage.BoundingBoxAsync());
        await page.EvaluateAsync("() => { window.scanDragStarts = 0; document.querySelector('[data-proof=color] [data-comparison-stage]').addEventListener('dragstart', () => window.scanDragStarts++); }");
        await page.Mouse.MoveAsync(bounds.X + (bounds.Width * 0.25f), bounds.Y + (bounds.Height * 0.5f));
        await page.Mouse.DownAsync();
        try
        {
            await page.Mouse.MoveAsync(bounds.X + (bounds.Width * 0.75f), bounds.Y + (bounds.Height * 0.5f), new MouseMoveOptions { Steps = 30 });
            Assert.Equal("none", await comparison.Locator("[data-comparison-handle] span").EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
            Assert.Equal("rgba(0, 0, 0, 0)", await comparison.Locator("[data-comparison-handle] span").EvaluateAsync<string>("element => getComputedStyle(element).borderTopColor"));
        }
        finally
        {
            await page.Mouse.UpAsync();
        }
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.NotEqual("none", await comparison.Locator("[data-comparison-handle] span").EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.InRange(int.Parse((await comparison.Locator("[data-comparison-handle]").GetAttributeAsync("aria-valuenow"))!), 74, 76);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.scanDragStarts"));
        Assert.True(await page.EvaluateAsync<bool>("Array.from(document.querySelectorAll('#scanning-sample img')).every(image => image.draggable === false)"));
        await comparison.Locator("[data-comparison-mode='raw']").ClickAsync(new LocatorClickOptions { Force = true });
        await stage.ClickAsync();
        Assert.InRange(int.Parse((await comparison.Locator("[data-comparison-handle]").GetAttributeAsync("aria-valuenow"))!), 74, 76);
    }

    /// <summary>
    /// The in-image handle supports a continuous touch drag without the removed lower slider.
    /// </summary>
    /// <returns>A task representing touch verification.</returns>
    [Fact]
    public async Task Comparison_TouchDragMovesHandleWithoutScrollingPage()
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 1000 },
            HasTouch = true,
            IsMobile = true,
        });
        IPage page = await OpenPageAsync(context, "th");
        ILocator comparison = page.Locator("[data-proof='color']");
        await page.Locator("#scanning-tab-color").ClickAsync();
        ILocator stage = comparison.Locator("[data-comparison-stage]");
        await stage.ScrollIntoViewIfNeededAsync();
        await stage.EvaluateAsync("element => element.scrollIntoView({ block: 'center', behavior: 'instant' })");
        var bounds = Assert.IsType<LocatorBoundingBoxResult>(await stage.BoundingBoxAsync());
        Assert.Equal("compare", await comparison.GetAttributeAsync("data-mode"));
        string hit = await page.EvaluateAsync<string>("point => document.elementFromPoint(point[0], point[1])?.closest('[data-comparison-stage]')?.getAttribute('data-comparison-stage') ?? 'miss'", new[] { bounds.X + (bounds.Width * 0.5), bounds.Y + (bounds.Height * 0.5) });
        Assert.Equal(string.Empty, hit);
        double scrollBefore = await page.EvaluateAsync<double>("scrollY");
        ICDPSession cdp = await context.NewCDPSessionAsync(page);
        double touchY = bounds.Y + (bounds.Height * 0.5);
        try
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x = bounds.X + (bounds.Width * 0.5), y = touchY } } });
            for (int step = 1; step <= 12; step++)
            {
                await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x = bounds.X + (bounds.Width * (0.5 + (0.25 * step / 12))), y = touchY } } });
            }
        }
        finally
        {
            try
            {
                await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
            }
            finally
            {
                await cdp.DetachAsync();
            }
        }
        Assert.InRange(int.Parse((await comparison.Locator("[data-comparison-handle]").GetAttributeAsync("aria-valuenow"))!), 74, 76);
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - scrollBefore), 0, 1);
    }
    private Task<IBrowserContext> CreateContextAsync(int width, bool javaScript = true) =>
        fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = "th-TH",
            ViewportSize = new ViewportSize { Width = width, Height = 1000 },
            JavaScriptEnabled = javaScript,
        });

    private async Task<IPage> OpenPageAsync(IBrowserContext context, string culture, bool javaScript = true)
    {
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin,
            "/services/3d-scanning?culture=" + culture).ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        var html = await response.TextAsync();
        Assert.Contains("data-migration-component=\"three-dimensional-scanning-content\"", html, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
        await EnsureCurrentPublicAssetsAsync(context, html);
        Assert.Equal(1, await page.Locator("main[data-migration-component='three-dimensional-scanning-content'][data-migration-route-owner='blazor-static-ssr']").CountAsync());
        if (javaScript)
        {
            await page.Locator("#scanning-sample[data-proof-tabs-ready] [data-proof-tablist]")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await page.WaitForFunctionAsync("Array.from(document.querySelectorAll('#scanning-sample [data-scanning-comparison]')).length === 2 && Array.from(document.querySelectorAll('#scanning-sample [data-scanning-comparison]')).every(element => element.dataset.mode === 'compare')");
            var reject = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await reject.IsVisibleAsync())
            {
                await reject.ClickAsync();
                await page.Locator("#cookieConsent").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
            }
        }
        await page.EvaluateAsync("() => document.fonts.ready");
        return page;
    }

    private async Task EnsureCurrentPublicAssetsAsync(IBrowserContext context, string html)
    {
        // The owned factory references Program from this test workspace. Public pages
        // identify assets by source-byte hashes, not the quotation-specific worker MVID.
        var root = BrowserHostIdentityVerifier.SourceProjectDirectory();
        foreach (var asset in new[] { "app.min.js", "route-service-scanning.js", "route-services.css" })
        {
            var expectedBytes = File.ReadAllBytes(Path.Combine(root, "wwwroot", "dist", asset));
            var digest = SHA256.HashData(expectedBytes);
            var version = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var assetPath = $"/dist/{asset}?v={version}";
            Assert.Contains(assetPath, html, StringComparison.Ordinal);
            var response = await context.APIRequest.GetAsync(new Uri(fixture.Origin, assetPath).ToString());
            Assert.Equal(200, response.Status);
            Assert.Equal(Convert.ToHexString(digest), Convert.ToHexString(SHA256.HashData(await response.BodyAsync())));
            // The per-case browser context owns and releases its API request response buffers.
        }
    }

    private static string ScreenshotPath(string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "scanning-comparison-screenshots");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }
}
