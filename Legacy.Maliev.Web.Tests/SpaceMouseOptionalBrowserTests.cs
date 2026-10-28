using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class SpaceMouseOptionalBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", "SpaceMouse help")]
    [InlineData("th", "การใช้งาน SpaceMouse")]
    public async Task DriverUnavailable_KeepsViewerAvailableAndExposesTheLicensedHelpSurface(string culture, string label)
    {
        await using IBrowserContext context = await fixture.Browser.NewContextAsync();
        await using IPage page = await context.NewPageAsync();
        // Avoid contacting a user's actual local driver during the regression.
        await page.RouteAsync("https://127.51.68.120:8181/**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 503,
            ContentType = "text/plain",
            Body = "Driver unavailable in isolated regression",
        }));
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        IResponse? response = await page.GotoAsync($"{origin}/instantquotation/cnc-machining?culture={culture}&tracking=excluded");
        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.iq-camera-toolbar [data-spacemouse-help]') && document.getElementById('model-viewer-canvas')?.getAttribute('data-spacemouse-status') === 'unavailable'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var help = page.Locator(".iq-camera-toolbar [data-spacemouse-help]");
        Assert.Equal(label, await help.GetAttributeAsync("aria-label"));
        Assert.Equal("/help/spacemouse.html", await help.GetAttributeAsync("href"));
        Assert.True(await page.EvaluateAsync<bool>("() => typeof viewer.FitView === 'function' && typeof viewer.ResetView === 'function'"));
        IResponse? helpResponse = await page.GotoAsync($"{origin}/help/spacemouse.html");
        Assert.NotNull(helpResponse);
        Assert.True(helpResponse.Ok);
        Assert.Contains("3Dconnexion 1992 - 2025. All rights reserved.", await page.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
    }
}
