using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class MaterialComparisonDiagnosticsBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task DetailEvent_IsConsentGatedAndEmittedOnceWithStableFields(string culture)
    {
        foreach (var allowed in new[] { false, true })
        {
            await using var context = await fixture.Browser.NewContextAsync();
            await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
                == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
            await using var page = await context.NewPageAsync();
            var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString());
            Assert.NotNull(response);
            Assert.True(response.Ok);
            var reject = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await reject.CountAsync() > 0) { await reject.ClickAsync(); }
            await page.EvaluateAsync(allowed ? "window.malievAnalytics.setConsent('granted')" : "window.malievAnalytics.setConsent('denied')");

            var row = page.Locator("#material-comparison [data-material-row]").First;
            var material = await row.GetAttributeAsync("data-material-key");
            var process = await row.GetAttributeAsync("data-material-group");
            var toggle = row.Locator("[data-material-toggle]");
            await toggle.ClickAsync();
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
            await toggle.ClickAsync();
            await toggle.ClickAsync();
            var events = await page.EvaluateAsync<MaterialEvent[]>("""
                () => window.dataLayer.filter(item => item && item.event === 'material_detail_viewed')
                    .map(item => ({ event: item.event, materialId: item.material_id, process: item.process,
                        locale: item.locale, source: item.source, keys: Object.keys(item).sort() }))
                """);
            if (!allowed)
            {
                Assert.Empty(events);
                await page.EvaluateAsync("window.malievAnalytics.setConsent('granted')");
                Assert.Equal(0, await page.EvaluateAsync<int>("window.dataLayer.filter(item => item?.event === 'material_detail_viewed').length"));
                continue;
            }
            var emitted = Assert.Single(events);
            Assert.Equal("material_detail_viewed", emitted.Event);
            Assert.Equal(material?.ToLowerInvariant(), emitted.MaterialId);
            Assert.Equal(process?.ToLowerInvariant(), emitted.Process);
            Assert.Equal(culture, emitted.Locale);
            Assert.Equal("material_comparison", emitted.Source);
            Assert.Equal(new[] { "event", "locale", "material_id", "process", "source" }, emitted.Keys);
        }
    }

    private sealed class MaterialEvent
    {
        public string? Event { get; set; }
        public string? MaterialId { get; set; }
        public string? Process { get; set; }
        public string? Locale { get; set; }
        public string? Source { get; set; }
        public string[] Keys { get; set; } = [];
    }
}
