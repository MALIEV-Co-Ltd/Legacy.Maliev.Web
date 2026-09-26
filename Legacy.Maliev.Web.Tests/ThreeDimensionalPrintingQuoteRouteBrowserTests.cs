using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class ThreeDimensionalPrintingQuoteRouteBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task QuoteChoice_EmitsOnlyScalarIntentAfterConsentWithoutCreatingLead(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await page.GotoAsync($"{origin}/services/3d-printing?culture={culture}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var routes = page.Locator("[data-quote-route][data-quote-placement]");
        Assert.Equal(10, await routes.CountAsync());
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = "/src/app/js/inquiry-pages.js" });
        await ClickWithoutNavigatingAsync(page.Locator("[data-quote-route='instant'][data-quote-placement='hero']"));
        await ClickWithoutNavigatingAsync(page.Locator("[data-quote-route='engineering'][data-quote-placement='hero']"));

        Assert.Equal(0, await page.EvaluateAsync<int>("() => dataLayer.filter(x => x.event === 'quote_route_selected').length"));
        await page.EvaluateAsync("() => malievAnalytics.setConsent('granted')");
        JsonElement events = await page.EvaluateAsync<JsonElement>(
            "() => dataLayer.filter(x => x.event === 'quote_route_selected')");
        Assert.Equal(2, events.GetArrayLength());
        AssertRouteEvent(events[0], "instant", culture);
        AssertRouteEvent(events[1], "engineering", culture);
        Assert.Equal(0, await page.EvaluateAsync<int>(
            "() => dataLayer.filter(x => x.event === 'request_quote' || x.event === 'generate_lead').length"));
    }

    private static async Task ClickWithoutNavigatingAsync(ILocator link) => await link.EvaluateAsync(
        "element => { element.addEventListener('click', event => event.preventDefault(), { once: true }); element.click(); }");

    private static void AssertRouteEvent(JsonElement item, string route, string locale)
    {
        Assert.Equal("quote_route_selected", item.GetProperty("event").GetString());
        Assert.Equal("3d_printing", item.GetProperty("service_id").GetString());
        Assert.Equal(route, item.GetProperty("route").GetString());
        Assert.Equal("hero", item.GetProperty("placement").GetString());
        Assert.Equal(locale, item.GetProperty("locale").GetString());
        Assert.Equal(
            ["event", "locale", "placement", "route", "service_id"],
            item.EnumerateObject().Select(property => property.Name)
                .Where(name => !string.Equals(name, "$id", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal));
    }
}
