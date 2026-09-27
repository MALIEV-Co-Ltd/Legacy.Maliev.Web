using System.Globalization;
using System.Net;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class InstantQuotationLocalizedValidationBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData(320, 700, "en", ColorScheme.Light)]
    [InlineData(375, 667, "th", ColorScheme.Dark)]
    [InlineData(820, 800, "th", ColorScheme.Light)]
    [InlineData(1280, 800, "en", ColorScheme.Dark)]
    public async Task RejectedCustomerFieldsShowReasonSpecificLocalizedGuidance(
        int width, int height, string culture, ColorScheme colorScheme)
    {
        var html = await RenderCustomerAsync(culture);
        var decoded = WebUtility.HtmlDecode(html);
        var thai = culture == "th";
        Assert.Contains(thai ? "เลขประจำตัวผู้เสียภาษีไทยต้องมีตัวเลข 13 หลัก" : "Thai tax ID must contain exactly 13 digits.", decoded, StringComparison.Ordinal);
        Assert.Contains(thai ? "ชื่ออาคารหรือองค์กรต้องมีความยาวไม่เกิน 256 ตัวอักษร" : "Building or organization name must be 256 characters or fewer.", decoded, StringComparison.Ordinal);
        Assert.Contains(thai ? "ช่องนี้มีรายละเอียดที่อยู่" : "This field contains address details.", decoded, StringComparison.Ordinal);
        if (thai)
        {
            Assert.DoesNotContain("Thai tax ID must contain exactly 13 digits.", decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("Building or organization name must be 256 characters or fewer.", decoded, StringComparison.Ordinal);
        }

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
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#instant-quote-files')?._blazorInputFileNextFileId !== undefined");

        await page.EvaluateAsync("""
            markup => {
              const fixture = document.createElement('div');
              fixture.className = 'instant-quote__workflow';
              fixture.dataset.workflowState = 'customer';
              fixture.innerHTML = markup;
              document.querySelector('main').appendChild(fixture);
            }
            """, html);

        var tax = page.Locator("#instant-quote-taxnumber").Last;
        var building = page.Locator("#instant-quote-billing-building").Last;
        var shipping = page.Locator("#instant-quote-shipping-building").Last;
        Assert.Equal("true", await tax.GetAttributeAsync("aria-invalid"));
        Assert.Equal("true", await building.GetAttributeAsync("aria-invalid"));
        Assert.Equal("true", await shipping.GetAttributeAsync("aria-invalid"));
        Assert.True(await page.Locator(".instant-quote__validation-summary").Last.IsVisibleAsync());
        Assert.True(await page.Locator("#instant-quote-taxnumber-error").Last.IsVisibleAsync());
        Assert.True(await page.Locator("#instant-quote-billing-building-error").Last.IsVisibleAsync());
        Assert.True(await page.Locator("#instant-quote-shipping-building-error").Last.IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        if (Environment.GetEnvironmentVariable("MALIEV_WEB_VALIDATION_SCREENSHOTS") == "1")
        {
            await page.Locator(".instant-quote__validation-summary").Last.ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(Path.GetTempPath(), $"legacy-web-276-validation-{culture}-{width}.png"),
                FullPage = true,
            });
        }
        await page.Locator(".instant-quote__validation-summary a[href='#instant-quote-taxnumber']").Last.ClickAsync();
        Assert.Equal("#instant-quote-taxnumber", await page.EvaluateAsync<string>("() => location.hash"));
    }

    private static async Task<string> RenderCustomerAsync(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IWebHostEnvironment>(_ => new TestWebHostEnvironment
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name ?? "Legacy.Maliev.Web",
            })
            .BuildServiceProvider();
        if (culture == "th")
        {
            var localizer = services.GetRequiredService<IStringLocalizer<ThreeDimensionalPrintingEstimateContent>>();
            Assert.Equal("เลขประจำตัวผู้เสียภาษีไทยต้องมีตัวเลข 13 หลัก", localizer["Thai tax ID must contain exactly 13 digits."].Value);
        }
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var model = InstantQuotationCustomerDisplayModel.Empty with
            {
                TaxNumber = "123",
                BillingBuilding = string.Empty,
                ShippingBuilding = "Warehouse, 99 Road, Bangkok 10000",
                ShipToBillingAddress = false,
                InvalidFields = ["TaxNumber", "BillingBuilding", "ShippingBuilding"],
                OverlengthBuildingFields = ["BillingBuilding"],
            };
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Model"] = model });
            var output = await renderer.RenderComponentAsync<InstantQuotationCustomerForm>(parameters);
            return output.ToHtmlString();
        });
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
