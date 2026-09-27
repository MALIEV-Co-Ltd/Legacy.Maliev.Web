using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationCompactSummaryBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public void LocalizedLeadTimeIsInTheAlwaysVisibleSummaryNotCollapsedDetails()
    {
        var project = BrowserHostIdentityVerifier.SourceProjectDirectory();
        var markup = File.ReadAllText(Path.Combine(project, "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor"));
        var summaryStart = markup.IndexOf("<details class=\"instant-quote__summary-dock\"", StringComparison.Ordinal);
        var summaryEnd = markup.IndexOf("</summary>", summaryStart, StringComparison.Ordinal);
        var detailsEnd = markup.IndexOf("</details>", summaryEnd, StringComparison.Ordinal);

        Assert.True(summaryStart >= 0 && summaryEnd > summaryStart && detailsEnd > summaryEnd);
        Assert.Contains("data-workflow-lead-time", markup[summaryStart..summaryEnd], StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Lead time\"]", markup[summaryStart..summaryEnd], StringComparison.Ordinal);
        Assert.Contains("@LeadTime", markup[summaryStart..summaryEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("data-workflow-lead-time", markup[summaryEnd..detailsEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void NativeDockKeepsWarningConsultationAndLegalLinksInTheWorkflow()
    {
        var project = BrowserHostIdentityVerifier.SourceProjectDirectory();
        var markup = File.ReadAllText(Path.Combine(project, "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor"));
        var code = File.ReadAllText(Path.Combine(project, "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor.cs"));
        var css = File.ReadAllText(Path.Combine(project, "wwwroot", "src", "app", "css", "instant-quotation.css"));

        Assert.Contains("data-summary-warning role=\"img\"", markup, StringComparison.Ordinal);
        Assert.Contains("HasConfigurationWarnings", markup + code, StringComparison.Ordinal);
        Assert.Contains("data-summary-part-details", markup, StringComparison.Ordinal);
        Assert.Contains("data-workflow-order-summary data-workflow-order-total aria-busy=", markup, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\" aria-label=\"@Localizer[\"Part and price summary\"]\"", markup, StringComparison.Ordinal);
        Assert.Contains("[data-workflow-order-total]:focus-visible", css, StringComparison.Ordinal);
        foreach (var label in new[] { "Dimensions", "Volume", "Surface area", "Min. thickness", "Subtotal", "Shipping", "VAT", "Total" })
        {
            Assert.Contains($"@Localizer[\"{label}\"]", markup, StringComparison.Ordinal);
        }
        Assert.Contains("data-summary-legal", markup, StringComparison.Ordinal);
        Assert.True(markup.IndexOf("data-summary-legal", StringComparison.Ordinal) >
            markup.IndexOf("</details>", markup.IndexOf("data-workflow-summary-dock", StringComparison.Ordinal), StringComparison.Ordinal));
        Assert.Contains("data-summary-show", markup, StringComparison.Ordinal);
        Assert.Contains("data-summary-hide", markup, StringComparison.Ordinal);
        var resource = System.Xml.Linq.XDocument.Load(Path.Combine(project, "Resources", "Components", "Pages",
            "InstantQuotation", "ThreeDimensionalPrintingEstimateContent.th.resx"));
        var labels = resource.Descendants("data").ToDictionary(
            element => (string)element.Attribute("name")!,
            element => (string?)element.Element("value") ?? string.Empty,
            StringComparer.Ordinal);
        Assert.Equal("แสดงรายละเอียด", labels["Show details"]);
        Assert.Equal("ซ่อนรายละเอียด", labels["Hide details"]);
        Assert.Contains("href=\"/legal/privacypolicy\"", markup, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/nondisclosureagreement\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-summary-consultation href=\"/contact#contact-us\" target=\"_blank\"", markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(320, 700, "en", ColorScheme.Light)]
    [InlineData(375, 667, "th", ColorScheme.Dark)]
    [InlineData(820, 800, "en", ColorScheme.Dark)]
    [InlineData(1280, 800, "th", ColorScheme.Light)]
    public async Task WarningAndExpandedDetailsRemainReachableBesideIndependentActions(int width, int height, string culture, ColorScheme colorScheme)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = colorScheme,
        });
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync(new Uri(new Uri(origin), $"/instantquotation/3d-printing?culture={culture}").ToString());
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        await page.EvaluateAsync("""
            thai => {
              const host = document.createElement('main');
              host.id = 'summary-test-host';
              host.className = 'instant-quote';
              const workflow = document.createElement('div');
              workflow.className = 'instant-quote__workflow';
              host.append(workflow);
              document.body.append(host);
              workflow.dataset.workflowState = 'configured';
              workflow.innerHTML = `<section data-workflow-configuration>
                <div style="height:900px">Part configuration</div>
                <div class="instant-quote__configuration-footer">
                  <details class="instant-quote__summary-dock" data-workflow-summary-dock>
                    <summary><span class="instant-quote__summary-dock-heading">
                      <span class="instant-quote__summary-warning" data-summary-warning role="img" aria-label="${thai ? 'ข้อควรระวังด้านการผลิต' : 'Manufacturing warning'}"><span aria-hidden="true">!</span></span>
                      <span class="instant-quote__summary-dock-title">${thai ? 'สรุปชิ้นงานและราคา' : 'Part and price summary'}</span>
                      </span><span class="instant-quote__summary-dock-meta"><span>1 part</span><span data-workflow-lead-time aria-live="polite" aria-atomic="true">${thai ? 'ระยะเวลา' : 'Lead time'} <strong>${thai ? '3–5 วันทำการ' : '3–5 business days'}</strong></span></span><strong>฿12,964.00</strong><span class="instant-quote__summary-dock-disclosure"><span data-summary-show>${thai ? 'แสดงรายละเอียด' : 'Show details'}</span><span data-summary-hide>${thai ? 'ซ่อนรายละเอียด' : 'Hide details'}</span><span class="instant-quote__summary-dock-caret" aria-hidden="true">⌄</span></span></summary>
                    <dl data-workflow-order-summary data-workflow-order-total tabindex="0" aria-label="${thai ? 'สรุปชิ้นงานและราคา' : 'Part and price summary'}">
                      <div data-summary-part-details><dt>${thai ? 'รายละเอียดชิ้นงาน' : 'Part details'}</dt><dd>very-long-part-name-for-a-custom-fabrication-project.stl</dd></div>
                      <div><dt>${thai ? 'ขนาด' : 'Dimensions'}</dt><dd>10 × 20 × 30 mm</dd></div>
                      <div><dt>${thai ? 'ปริมาตร' : 'Volume'}</dt><dd>6 cm³</dd></div>
                      <div><dt>${thai ? 'พื้นที่ผิว' : 'Surface area'}</dt><dd>2 cm²</dd></div>
                      <div><dt>${thai ? 'ความหนาต่ำสุด' : 'Min. thickness'}</dt><dd>1 mm</dd></div>
                      <div><dt>${thai ? 'ยอดรวมย่อย' : 'Subtotal'}</dt><dd>฿12,000.00</dd></div>
                      <div><dt>${thai ? 'ค่าจัดส่ง' : 'Shipping'}</dt><dd>฿100.00</dd></div>
                      <div><dt>${thai ? 'ภาษีมูลค่าเพิ่ม' : 'VAT'}</dt><dd>฿864.00</dd></div>
                      <div><dt>${thai ? 'รวมทั้งหมด' : 'Total'}</dt><dd>฿12,964.00</dd></div>
                    </dl>
                  </details>
                  <div class="instant-quote__summary-legal" data-summary-legal>
                      <a href="/legal/privacypolicy">${thai ? 'นโยบายความเป็นส่วนตัว' : 'Privacy Policy'}</a>
                      <span aria-hidden="true">·</span>
                      <a href="/legal/nondisclosureagreement">${thai ? 'สัญญาปกปิดความลับ' : 'Non-Disclosure Agreement'}</a>
                    </div>
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
        if (width == 320)
        {
            await page.Locator("#summary-test-host").ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(Path.GetTempPath(), "legacy-wall-summary-main-320-collapsed.png"),
            });
        }
        Assert.True(await page.Locator("[data-summary-warning]").IsVisibleAsync());
        Assert.Equal(culture == "th" ? "ข้อควรระวังด้านการผลิต" : "Manufacturing warning",
            await page.Locator("[data-summary-warning]").GetAttributeAsync("aria-label"));
        Assert.False(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await page.Locator("[data-summary-legal]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-summary-show]").IsVisibleAsync(),
            await page.Locator("[data-summary-show]").EvaluateAsync<string>(
                "element => JSON.stringify({display:getComputedStyle(element).display, html:element.outerHTML, parent:element.parentElement.outerHTML})"));
        Assert.False(await page.Locator("[data-summary-hide]").IsVisibleAsync());
        foreach (var link in await page.Locator("[data-summary-legal] a").AllAsync())
        {
            await link.ScrollIntoViewIfNeededAsync();
            Assert.True(await link.EvaluateAsync<bool>(
                "element => { const rect = element.getBoundingClientRect(); return rect.height >= 44 && rect.left >= -1 && rect.right <= innerWidth + 1; }"));
        }
        if (width == 1280)
        {
            await page.Locator("#summary-test-host").ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(Path.GetTempPath(), "legacy-wall-summary-main-1280-collapsed.png"),
            });
        }
        Assert.True(await consultation.IsVisibleAsync());
        Assert.True(await consultation.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
        var leadTime = page.Locator("[data-workflow-lead-time]");
        Assert.True(await leadTime.IsVisibleAsync());
        Assert.Equal(culture == "th" ? "ระยะเวลา 3–5 วันทำการ" : "Lead time 3–5 business days", await leadTime.InnerTextAsync());
        Assert.True(await leadTime.EvaluateAsync<bool>("element => element.getBoundingClientRect().right <= innerWidth"));

        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.True(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await page.Locator("[data-summary-part-details]").IsVisibleAsync());
        var detailRows = dock.Locator("[data-workflow-order-total] > div");
        Assert.Equal(9, await detailRows.CountAsync());
        Assert.Equal(
            culture == "th"
                ? ["รายละเอียดชิ้นงาน", "ขนาด", "ปริมาตร", "พื้นที่ผิว", "ความหนาต่ำสุด", "ยอดรวมย่อย", "ค่าจัดส่ง", "ภาษีมูลค่าเพิ่ม", "รวมทั้งหมด"]
                : ["Part details", "Dimensions", "Volume", "Surface area", "Min. thickness", "Subtotal", "Shipping", "VAT", "Total"],
            await detailRows.Locator("dt").AllTextContentsAsync());
        Assert.Equal("very-long-part-name-for-a-custom-fabrication-project.stl", await detailRows.Nth(0).Locator("dd").InnerTextAsync());
        Assert.Equal("10 × 20 × 30 mm", await detailRows.Nth(1).Locator("dd").InnerTextAsync());
        Assert.Equal("฿12,964.00", await detailRows.Last.Locator("dd").InnerTextAsync());
        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        var detailList = dock.Locator("[data-workflow-order-total]");
        Assert.True(await detailList.EvaluateAsync<bool>("element => document.activeElement === element"),
            "The independently scrollable breakdown needs a keyboard stop after its disclosure.");
        if (width <= 375)
        {
            await page.Keyboard.PressAsync("End");
            await page.WaitForFunctionAsync(
                "() => { const details = document.querySelector('[data-workflow-summary-dock] [data-workflow-order-total]'); return details.scrollTop >= details.scrollHeight - details.clientHeight - 1; }",
                null,
                new PageWaitForFunctionOptions { Timeout = 3000 });
            Assert.True(await detailList.EvaluateAsync<bool>("""
                element => element.lastElementChild.getBoundingClientRect().bottom
                  <= element.getBoundingClientRect().bottom + 1
                """));
            if (width == 320)
            {
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(Path.GetTempPath(), "legacy-web-276-summary-keyboard-bottom-320.png"),
                });
            }
        }
        Assert.True(await page.Locator("[data-summary-legal]").IsVisibleAsync());
        Assert.False(await page.Locator("[data-summary-show]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-summary-hide]").IsVisibleAsync());
        if (width is 320 or 1280)
        {
            await page.Locator("#summary-test-host").ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(Path.GetTempPath(), $"legacy-wall-summary-main-{width}-expanded.png"),
            });
        }
        Assert.True(await leadTime.IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.True(await page.Locator(".instant-quote__configuration-actions button").IsVisibleAsync());
        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.False(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await page.Locator("[data-summary-show]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-summary-legal]").IsVisibleAsync());
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
                      <span class="instant-quote__summary-dock-meta"><span>2 parts</span><span data-workflow-lead-time>Lead time <strong>3–5 business days</strong></span></span><strong>฿12,964.00</strong><span aria-hidden="true">⌄</span></summary>
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
        Assert.True(await page.Locator("[data-workflow-lead-time]").IsVisibleAsync());
        Assert.True(await review.IsVisibleAsync());
        Assert.True(await summary.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
        Assert.True(await summary.EvaluateAsync<bool>("element => getComputedStyle(element).display === 'grid'"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

        await summary.ClickAsync();
        Assert.True(await dock.EvaluateAsync<bool>("element => element.open"));
        Assert.True(await dock.Locator("[data-workflow-order-summary]").IsVisibleAsync());
        Assert.True(await page.Locator("[data-workflow-lead-time]").IsVisibleAsync());
        Assert.True(await review.IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }
}
