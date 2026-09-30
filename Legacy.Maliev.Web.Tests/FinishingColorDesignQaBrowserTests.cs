using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class FinishingColorDesignQaBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", 1360, false)]
    [InlineData("th", 1360, true)]
    [InlineData("en", 390, true)]
    [InlineData("th", 390, false)]
    public async Task Matcher_KeyboardSelectionLocalizedQuoteAndResponsiveLayoutRemainUsable(
        string culture, int width, bool reduced)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 912 },
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        await using var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.AddInitScriptAsync("""
            window.__matcherScheduledFrames = 0;
            window.__matcherPointerEvents = 0;
            let handlingMatcherPointer = false;
            const nativeListen = EventTarget.prototype.addEventListener;
            EventTarget.prototype.addEventListener = function (type, listener, options) {
                if (this instanceof Element && this.matches('[data-sheen-stage]')
                    && (type === 'pointermove' || type === 'pointerleave') && typeof listener === 'function') {
                    const original = listener;
                    listener = function (event) {
                        window.__matcherPointerEvents++;
                        handlingMatcherPointer = true;
                        try { return original.call(this, event); }
                        finally { handlingMatcherPointer = false; }
                    };
                }
                return nativeListen.call(this, type, listener, options);
            };
            const nativeFrame = window.requestAnimationFrame.bind(window);
            window.requestAnimationFrame = callback => {
                if (handlingMatcherPointer) window.__matcherScheduledFrames++;
                return nativeFrame(callback);
            };
            """);
        await OpenMatcherAsync(page, culture);
        var root = page.Locator("[data-finish-color-matcher]");
        Assert.Equal(culture, await root.GetAttributeAsync("data-culture"));
        Assert.Equal(10, await root.Locator("[data-reference-code]").CountAsync());
        Assert.Equal(1, await root.Locator("[aria-pressed='true']").CountAsync());
        Assert.Contains("HLC Colour Atlas", await root.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "แผ่นพ่นทดสอบ" : "physical spray-out", await root.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal("https://www.pantone.com/na/en-us/color-finder",
            await root.Locator("a[href*='pantone.com']").GetAttributeAsync("href"));

        var hex = root.Locator("[data-hex-input]");
        await hex.FillAsync("#00FF00");
        await hex.PressAsync("Enter");
        await Assertions.Expect(root.Locator("[data-selected-hex]")).ToHaveTextAsync("#00FF00");
        var cards = root.Locator("[data-reference-code]");
        await cards.Nth(0).FocusAsync();
        await cards.Nth(0).PressAsync("ArrowRight");
        Assert.Equal("true", await cards.Nth(1).GetAttributeAsync("aria-pressed"));
        Assert.True(await cards.Nth(1).EvaluateAsync<bool>("element => element === document.activeElement"));
        await cards.Nth(1).PressAsync("End");
        Assert.Equal("true", await cards.Nth(9).GetAttributeAsync("aria-pressed"));
        await cards.Nth(9).PressAsync("Home");
        Assert.Equal("true", await cards.Nth(0).GetAttributeAsync("aria-pressed"));

        var pantone = root.Locator("[data-pantone-input]");
        await pantone.EvaluateAsync("element => element.closest('details').open = true");
        await pantone.FillAsync("PANTONE 16-6340 TCX");
        // The styled radio is covered by its label: exercise native keyboard
        // activation rather than forcing a click through the visible label.
        var gloss = root.Locator("input[name='finish-sheen'][value='gloss']");
        await gloss.FocusAsync();
        await gloss.PressAsync("Space");
        Assert.True(await gloss.IsCheckedAsync());
        Assert.Equal("gloss", await root.Locator("[data-sheen-preview]").GetAttributeAsync("data-sheen"));
        Assert.Contains(culture == "th" ? "เงา" : "Gloss", await root.Locator("[data-selected-sheen]").InnerTextAsync(), StringComparison.Ordinal);
        var quote = root.Locator("[data-quote-link]");
        await quote.ScrollIntoViewIfNeededAsync();
        var box = await quote.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.X >= 0 && box.X + box.Width <= width + 1);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));

        if (reduced)
        {
            Assert.True(await page.EvaluateAsync<bool>("matchMedia('(prefers-reduced-motion: reduce)').matches"));
            var stage = root.Locator("[data-sheen-stage]");
            await stage.ScrollIntoViewIfNeededAsync();
            var previewProbe = await page.EvaluateAsync<string>("""
                () => JSON.stringify({
                    threeRevision: window.MalievFinishingThree?.REVISION ?? null,
                    webglAvailable: !!document.createElement('canvas').getContext('webgl2'),
                    previewApi: typeof window.MalievFinishColorMatcherPreview?.create,
                    stageClass: document.querySelector('[data-sheen-stage]').className
                })
                """);
            Assert.True((await stage.GetAttributeAsync("class"))?.Contains("is-pbr-ready", StringComparison.Ordinal) == true, previewProbe);
            await Assertions.Expect(stage).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-pbr-ready"));
            var stageBox = await stage.BoundingBoxAsync();
            Assert.NotNull(stageBox);
            await page.EvaluateAsync("window.__matcherScheduledFrames = 0");
            await page.Mouse.MoveAsync(stageBox.X + stageBox.Width / 2, stageBox.Y + stageBox.Height / 2);
            Assert.True(await page.EvaluateAsync<int>("window.__matcherPointerEvents") > 0);
            Assert.Equal(0, await page.EvaluateAsync<int>("window.__matcherScheduledFrames"));
        }

        var evidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "finishing-color-design-qa");
        Directory.CreateDirectory(evidence);
        await root.ScreenshotAsync(new() { Path = Path.Combine(evidence, $"matcher-{culture}-{width}.png") });
        var href = await quote.GetAttributeAsync("href");
        Assert.NotNull(href);
        await page.GotoAsync(new Uri(fixture.Origin, href).ToString());
        var message = await page.Locator("#Message").InputValueAsync();
        Assert.Contains("#00FF00", message, StringComparison.Ordinal);
        Assert.Contains("PANTONE 16-6340 TCX", message, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ระดับความเงา: เงา" : "Finish sheen: Gloss", message, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "รหัสอ้างอิง HLC:" : "HLC reference:", message, StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task Matcher_InvalidHexAndImagePreserveSelectionAndExposeLocalizedErrors(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await using var page = await context.NewPageAsync();
        await OpenMatcherAsync(page, culture);
        var root = page.Locator("[data-finish-color-matcher]");
        var selected = await root.Locator("[data-selected-code]").InnerTextAsync();
        var hex = root.Locator("[data-hex-input]");
        await hex.FillAsync("invalid");
        await hex.PressAsync("Enter");
        Assert.False(await hex.EvaluateAsync<bool>("element => element.checkValidity()"));
        Assert.Equal(await root.GetAttributeAsync("data-invalid-hex"), await hex.EvaluateAsync<string>("element => element.validationMessage"));
        Assert.Equal(selected, await root.Locator("[data-selected-code]").InnerTextAsync());
        await root.Locator("[data-image-input]").SetInputFilesAsync(new FilePayload
        {
            Name = "invalid.txt",
            MimeType = "text/plain",
            Buffer = "not an image"u8.ToArray(),
        });
        var status = root.Locator("[data-image-status]");
        Assert.True(await status.IsVisibleAsync());
        Assert.Equal(await root.GetAttributeAsync("data-invalid-image"), await status.InnerTextAsync());
        Assert.Equal(selected, await root.Locator("[data-selected-code]").InnerTextAsync());
    }

    private async Task OpenMatcherAsync(IPage page, string culture)
    {
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services/finishing-and-color?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-finish-color-matcher]")).ToHaveAttributeAsync("data-matcher-initialized", "true");
    }
}
