using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

// Original 6c00db5489b025f3a6156d8afa78018617d050b9, with later source setup-label sizing.
[Collection(CncNativeBrowserCollection.Name)]
public sealed class CncDeliveryPolishBrowserTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task CncConfigurator_PolishesRequirementsAndSetupControls()
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
                const button = document.querySelector('#cnc-setup-controls [data-cnc-setup-number]');
                const marker = viewer.GetCncOverlayState().setupMarkers[0];
                const item = utils.GetItem(utils.GetActiveId());
                return { status: item.cncStatus, error: item.cncQuoteError,
                    icons: document.querySelectorAll('.iq-cnc-section-title > i[aria-hidden="true"]').length,
                    left: parseFloat(style.paddingLeft), right: parseFloat(style.paddingRight),
                    drawingWidth: drawing.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight),
                    drawingButtonWidth: drawing.querySelector('button').getBoundingClientRect().width,
                    font: button ? parseFloat(getComputedStyle(button).fontSize) : null,
                    height: button ? button.getBoundingClientRect().height : null,
                    labelWidth: marker?.labelPixelWidth, labelHeight: marker?.labelPixelHeight,
                    labelFont: marker?.labelCssFontPx,
                    stock: document.querySelector('[data-cnc-metric="stock-form"]').textContent };
            }
            """);
        Assert.True(actual.GetProperty("icons").GetInt32() >= 3, actual.ToString());
        Assert.InRange(actual.GetProperty("left").GetDouble(), 10, 16);
        Assert.InRange(actual.GetProperty("right").GetDouble(), 10, 16);
        Assert.True(actual.GetProperty("drawingButtonWidth").GetDouble() >= actual.GetProperty("drawingWidth").GetDouble() - 1, actual.ToString());
        Assert.True(actual.GetProperty("font").ValueKind == JsonValueKind.Number, actual.ToString());
        Assert.InRange(actual.GetProperty("font").GetDouble(), 11, 13);
        Assert.InRange(actual.GetProperty("height").GetDouble(), 30, 34);
        Assert.Equal(96, actual.GetProperty("labelWidth").GetDouble());
        Assert.Equal(32, actual.GetProperty("labelHeight").GetDouble());
        Assert.Equal(12, actual.GetProperty("labelFont").GetDouble());
        Assert.Contains("ก้อนสี่เหลี่ยม", actual.GetProperty("stock").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CncAggregate_AddsCustomerShippingToVisibleTaxableTotal()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await OpenOriginalPartAsync(page);
        var actual = await page.EvaluateAsync<JsonElement>("""
            () => {
                const item = utils.GetItem(utils.GetActiveId());
                const supplierLines = item.cncQuote?.lineItems.filter(line => line.code === 'shipping');
                return { status: item.cncStatus, error: item.cncQuoteError,
                    quotePrice: item.cncQuote?.estimatedPriceBeforeVat,
                    supplierShipping: item.cncQuote?.stockPlan.supplierShippingBeforeVat,
                    supplierLineCount: supplierLines?.length, supplierLineAmount: supplierLines?.[0]?.amountBeforeVat,
                    itemsSubtotal: latestOrderTotal.itemsSubtotal, shipping: latestOrderTotal.shipping,
                    vat: latestOrderTotal.vat, total: latestOrderTotal.finalOrderPrice };
            }
            """);
        Assert.True(actual.GetProperty("status").GetString() == "finalized", actual.ToString());
        var subtotal = actual.GetProperty("itemsSubtotal").GetDouble();
        Assert.True(subtotal > 0, actual.ToString());
        Assert.Equal(subtotal, actual.GetProperty("quotePrice").GetDouble());
        Assert.Equal(200, actual.GetProperty("supplierShipping").GetDouble());
        Assert.Equal(1, actual.GetProperty("supplierLineCount").GetInt32());
        Assert.Equal(200, actual.GetProperty("supplierLineAmount").GetDouble());
        Assert.Equal(100, actual.GetProperty("shipping").GetDouble());
        var vat = Math.Ceiling(((subtotal + 100) * 0.07) / 10) * 10;
        Assert.Equal(vat, actual.GetProperty("vat").GetDouble());
        Assert.Equal(subtotal + 100 + vat, actual.GetProperty("total").GetDouble());
        await Assertions.Expect(page.Locator("#cnc-quotation-result")).ToContainTextAsync("ค่าจัดส่งโดยประมาณ");
        await Assertions.Expect(page.Locator("#cnc-quotation-result")).ToContainTextAsync("100.00 THB");
    }

    private async Task OpenOriginalPartAsync(IPage page)
    {
        // Same isolated upload response as CncNativeImportBrowserTests; the CAD worker and planner remain real.
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
