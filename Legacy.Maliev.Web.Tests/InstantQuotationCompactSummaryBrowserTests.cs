using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCompactSummaryBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public void NativeDockKeepsWarningConsultationAndLegalLinksInTheWorkflow()
    {
        var project = BrowserHostIdentityVerifier.SourceProjectDirectory();
        var markup = File.ReadAllText(Path.Combine(project, "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor"));
        var code = File.ReadAllText(Path.Combine(project, "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor.cs"));

        Assert.Contains("data-summary-warning role=\"img\"", markup, StringComparison.Ordinal);
        Assert.Contains("HasConfigurationWarnings", markup + code, StringComparison.Ordinal);
        Assert.Contains("data-summary-part-details", markup, StringComparison.Ordinal);
        Assert.Contains("data-summary-legal", markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/privacypolicy\"", markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/nondisclosureagreement\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-summary-consultation href=\"/contact#contact-us\" target=\"_blank\"", markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(320, 700, "en")]
    [InlineData(375, 667, "th")]
    [InlineData(820, 800, "en")]
    [InlineData(1280, 800, "th")]
    public async Task WarningAndExpandedDetailsRemainReachableBesideIndependentActions(int width, int height, string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        await page.EvaluateAsync("""
            thai => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'configured';
              workflow.innerHTML = `<section data-workflow-configuration>
                <div style="height:900px">Part configuration</div>
                <div class="instant-quote__configuration-footer">
                  <details class="instant-quote__summary-dock" data-workflow-summary-dock>
                    <summary><span class="instant-quote__summary-dock-heading">
                      <span class="instant-quote__summary-warning" data-summary-warning role="img" aria-label="${thai ? 'ข้อควรระวังด้านการผลิต' : 'Manufacturing warning'}"><span aria-hidden="true">!</span></span>
                      <span class="instant-quote__summary-dock-title">${thai ? 'สรุปชิ้นงานและราคา' : 'Part and price summary'}</span>
                      </span><span>1 part</span><strong>฿12,964.00</strong><span class="instant-quote__summary-dock-caret" aria-hidden="true">⌄</span></summary>
                    <dl data-workflow-order-summary data-workflow-order-total>
                      <div data-summary-part-details><dt>${thai ? 'รายละเอียดชิ้นงาน' : 'Part details'}</dt><dd>very-long-part-name-for-a-custom-fabrication-project.stl</dd></div>
                      <div><dt>Dimensions</dt><dd>10 × 20 × 30 mm</dd></div>
                      <div><dt>Volume</dt><dd>6 cm³</dd></div>
                      <div><dt>Surface area</dt><dd>2 cm²</dd></div>
                      <div><dt>Min. thickness</dt><dd>1 mm</dd></div>
                      <div><dt>Total</dt><dd>฿12,964.00</dd></div>
                    </dl>
                    <div class="instant-quote__summary-legal" data-summary-legal>
                      <a href="/legal/privacypolicy">${thai ? 'นโยบายความเป็นส่วนตัว' : 'Privacy Policy'}</a>
                      <span aria-hidden="true">·</span>
                      <a href="/legal/nondisclosureagreement">${thai ? 'สัญญาปกปิดความลับ' : 'Non-Disclosure Agreement'}</a>
                    </div>
                  </details>
                  <div class="instant-quote__configuration-actions">
                    <button type="button">${thai ? 'ตรวจสอบรายการ' : 'Review'}</button>
                    <a class="instant-quote__consultation-link" data-summary-consultation href="/contact#contact-us" target="_blank" rel="noopener noreferrer">${thai ? 'ปรึกษาวิศวกร' : 'Talk to an engineer'}</a>
                  </div>
                </div></section>`;
            }
            """, culture == "th");

        var dock = page.Locator("[data-workflow-summary-dock]");
        var summary = dock.Locator("summary");
        var consultation = page.Locator("[data-summary-consultation]");
        Assert.True(await page.Locator("[data-summary-warning]").IsVisibleAsync());
        Assert.Equal(culture == "th" ? "ข้อควรระวังด้านการผลิต" : "Manufacturing warning",
            await page.Locator("[data-summary-warning]").GetAttributeAsync("aria-label"));
        Assert.False(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.False(await page.Locator("[data-summary-legal]").IsVisibleAsync());
        Assert.True(await consultation.IsVisibleAsync());
        Assert.True(await consultation.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));

        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.True(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await page.Locator("[data-summary-part-details]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-summary-legal]").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.True(await page.Locator(".instant-quote__configuration-actions button").IsVisibleAsync());
        await consultation.FocusAsync();
        Assert.True(await consultation.EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.Equal("_blank", await consultation.GetAttributeAsync("target"));
        Assert.Contains("noopener", await consultation.GetAttributeAsync("rel"));
    }

    [Theory]
    [InlineData(375, 667)]
    [InlineData(820, 800)]
    [InlineData(1280, 800)]
    public async Task CompactDockKeepsTotalAndReviewReachableWhenDetailsToggle(int width, int height)
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

        await page.EvaluateAsync("""
            () => {
              const workflow = document.querySelector('.instant-quote__workflow');
              workflow.dataset.workflowState = 'configuration';
              workflow.innerHTML = `<section data-workflow-configuration>
                <div style="height: 1100px">Part configuration</div>
                <div class="instant-quote__configuration-footer">
                  <details class="instant-quote__summary-dock" data-workflow-summary-dock>
                    <summary><span class="instant-quote__summary-dock-title">Part and price summary</span>
                      <span>2 parts</span><strong>฿12,964.00</strong><span aria-hidden="true">⌄</span></summary>
                    <dl data-workflow-order-summary><div><dt>Subtotal</dt><dd>฿12,000.00</dd></div>
                      <div><dt>Shipping</dt><dd>฿100.00</dd></div><div><dt>VAT</dt><dd>฿864.00</dd></div>
                      <div><dt>Total</dt><dd>฿12,964.00</dd></div></dl>
                  </details>
                  <p class="instant-quote__price-confidence">Preliminary price</p>
                  <div class="instant-quote__configuration-actions"><button type="button">Review</button></div>
                </div></section>`;
            }
            """);

        var dock = page.Locator("[data-workflow-summary-dock]");
        var summary = dock.Locator("summary");
        var review = page.Locator(".instant-quote__configuration-actions button");
        Assert.False(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await summary.IsVisibleAsync());
        Assert.True(await review.IsVisibleAsync());
        Assert.True(await summary.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
        Assert.True(await summary.EvaluateAsync<bool>("element => getComputedStyle(element).display === 'grid'"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

        await summary.ClickAsync();
        Assert.True(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await dock.Locator("[data-workflow-order-summary]").IsVisibleAsync());
        Assert.True(await review.IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }
}
