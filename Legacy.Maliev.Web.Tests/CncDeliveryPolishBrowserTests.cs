using System.Text.Json;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

// Delivery: 6c00db5; diagnostic native policy: bbba5046/0e7c4898;
// finalized-batch pricing: fa5b6fe4/34f5de92; CSS-pixel labels: 5539482c.
[Collection(CncNativeBrowserCollection.Name)]
public sealed class CncDeliveryPolishBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task NativeDiagnosticImport_RetainsDrawingPolishWithoutAuthorizingSetups()
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1495, Height = 900 },
        });
        await using var page = await context.NewPageAsync();
        await OpenOriginalPartAsync(page);
        var actual = await page.EvaluateAsync<JsonElement>("""
            () => {
                const drawing = document.querySelector('[data-cnc-drawing-control]');
                const style = getComputedStyle(drawing);
                const item = utils.GetItem(utils.GetActiveId());
                return { status: item.cncStatus, error: item.cncQuoteError,
                    icons: document.querySelectorAll('.iq-cnc-section-title > i[aria-hidden="true"]').length,
                    left: parseFloat(style.paddingLeft), right: parseFloat(style.paddingRight),
                    drawingWidth: drawing.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight),
                    drawingButtonWidth: drawing.querySelector('button').getBoundingClientRect().width,
                    hasSetup: !!document.querySelector('#cnc-setup-controls [data-cnc-setup-number]'),
                    markers: viewer.GetCncOverlayState().setupMarkers.length,
                    stock: document.querySelector('[data-cnc-metric="stock-form"]').textContent };
            }
            """);
        Assert.True(actual.GetProperty("icons").GetInt32() >= 3, actual.ToString());
        Assert.InRange(actual.GetProperty("left").GetDouble(), 10, 16);
        Assert.InRange(actual.GetProperty("right").GetDouble(), 10, 16);
        Assert.True(actual.GetProperty("drawingButtonWidth").GetDouble() >= actual.GetProperty("drawingWidth").GetDouble() - 1, actual.ToString());
        Assert.Equal("review_required", actual.GetProperty("status").GetString());
        Assert.Equal("native_machining_role_unverified", actual.GetProperty("error").GetString());
        Assert.False(actual.GetProperty("hasSetup").GetBoolean());
        Assert.Equal(0, actual.GetProperty("markers").GetInt32());
        Assert.Equal("—", actual.GetProperty("stock").GetString()?.Trim());
    }

    [Fact]
    public async Task NativeDiagnosticImport_ExcludesDeliveryAndTotalsWhileCalculatorRetainsSourceRates()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await OpenOriginalPartAsync(page);
        var actual = await page.EvaluateAsync<JsonElement>("""
            () => {
                const item = utils.GetItem(utils.GetActiveId());
                return { status: item.cncStatus, error: item.cncQuoteError,
                    hasQuote: !!item.cncQuote, hasValidatedPlan: !!item.cncValidatedPlan,
                    pricedDelivery: CncCustomerShippingForPricedItems(utils.GetAllItemIds()),
                    calculator: [[0,0],[4800,0],[0,25000],[20000,0]]
                        .map(([grams,cm3]) => CncCustomerShippingThb(grams,cm3)),
                    itemsSubtotal: latestOrderTotal.itemsSubtotal, shipping: latestOrderTotal.shipping,
                    vat: latestOrderTotal.vat, total: latestOrderTotal.finalOrderPrice };
            }
            """);
        Assert.Equal("review_required", actual.GetProperty("status").GetString());
        Assert.Equal("native_machining_role_unverified", actual.GetProperty("error").GetString());
        Assert.False(actual.GetProperty("hasQuote").GetBoolean());
        Assert.False(actual.GetProperty("hasValidatedPlan").GetBoolean());
        foreach (var property in new[] { "pricedDelivery", "itemsSubtotal", "shipping", "vat", "total" })
        {
            Assert.Equal(0, actual.GetProperty(property).GetDouble());
        }

        var inputs = new[] { (0d, 0d), (4800d, 0d), (0d, 25000d), (20000d, 0d) };
        Assert.Equal(inputs.Select(input => ShippingCalculator.CustomerShippingThb(input.Item1, input.Item2)),
            actual.GetProperty("calculator").EnumerateArray().Select(value => value.GetDouble()));
        await Assertions.Expect(page.Locator("#cnc-quotation-result")).Not.ToContainTextAsync("100.00 THB");
    }

    [Fact]
    public async Task SetupLabelRenderer_RetainsLaterOriginalCssPixelContractWithoutPricingAuthority()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await OpenOriginalPartAsync(page);
        // Original InstantQuotationPreviewShadingTests uses this renderer boundary directly.
        // Overlay input is presentation data, never a validated manufacturing plan.
        var actual = await page.EvaluateAsync<JsonElement>("""
            () => {
                viewer.SetCncOverlayPlan(
                    {stockShape:'block',stockSizeMm:{x:12,y:32,z:42}},
                    [{id:'setup-1',number:1,direction:{x:0,y:0,z:1}}], [],
                    {partId:utils.GetActiveId(),topologyDegraded:false,surfaceClusters:[]});
                const item = utils.GetItem(utils.GetActiveId());
                return {marker:viewer.GetCncOverlayState().setupMarkers[0],
                    status:item.cncStatus,hasQuote:!!item.cncQuote,
                    hasValidatedPlan:!!item.cncValidatedPlan,total:latestOrderTotal.finalOrderPrice};
            }
            """);
        var marker = actual.GetProperty("marker");
        Assert.Equal(96, marker.GetProperty("labelPixelWidth").GetDouble());
        Assert.Equal(32, marker.GetProperty("labelPixelHeight").GetDouble());
        Assert.Equal(12, marker.GetProperty("labelCssFontPx").GetDouble());
        Assert.Equal("review_required", actual.GetProperty("status").GetString());
        Assert.False(actual.GetProperty("hasQuote").GetBoolean());
        Assert.False(actual.GetProperty("hasValidatedPlan").GetBoolean());
        Assert.Equal(0, actual.GetProperty("total").GetDouble());
    }

    [Fact]
    public async Task OriginalPresentationBoundary_RetainsDisabledSetupSizingAndThaiBlockTerminology()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await OpenOriginalPartAsync(page);
        // Original UnavailableSetupControl_IsVisiblyDisabledAndExplainsWhy renders
        // this presentation input directly. It never creates a manufacturing plan.
        var actual = await page.EvaluateAsync<JsonElement>("""
            () => {
                RenderCncSetupControls([{id:'setup-1',number:1}]);
                const button = document.querySelector('#cnc-setup-controls button');
                const height = button.getBoundingClientRect().height;
                const font = parseFloat(getComputedStyle(button).fontSize);
                ApplyCncOverlayUiState(viewer.GetCncOverlayState());
                const item = utils.GetItem(utils.GetActiveId());
                return {height,font,
                    rootFont:parseFloat(getComputedStyle(document.documentElement).fontSize),
                    controlsHidden:document.querySelector('#cnc-setup-controls').hidden,
                    disabled:button.disabled,reason:button.title,
                    stockTerm:CncReviewStockType({stockShape:'block'}),
                    status:item.cncStatus,hasQuote:!!item.cncQuote,
                    hasValidatedPlan:!!item.cncValidatedPlan,total:latestOrderTotal.finalOrderPrice};
            }
            """);
        Assert.InRange(actual.GetProperty("height").GetDouble(), 31.5, 32.5);
        Assert.InRange(actual.GetProperty("font").GetDouble(),
            actual.GetProperty("rootFont").GetDouble() * .72 - .1,
            actual.GetProperty("rootFont").GetDouble() * .72 + .1);
        Assert.True(actual.GetProperty("disabled").GetBoolean());
        Assert.True(actual.GetProperty("controlsHidden").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(actual.GetProperty("reason").GetString()));
        Assert.Equal("ก้อนสี่เหลี่ยม", actual.GetProperty("stockTerm").GetString());
        Assert.Equal("review_required", actual.GetProperty("status").GetString());
        Assert.False(actual.GetProperty("hasQuote").GetBoolean());
        Assert.False(actual.GetProperty("hasValidatedPlan").GetBoolean());
        Assert.Equal(0, actual.GetProperty("total").GetDouble());
    }

    private async Task OpenOriginalPartAsync(IPage page)
    {
        // Existing isolated upload boundary; the CAD worker and planner remain real.
        await page.RouteAsync("**/*", async route =>
        {
            if (route.Request.Url.Contains("Handler=UploadFile", StringComparison.OrdinalIgnoreCase))
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = "{\"success\":true,\"path\":\"runtime-test/part.step\",\"receipt\":\"isolated-runtime-test-receipt\"}",
                });
            }
            else await route.ContinueAsync();
        });
        await page.GotoAsync(fixture.CncQuotationUrl.Replace("culture=en", "culture=th", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => !!window.viewer && typeof viewer.ParseFile === 'function'");
        var part = Path.GetFullPath(Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(), "..",
            "Legacy.Maliev.Web.Tests", "TestAssets", "Cnc", "high-confidence-block-10x30x40.step"));
        await page.SetInputFilesAsync("#file-input-hidden", part);
        await page.WaitForFunctionAsync("""
            () => {
                const item = utils.GetItem(utils.GetActiveId());
                return item && ['finalized', 'review_required', 'failed'].includes(item.cncStatus);
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 120000 });
    }
}
