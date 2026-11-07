using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class HomeSourceShowcaseHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task Home_RendersLocalizedShowcaseAndAttributedReviews(string culture, bool active)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Home", active ? "true" : "false"));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"service-showcase\"", html, StringComparison.Ordinal);
        Assert.Contains("feature-print-title", html, StringComparison.Ordinal);
        Assert.Contains("feature-cnc-title", html, StringComparison.Ordinal);
        Assert.Contains("feature-scan-title", html, StringComparison.Ordinal);
        Assert.Contains("feature-design-title", html, StringComparison.Ordinal);
        Assert.Contains("/src/images/services/injection-molding/injection-mold-and-parts-photo.webp", html, StringComparison.Ordinal);
        Assert.Contains("width=\"4016\" height=\"4016\"", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ชิ้นงานฉีดพลาสติกสีน้ำเงินพร้อมแม่พิมพ์" : "Blue injection-molded components beside their mold", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "สูงสุด 1,000 ชิ้น น้ำหนักฉีดสูงสุด 50 กรัม" : "Up to 1,000 parts; shot weights up to 50 g.", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "พลาสติกที่หลอมไม่เกิน 350 °C เสนอราคาหลังประเมิน" : "Plastics melting at up to 350 °C; project quote after review.", html, StringComparison.Ordinal);
        Assert.Contains($"/Services/Low-Volume-Injection-Molding?culture={culture}", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "เสียงจากลูกค้าของเรา" : "In our customers’ words", html, StringComparison.Ordinal);
        Assert.Contains("https://maps.app.goo.gl/rVg3aG8nZcK1k5fu5", html, StringComparison.Ordinal);
        Assert.Contains("Fantastic service and fast.", html, StringComparison.Ordinal);
        Assert.Contains("https://www.google.com/maps/search/MALIEV+Co+Ltd+Nonthaburi", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ให้คะแนน 5 ดาวโดยไม่มีข้อความรีวิว" : "Five-star rating without a written review", html, StringComparison.Ordinal);
    }
}

[Collection(PublicContactBrowserCollection.Name)]
public sealed class HomeSourceShowcaseBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", 390)]
    [InlineData("th", 390)]
    [InlineData("en", 1440)]
    [InlineData("th", 1440)]
    public async Task Showcase_IsResponsiveAndFactsWorkWithReducedMotion(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(fixture.Origin, $"/?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.True(response.Ok);
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) { await consent.ClickAsync(); }

        var cards = page.Locator(".service-showcase .service-feature");
        Assert.Equal(4, await cards.CountAsync());
        Assert.Equal(12, await cards.Locator(".fact-toggle").CountAsync());
        await cards.First.ScrollIntoViewIfNeededAsync();
        var first = cards.First.Locator(".fact-disclosure").First;
        await first.Locator(".fact-summary").ClickAsync();
        Assert.True(await first.Locator(".fact-toggle").IsCheckedAsync());
        Assert.True(await first.Locator(".fact-popover").IsVisibleAsync());
        await first.Locator(".fact-toggle").FocusAsync();
        await page.Keyboard.PressAsync("Space");
        Assert.False(await first.Locator(".fact-toggle").IsCheckedAsync());
        Assert.Equal(0, await page.Locator(".service-intro-playing, .service-intro-effect").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.Equal(5, await page.Locator(".landing-testimonial-grid figure").CountAsync());
        Assert.Equal(4, await page.Locator(".landing-testimonial-grid blockquote").CountAsync());
        Assert.Equal(1, await page.Locator(".landing-testimonial-grid .testimonial-rating-only").CountAsync());
        var injectionCard = page.Locator(".landing-service-card--injection");
        await injectionCard.ScrollIntoViewIfNeededAsync();
        var injectionImage = injectionCard.Locator("img");
        Assert.EndsWith("/injection-mold-and-parts-photo.webp", await injectionImage.GetAttributeAsync("src"), StringComparison.Ordinal);
        Assert.Equal(culture == "th" ? "ชิ้นงานฉีดพลาสติกสีน้ำเงินพร้อมแม่พิมพ์" : "Blue injection-molded components beside their mold", await injectionImage.GetAttributeAsync("alt"));
        await injectionImage.ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("() => { const image = document.querySelector('.landing-service-card--injection img'); return image?.complete && image.naturalWidth > 0; }");
        await injectionImage.EvaluateAsync("image => image.decode()");
        Assert.True(await injectionImage.EvaluateAsync<bool>("image => image.naturalWidth === 4016 && image.naturalHeight === 4016"));
        Assert.Equal("50% 50%", await injectionImage.EvaluateAsync<string>("image => getComputedStyle(image).objectPosition"));
        Assert.Equal(2, await injectionCard.Locator(".landing-service-facts > div").CountAsync());
        Assert.Equal($"/Services/Low-Volume-Injection-Molding?culture={culture}", await injectionCard.Locator("a").GetAttributeAsync("href"));

        var screenshotDirectory = Environment.GetEnvironmentVariable("MALIEV_HOME_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
            await page.EvaluateAsync("""
                async () => {
                    for (let y = 0; y < document.documentElement.scrollHeight; y += innerHeight) {
                        scrollTo(0, y);
                        await new Promise(resolve => setTimeout(resolve, 50));
                    }
                    await Promise.all([...document.images].map(image => image.decode().catch(() => {})));
                    scrollTo(0, 0);
                }
                """);
            await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotDirectory, $"home-showcase-{culture}-{width}.png"), FullPage = true });
        }
    }
}
