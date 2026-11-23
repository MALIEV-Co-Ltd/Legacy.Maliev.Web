using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class NoWeaponsResponsiveBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("th", 320)]
    [InlineData("en", 320)]
    [InlineData("th", 390)]
    [InlineData("en", 390)]
    [InlineData("th", 1440)]
    [InlineData("en", 1440)]
    [InlineData("", 390)]
    public async Task Policy_RemainsReadableAndDiscoverableWithoutJavaScript(string culture, int width)
    {
        Assert.True(fixture.Origin.IsLoopback);
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            JavaScriptEnabled = false,
            Locale = "th-TH",
            ViewportSize = new() { Width = width, Height = 900 },
        });
        await context.RouteAsync("**/*", route =>
            route.Request.Method == "GET" &&
            Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var uri) &&
            uri.GetLeftPart(UriPartial.Authority) == fixture.Origin.GetLeftPart(UriPartial.Authority)
                ? route.ContinueAsync()
                : route.AbortAsync());
        await using var page = await context.NewPageAsync();
        var path = "/no-weapons" + (culture.Length == 0 ? string.Empty : "?culture=" + culture);
        var response = await page.GotoAsync(new Uri(fixture.Origin, path).ToString());
        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        var thai = culture != "en";
        Assert.Equal(1, await page.Locator("main h1").CountAsync());
        Assert.Contains(thai ? "ไม่รับผลิตอาวุธ" : "Weapons manufacturing policy", await page.TitleAsync());
        var article = await page.Locator("article").InnerTextAsync();
        Assert.Contains(thai ? "ไม่มีใบอนุญาต" : "does not hold", article);
        Assert.Contains(thai ? "ตามที่กำหนดในกฎกระทรวง" : "as prescribed by ministerial regulation", article);
        Assert.Contains(thai ? "ไม่ใช่ฉบับรวมการแก้ไขล่าสุด" : "not a consolidated current edition", article);
        Assert.Equal(1, await page.Locator("footer a[href='/no-weapons']").CountAsync());
        Assert.Equal(1, await page.Locator("a[href='/no-weapons']").CountAsync());
        Assert.Equal(0, await page.Locator("main a[href*='bit.ly']").CountAsync());
        Assert.Contains("/no-weapons", await page.Locator("link[rel='canonical']").GetAttributeAsync("href"));
        Assert.Equal(await page.TitleAsync(), await page.Locator("meta[property='og:title']").GetAttributeAsync("content"));

        var overflow = await page.EvaluateAsync<string>("""
            () => JSON.stringify(Array.from(document.querySelectorAll('body *'))
              .filter(element => element.getClientRects().length && element.getBoundingClientRect().right > innerWidth + 1)
              .slice(0, 12).map(element => ({ tag: element.tagName,
                className: typeof element.className === 'string' ? element.className : '',
                right: Math.round(element.getBoundingClientRect().right) })))
            """);
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
            $"Horizontal overflow: {overflow}");

        foreach (var section in new[] { "policy", "law", "components", "questions", "references" })
        {
            Assert.Equal(1, await page.Locator("section#" + section).CountAsync());
            Assert.Equal(1, await page.Locator(".legal-toc a[href='#" + section + "']").CountAsync());
        }

        var pdfPath = await page.Locator("a[download='arms-factory-act-2550.pdf']").GetAttributeAsync("href");
        Assert.Equal("/documents/arms-factory-act-2550.pdf", pdfPath);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = fixture.Origin,
            Timeout = TimeSpan.FromSeconds(10),
        };
        using var pdf = await client.GetAsync(pdfPath);
        Assert.True(pdf.IsSuccessStatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }
}
