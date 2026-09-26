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
    [InlineData(375, 667)]
    [InlineData(820, 800)]
    [InlineData(1280, 800)]
    public async Task CompletedPricingTiersAndNextActionRemainReachable(int width, int height)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), "/instantquotation/3d-printing?culture=en").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        // Exercise the target-native layout with a completed tier list. The hosted
        // preview has no uploaded part, so this is a CSS/keyboard geometry fixture,
        // not an end-to-end quotation or production pricing acceptance test.
        await page.EvaluateAsync("""
            () => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'configured';
              workflow.innerHTML = `<section data-workflow-configuration>
                <label for="reach-quantity">Quantity</label><input id="reach-quantity" type="number" value="1">
                <dl data-workflow-part-price><div class="instant-quote__bulk-pricing"
                  data-workflow-bulk-pricing tabindex="0" role="region" aria-label="Bulk pricing table">
                  ${Array.from({length: 10}, (_, i) => `<div data-workflow-price-tier><dt>Quantity ${i + 1}</dt><dd>฿100.00</dd></div>`).join('')}
                </div></dl>
                <div class="instant-quote__configuration-footer"><div class="instant-quote__configuration-actions">
                  <button id="reach-review" type="button">Review</button>
                </div></div></section>`;
            }
            """);

        var pricing = page.Locator("[data-workflow-bulk-pricing]");
        var finalTier = pricing.Locator("[data-workflow-price-tier]").Last;
        Assert.True(await pricing.IsVisibleAsync());
        await finalTier.ScrollIntoViewIfNeededAsync();
        Assert.True(await finalTier.EvaluateAsync<bool>("element => { const tier = element.getBoundingClientRect(); const rail = element.parentElement.getBoundingClientRect(); return tier.top >= rail.top - 1 && tier.bottom <= rail.bottom + 1; }"));

        await page.Locator("#reach-quantity").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await pricing.EvaluateAsync<bool>("element => document.activeElement === element"));
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.Locator("#reach-review").EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }
}
