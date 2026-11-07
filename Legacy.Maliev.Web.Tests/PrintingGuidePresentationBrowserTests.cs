using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class PrintingGuidePresentationBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", 320)]
    [InlineData("th", 320)]
    [InlineData("en", 390)]
    [InlineData("th", 390)]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    public async Task PrintingGuide_RendersAccessibleFileBadgesAndActions(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/*", request => new Uri(request.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? request.ContinueAsync() : request.AbortAsync());
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) await consent.ClickAsync();

        var title = page.Locator("h1#printing-title");
        Assert.Equal(culture == "th" ? "รับปริ้น 3D และรับพิมพ์ 3 มิติ อัปโหลดไฟล์ประเมินราคา" : "3D Printing Service in Thailand for Prototypes and Production Parts", await title.InnerTextAsync());
        Assert.Equal(culture == "th" ? 2 : 0, await title.Locator(".printing-title-nowrap").CountAsync());
        var badges = page.Locator("#printing-quote-guide ul.printing-file-badges");
        Assert.True(await badges.IsVisibleAsync());
        Assert.Equal(new[] { "STL", "OBJ", "3MF", "GLB", "GLTF", "STEP / STP", "IGES / IGS" }, await badges.Locator("li").AllTextContentsAsync());
        Assert.Equal("flex", await badges.EvaluateAsync<string>("element => getComputedStyle(element).display"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        var action = page.Locator("#printing-quote-guide [data-quote-route='instant']").First;
        Assert.Equal("/instantquotation/3d-printing", (await action.GetAttributeAsync("href"))?.ToLowerInvariant());
        await action.FocusAsync();
        Assert.True(await action.EvaluateAsync<bool>("element => document.activeElement === element"));
        Assert.Contains(culture == "th" ? "อาจต้องซ่อม" : "Mesh files may need repair", await page.Locator("#printing-quote-guide .service-note").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        var screenshots = Environment.GetEnvironmentVariable("MALIEV_PRINTING_GUIDE_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(screenshots))
            await page.Locator("#printing-quote-guide").ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"printing-guide-{culture}-{width}.png") });
    }
}
