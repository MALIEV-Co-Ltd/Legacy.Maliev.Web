using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCompactControlReachabilityBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public void PricingReachabilityFixtureMatchesWorkflowMarkup()
    {
        var project = BrowserHostIdentityVerifier.SourceProjectDirectory();
        var workflow = File.ReadAllText(Path.Combine(project, "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor"));
        var css = File.ReadAllText(Path.Combine(project, "wwwroot", "src", "app", "css", "instant-quotation.css"));

        Assert.Contains("data-workflow-bulk-pricing tabindex=\"0\" role=\"region\"", workflow, StringComparison.Ordinal);
        Assert.Matches(@"\.instant-quote__bulk-pricing\s*\{[^}]*max-height:\s*11rem;[^}]*overflow-y:\s*auto;", css);
    }

    [Theory]
    [InlineData(320, 700, "en", ColorScheme.Light)]
    [InlineData(320, 700, "en", ColorScheme.Dark)]
    [InlineData(320, 700, "th", ColorScheme.Light)]
    [InlineData(320, 700, "th", ColorScheme.Dark)]
    [InlineData(375, 667, "en", ColorScheme.Light)]
    [InlineData(375, 667, "en", ColorScheme.Dark)]
    [InlineData(375, 667, "th", ColorScheme.Light)]
    [InlineData(375, 667, "th", ColorScheme.Dark)]
    [InlineData(820, 800, "en", ColorScheme.Light)]
    [InlineData(820, 800, "en", ColorScheme.Dark)]
    [InlineData(820, 800, "th", ColorScheme.Light)]
    [InlineData(820, 800, "th", ColorScheme.Dark)]
    [InlineData(1280, 800, "en", ColorScheme.Light)]
    [InlineData(1280, 800, "en", ColorScheme.Dark)]
    [InlineData(1280, 800, "th", ColorScheme.Light)]
    [InlineData(1280, 800, "th", ColorScheme.Dark)]
    [InlineData(1280, 1032, "en", ColorScheme.Light)]
    public async Task CompletedPricingTiersAndNextActionRemainReachable(int width, int height, string culture, ColorScheme colorScheme)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = colorScheme,
            HasTouch = width <= 375,
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        // Synthetic workflow markup must not race the initial interactive render.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId !== undefined");

        // Exercise the target-native layout with a completed tier list. The hosted
        // preview has no uploaded part, so this is a CSS/keyboard geometry fixture,
        // not an end-to-end quotation or production pricing acceptance test.
        await page.EvaluateAsync("""
            thai => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'configured';
              workflow.innerHTML = `<section data-workflow-configuration>
                <label for="reach-quantity">${thai ? 'จำนวน' : 'Quantity'}</label><input id="reach-quantity" type="number" value="1">
                <dl data-workflow-part-price><div class="instant-quote__bulk-pricing"
                  data-workflow-bulk-pricing tabindex="0" role="region" aria-label="${thai ? 'ตารางราคาตามจำนวน' : 'Bulk pricing table'}">
                  ${Array.from({length: 20}, (_, i) => `<div data-workflow-price-tier><dt>${thai ? 'จำนวน' : 'Quantity'} ${i + 1}</dt><dd>฿100.00</dd></div>`).join('')}
                </div></dl>
                <div class="instant-quote__configuration-footer"><div class="instant-quote__configuration-actions">
                  <button id="reach-review" type="button">${thai ? 'ตรวจสอบ' : 'Review'}</button>
                </div></div></section>`;
            }
            """, culture == "th");

        var pricing = page.Locator("[data-workflow-bulk-pricing]");
        var finalTier = pricing.Locator("[data-workflow-price-tier]").Last;
        Assert.True(await pricing.IsVisibleAsync());
        Assert.True(await pricing.EvaluateAsync<bool>("element => { const rows = [...element.querySelectorAll('[data-workflow-price-tier]')]; return getComputedStyle(element).display === 'block' && rows.every((row, index) => index === 0 || row.getBoundingClientRect().top >= rows[index - 1].getBoundingClientRect().bottom - 1); }"));
        Assert.True(await pricing.EvaluateAsync<bool>("element => innerWidth < 992 ? getComputedStyle(element).overflowY === 'visible' : getComputedStyle(element).overflowY === 'auto'"));
        if (height >= 1000)
        {
            var tallGeometry = await pricing.EvaluateAsync<string>("element => JSON.stringify({ height: element.getBoundingClientRect().height, maxHeight: getComputedStyle(element).maxHeight, scrollHeight: element.scrollHeight, viewportHeight: innerHeight })");
            Assert.True(await pricing.EvaluateAsync<bool>("element => element.getBoundingClientRect().height > 176"), tallGeometry);
        }
        await finalTier.ScrollIntoViewIfNeededAsync();
        Assert.True(await finalTier.EvaluateAsync<bool>("element => { const tier = element.getBoundingClientRect(); const rail = element.parentElement.getBoundingClientRect(); return tier.top >= rail.top - 1 && tier.bottom <= rail.bottom + 1; }"));

        await page.Locator("#reach-quantity").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await pricing.EvaluateAsync<bool>("element => document.activeElement === element"));
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.Locator("#reach-review").EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        if (Environment.GetEnvironmentVariable("MALIEV_WEB_VALIDATION_SCREENSHOTS") == "1"
            && width is 375 or 1280 && culture == "th" && colorScheme == ColorScheme.Light)
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(Path.GetTempPath(), $"legacy-web-276-pricing-{culture}-{width}-{height}.png"),
            });
        }
    }
}
