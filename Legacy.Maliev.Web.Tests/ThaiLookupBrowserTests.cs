using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.Web.Components.Lookups;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

// Uses the existing hosted browser owner, not a second browser/service worker.
// Rendered real component + controlled Catalog replies; does not establish joined provider/persistence acceptance.
[Collection(CncNativeBrowserCollection.Name)]
public sealed class ThaiLookupBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task PostcodeFirstRequiresKeyboardChoiceAndPreservesHouseText(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = culture == "th" ? 375 : 1280, Height = 800 },
        });
        await using var page = await context.NewPageAsync();
        string? body = null;
        await page.RouteAsync("**/lookups/addresses/search", async route =>
        {
            body = route.Request.PostData;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = AddressPage });
        });
        await Load(page, culture);
        await page.Locator("[data-lookup-query]").FillAsync("๑๑๑๒๐");
        await page.Locator("[role=option]").First.WaitForAsync();
        using var request = JsonDocument.Parse(body!);
        Assert.Equal("", request.RootElement.GetProperty("q").GetString());
        Assert.Equal("๑๑๑๒๐", request.RootElement.GetProperty("constraints").GetProperty("postcode").GetString());
        Assert.Equal("", await page.Locator("#province").InputValueAsync());
        await page.Locator("[data-lookup-query]").PressAsync("ArrowDown");
        await page.Locator("[data-lookup-query]").PressAsync("Enter");
        Assert.True(await page.Locator("[data-lookup-preview]").IsVisibleAsync());
        await page.Locator("[data-lookup-apply]").ClickAsync();
        Assert.Equal(culture == "th" ? "นนทบุรี" : "Nonthaburi", await page.Locator("#province").InputValueAsync());
        Assert.Equal("11120", await page.Locator("#postcode").InputValueAsync());
        Assert.Equal("36/1 house and road", await page.Locator("#detail").InputValueAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        var evidence = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "thai-lookup-browser");
        Directory.CreateDirectory(evidence);
        await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, "postcode-review-" + culture + ".png"), FullPage = true });
        await page.Locator("#province").FillAsync("new province");
        Assert.Equal("", await page.Locator("#district").InputValueAsync());
        Assert.Equal("", await page.Locator("#subdistrict").InputValueAsync());
        Assert.Equal("", await page.Locator("#postcode").InputValueAsync());
        Assert.Equal("36/1 house and road", await page.Locator("#detail").InputValueAsync());
    }

    [Fact]
    public async Task LockedAccountMismatchRejectsEntireAutofill()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await page.RouteAsync("**/lookups/addresses/search", route => route.FulfillAsync(new() { ContentType = "application/json", Body = AddressPage }));
        await Load(page, "en");
        await page.Locator("#province").EvaluateAsync("element => { element.value = 'locked province'; element.readOnly = true; }");
        await page.Locator("[data-lookup-query]").FillAsync("11120");
        await page.Locator("[role=option]").First.ClickAsync();
        await page.Locator("[data-lookup-apply]").ClickAsync();
        Assert.Equal("locked province", await page.Locator("#province").InputValueAsync());
        Assert.Equal("", await page.Locator("#district").InputValueAsync());
        Assert.Contains("saved account", await page.Locator("[data-lookup-status]").InnerTextAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompanyProviderFailureRetainsEditableManualEntry()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await page.RouteAsync("**/lookups/companies/search", route => route.FulfillAsync(new() { Status = 503 }));
        await Load(page, "en", "company");
        await page.Locator("#company").FillAsync("example");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-lookup-status]').textContent.includes('unavailable')");
        Assert.Equal("example", await page.Locator("#company").InputValueAsync());
        Assert.True(await page.Locator("#company").IsEditableAsync());
        await page.Locator("#company").FillAsync("corrected company");
        Assert.Equal("corrected company", await page.Locator("#company").InputValueAsync());
    }

    [Fact]
    public async Task AmbiguousPasteNeverGuessesAndKeepsExistingDetailUntilOptIn()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        using var json = JsonDocument.Parse(AddressPage);
        var reply = JsonSerializer.Serialize(new
        {
            datasetVersion = "fixture", originalText = "pasted original", normalizedText = "pasted original",
            outcome = "ambiguous", candidates = json.RootElement.GetProperty("items"), hasMore = true,
            uniqueFields = new { }, detailText = "different parsed house", extractedSpans = Array.Empty<object>(), conflicts = Array.Empty<object>(),
        });
        await page.RouteAsync("**/lookups/addresses/resolve", route => route.FulfillAsync(new() { ContentType = "application/json", Body = reply }));
        await Load(page, "en");
        await page.Locator("[data-lookup-paste]").FillAsync("pasted original");
        await page.Locator("[data-lookup-resolve]").ClickAsync();
        await page.Locator("[role=option]").First.WaitForAsync();
        Assert.Equal("", await page.Locator("#postcode").InputValueAsync());
        Assert.Contains("Narrow", await page.Locator("[data-lookup-status]").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        await page.Locator("[role=option]").First.ClickAsync();
        Assert.Equal("36/1 house and road", await page.Locator("[data-lookup-detail]").InputValueAsync());
        Assert.False(await page.Locator("[data-lookup-replace]").IsCheckedAsync());
        await page.Locator("[data-lookup-apply]").ClickAsync();
        Assert.Equal("36/1 house and road", await page.Locator("#detail").InputValueAsync());
    }

    [Fact]
    public async Task NonThaiCountryPreservesManualValuesAndDoesNotCallLookup()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var calls = 0;
        await page.RouteAsync("**/lookups/**", route => { calls++; return route.FulfillAsync(new() { Status = 503 }); });
        await Load(page, "en");
        await page.Locator("#detail").FillAsync("foreign road");
        await page.Locator("#district").FillAsync("foreign city");
        await page.Locator("#country").FillAsync("Japan");
        await page.Locator("[data-lookup-query]").FillAsync("11120");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-lookup-status]').textContent.includes('Thailand')");
        Assert.Equal(0, calls);
        Assert.Equal("foreign road", await page.Locator("#detail").InputValueAsync());
        Assert.Equal("foreign city", await page.Locator("#district").InputValueAsync());
    }

    [Fact]
    public async Task InteractiveInsertionInitializesAndEnterDoesNotSubmitCustomerForm()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await page.RouteAsync("**/lookups/addresses/search", route => route.FulfillAsync(new() { ContentType = "application/json", Body = AddressPage }));
        await Load(page, "en");
        await page.EvaluateAsync("""
            () => {
                const original = document.querySelector('[data-thai-lookup]');
                const replacement = original.cloneNode(true);
                replacement.removeAttribute('data-lookup-bound');
                original.replaceWith(replacement);
                window.formSubmitted = false;
                document.querySelector('form').addEventListener('submit', event => { event.preventDefault(); window.formSubmitted = true; });
            }
            """);
        await page.WaitForFunctionAsync("() => document.querySelector('[data-thai-lookup]').dataset.lookupBound === 'true'");
        await page.Locator("[data-lookup-query]").FillAsync("11120");
        await page.Locator("[data-lookup-query]").PressAsync("Enter");
        await page.Locator("[role=option]").First.WaitForAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => window.formSubmitted"));
    }

    [Fact]
    public async Task LateResponseCannotReplaceCurrentSearchEvenIfTransportIgnoresAbort()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await Load(page, "en", "company");
        await page.EvaluateAsync("""
            () => {
                window.lookupPending = [];
                window.fetch = () => new Promise(resolve => window.lookupPending.push(resolve));
            }
            """);
        await page.Locator("[data-lookup-query]").FillAsync("old search");
        await page.WaitForFunctionAsync("() => window.lookupPending.length === 1");
        await page.Locator("[data-lookup-query]").FillAsync("new search");
        await page.WaitForFunctionAsync("() => window.lookupPending.length === 2");
        await page.EvaluateAsync("""
            () => window.lookupPending[1]({ ok: true, json: async () => ({ outcome: 'no-match', items: [], hasMore: false }) })
            """);
        await page.WaitForFunctionAsync("() => document.querySelector('[data-lookup-status]').textContent.includes('No matches')");
        await page.EvaluateAsync("""
            () => window.lookupPending[0]({ ok: true, json: async () => ({ outcome: 'matches', items: [{ nameEn: 'stale company' }], hasMore: false }) })
            """);
        Assert.Equal(0, await page.Locator("[role=option]").CountAsync());
        Assert.Equal("manual company", await page.Locator("#company").InputValueAsync());
    }

    [Fact]
    public async Task MissingCompanyNameDoesNotClearExistingCompanyWhenTaxIdIsAvailable()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        await page.RouteAsync("**/lookups/companies/search", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"outcome":"matches","provider":"creden","capability":"suggestion","items":[{"taxId":"0100000000001","nameTh":null,"nameEn":null}],"hasMore":false}""",
        }));
        await Load(page, "en", "company");
        await page.Locator("[data-lookup-query]").FillAsync("example");
        await page.Locator("[role=option]").First.ClickAsync();
        await page.Locator("[data-lookup-apply]").ClickAsync();
        Assert.Equal("manual company", await page.Locator("#company").InputValueAsync());
        Assert.Equal("0100000000001", await page.Locator("#taxId").InputValueAsync());
    }

    private async Task Load(IPage page, string culture, string kind = "address")
    {
        var previous = CultureInfo.CurrentUICulture;
        string component;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var services = new ServiceCollection();
            services.AddLogging(); services.AddLocalization(options => options.ResourcesPath = "Resources");
            using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            component = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var rendered = await renderer.RenderComponentAsync<ThaiLookup>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Id"] = "test", ["Kind"] = kind,
                    ["Fields"] = new Dictionary<string, string> { ["province"] = "province", ["district"] = "district",
                        ["subdistrict"] = "subdistrict", ["postcode"] = "postcode", ["detail"] = "detail", ["country"] = "country",
                        ["company"] = "company", ["taxId"] = "taxId" },
                }));
                return rendered.ToHtmlString();
            });
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        var html = "<!doctype html><meta charset='utf-8'/><form><input type='hidden' name='__RequestVerificationToken' value='fixture-csrf' />"
            + "<input id='province'/><input id='district'/><input id='subdistrict'/><input id='postcode'/>"
            + "<input id='detail' value='36/1 house and road'/><input id='country' value='Thailand'/>"
            + "<input id='company' value='manual company'/><input id='taxId'/>" + component + "</form>";
        await page.RouteAsync("**/fixture-lookup", route => route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html }));
        await page.RouteAsync("**/src/app/js/lookups/thai-lookup.js", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript; charset=utf-8",
            Body = File.ReadAllText(Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", "src", "app", "js", "lookups", "thai-lookup.js")),
        }));
        await page.GotoAsync(new Uri(new Uri(fixture.CncQuotationUrl), "/fixture-lookup").ToString());
    }

    private const string AddressPage = """
        {"datasetVersion":"fixture","items":[
          {"province":{"code":"12","nameTh":"นนทบุรี","nameEn":"Nonthaburi"},
           "district":{"code":"1206","provinceCode":"12","nameTh":"ปากเกร็ด","nameEn":"Pak Kret"},
           "subdistrict":{"code":"120612","districtCode":"1206","nameTh":"คลองข่อย","nameEn":"Khlong Khoi"},"postcode":"11120"},
          {"province":{"code":"12","nameTh":"นนทบุรี","nameEn":"Nonthaburi"},
           "district":{"code":"1206","provinceCode":"12","nameTh":"ปากเกร็ด","nameEn":"Pak Kret"},
           "subdistrict":{"code":"120611","districtCode":"1206","nameTh":"บางพลับ","nameEn":"Bang Phlap"},"postcode":"11120"}],
         "hasMore":false,"nextCursor":null}
        """;
}
