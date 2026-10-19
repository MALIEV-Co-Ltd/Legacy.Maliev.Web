using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class ThreeDimensionalPrintingGalleryBrowserTests(PublicContactBrowserFixture fixture)
{
    [Fact]
    public async Task PartGallery_RejectingOptionalCookiesAllowsExpansionAndLoadsDeferredImages()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, "/services/3d-printing?culture=en").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");

        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var toggle = page.Locator("[data-part-gallery-toggle]");
        var gallery = page.Locator("#printing-part-gallery-extra");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.False(await gallery.IsVisibleAsync());

        await toggle.ClickAsync();

        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await gallery.IsVisibleAsync());
        Assert.False(await toggle.IsVisibleAsync());
        Assert.Equal(12, await gallery.Locator("[data-part-gallery-extra]").CountAsync());
        Assert.Equal(0, await gallery.Locator("img[data-src], img[data-srcset]").CountAsync());
        Assert.Equal(0, await gallery.Locator("[data-part-gallery-extra][aria-hidden='true']").CountAsync());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ToleranceComparison_ReflowsAndKeepsDetailsAndTocKeyboardOperable(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = culture == "th" ? "th-TH" : "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(
            new Uri(fixture.Origin, $"/services/3d-printing?culture={culture}").ToString(),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok, $"3D-printing service page returned HTTP {response.Status}.");

        var consent = page.Locator("#cookieConsent");
        if (await consent.IsVisibleAsync())
        {
            await consent.Locator("[data-consent-action='reject']").ClickAsync();
        }
        Assert.Equal(0, await consent.CountAsync());

        var section = page.Locator("#printing-tolerances");
        var grid = section.Locator(".service-tolerance-grid");
        await grid.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        Assert.Equal(4, await grid.Locator(":scope > article.service-tolerance-item").CountAsync());

        foreach (var (width, columns) in new[] { (1280, 4), (390, 2) })
        {
            await page.SetViewportSizeAsync(width, 900);
            Assert.Equal(columns, await grid.EvaluateAsync<int>(
                "element => getComputedStyle(element).gridTemplateColumns.split(' ').length"));
            var rectangles = await grid.EvaluateAsync<double[][]>(
                "grid => [grid, ...grid.querySelectorAll(':scope > article')].map(element => { const r = element.getBoundingClientRect(); return [r.left, r.right, r.top, r.bottom]; })");
            Assert.Equal(5, rectangles.Length);
            for (var index = 1; index < rectangles.Length; index++)
            {
                Assert.True(rectangles[index][1] > rectangles[index][0]);
                Assert.InRange(rectangles[index][0], rectangles[0][0] - 1, rectangles[0][1]);
                Assert.InRange(rectangles[index][1], rectangles[index][0], rectangles[0][1] + 1);
            }
            Assert.InRange(Math.Abs(rectangles[1][2] - rectangles[2][2]), 0, 1);
            if (columns == 4)
            {
                Assert.InRange(Math.Abs(rectangles[1][2] - rectangles[3][2]), 0, 1);
                Assert.InRange(Math.Abs(rectangles[1][2] - rectangles[4][2]), 0, 1);
            }
            else
            {
                Assert.InRange(Math.Abs(rectangles[3][2] - rectangles[4][2]), 0, 1);
                Assert.True(rectangles[3][2] >= rectangles[1][3] - 1);
            }
        }

        var details = section.Locator("details.service-tolerance-details");
        var summary = details.Locator("summary");
        await Assertions.Expect(summary).ToHaveTextAsync(culture == "th"
            ? "อะไรทำให้ค่าคลาดเคลื่อนและการประกอบเปลี่ยนไป?"
            : "What changes these ranges and part fit?");
        Assert.Null(await details.GetAttributeAsync("open"));
        Assert.False(await details.Locator("div > p").First.IsVisibleAsync());
        await summary.FocusAsync();
        Assert.True(await summary.EvaluateAsync<bool>("element => element === document.activeElement"));
        await summary.PressAsync("Enter");
        Assert.NotNull(await details.GetAttributeAsync("open"));
        Assert.True(await details.Locator("div > p").First.IsVisibleAsync());
        await summary.PressAsync("Space");
        Assert.Null(await details.GetAttributeAsync("open"));
        Assert.False(await details.Locator("div > p").First.IsVisibleAsync());

        var toc = page.Locator("nav[data-service-toc]");
        var toggle = toc.Locator("[data-service-toc-toggle]");
        await toggle.ClickAsync();
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        await Assertions.Expect(toc.Locator("[data-service-toc-list] a:focus")).ToHaveCountAsync(1);
        var link = toc.Locator("a[href='#printing-tolerances']");
        await Assertions.Expect(link).ToHaveCountAsync(1);
        await link.PressAsync("Enter");
        Assert.EndsWith("#printing-tolerances", page.Url, StringComparison.Ordinal);
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.Equal("true", await link.GetAttributeAsync("aria-current"));
        await Assertions.Expect(section).ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions
        {
            Timeout = 30000
        });
    }
}
