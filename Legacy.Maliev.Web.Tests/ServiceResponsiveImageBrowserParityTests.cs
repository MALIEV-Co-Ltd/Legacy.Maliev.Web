using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class ServiceResponsiveImageBrowserParityTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("/services/3d-printing", 1672, 941)]
    [InlineData("/services/cnc-machining", 1672, 941)]
    [InlineData("/services/3d-scanning", 1672, 941)]
    [InlineData("/services/custom-manufacturing", 1536, 1024)]
    [InlineData("/services/3d-design", 1536, 1024)]
    [InlineData("/services/silicone-casting", 1536, 1024)]
    [InlineData("/services/finishing-and-color", 1536, 1024)]
    [InlineData("/services/low-volume-injection-molding", 2172, 724)]
    public async Task MobileServiceImages_SelectResponsiveCandidatesThatResolveFromSiteRoot(string route, int width, int height)
    {
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        foreach (var culture in new[] { "en", "th" })
        {
            await using IBrowserContext context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = culture == "th" ? "th-TH" : "en-US",
                ViewportSize = new ViewportSize { Width = 390, Height = 844 },
                DeviceScaleFactor = 1,
            });
            await using IPage page = await context.NewPageAsync();
            IResponse? response = await page.GotoAsync(
                $"{origin}{route}?culture={culture}&tracking=excluded",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
            Assert.NotNull(response);
            Assert.True(response.Ok, $"{route} returned HTTP {response.Status}.");
            await page.WaitForFunctionAsync(
                "() => { const image = document.querySelector('.service-hero-media'); return image?.complete && image.naturalWidth > 0; }",
                null,
                new PageWaitForFunctionOptions { Timeout = 30000 });
            JsonElement result = await page.EvaluateAsync<JsonElement>(
                """
                () => {
                    const image = document.querySelector('.service-hero-media');
                    const preload = document.querySelector('link[rel="preload"][as="image"]');
                    const rawCandidates = Array.from(document.querySelectorAll('link[rel="preload"][as="image"], img[srcset]'))
                        .flatMap(element => ['imagesrcset', 'srcset'].flatMap(attribute =>
                            (element.getAttribute(attribute) || '').split(',').filter(value => value.trim())))
                        .map(value => value.trim().split(/\s+/)[0]);
                    return {
                        culture: document.documentElement.lang,
                        selected: image.currentSrc,
                        loading: image.getAttribute('loading'),
                        priority: image.getAttribute('fetchpriority'),
                        width: Number(image.getAttribute('width')),
                        height: Number(image.getAttribute('height')),
                        imageSet: image.getAttribute('srcset'),
                        imageSizes: image.getAttribute('sizes'),
                        preloadSet: preload?.getAttribute('imagesrcset'),
                        preloadSizes: preload?.getAttribute('imagesizes'),
                        preloadPriority: preload?.getAttribute('fetchpriority'),
                        rawCandidates,
                        candidateUrls: rawCandidates.map(value => new URL(value, document.baseURI).href)
                    };
                }
                """);
            Assert.Equal(culture, result.GetProperty("culture").GetString());
            if (route == "/services/cnc-machining" && culture == "th")
            {
                Assert.Contains("รับงาน CNC ตามแบบทั่วประเทศไทย", await page.TitleAsync(), StringComparison.Ordinal);
            }
            Assert.Equal("eager", result.GetProperty("loading").GetString());
            Assert.Equal("high", result.GetProperty("priority").GetString());
            Assert.Equal(width, result.GetProperty("width").GetInt32());
            Assert.Equal(height, result.GetProperty("height").GetInt32());
            Assert.Equal(result.GetProperty("imageSet").GetString(), result.GetProperty("preloadSet").GetString());
            Assert.Equal("100vw", result.GetProperty("imageSizes").GetString());
            Assert.Equal("100vw", result.GetProperty("preloadSizes").GetString());
            Assert.Equal("high", result.GetProperty("preloadPriority").GetString());
            string[] rawCandidates = result.GetProperty("rawCandidates").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            Assert.NotEmpty(rawCandidates);
            Assert.All(rawCandidates, value => Assert.StartsWith("/src/images/services/", value, StringComparison.Ordinal));
            string[] candidateUrls = result.GetProperty("candidateUrls").EnumerateArray()
                .Select(value => value.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            var selected = result.GetProperty("selected").GetString();
            Assert.Contains(selected, candidateUrls);
            Assert.True(selected!.EndsWith("-640.webp", StringComparison.Ordinal)
                || selected.EndsWith("-768.webp", StringComparison.Ordinal),
                $"390px DPR1 viewport selected an oversized hero: {selected}");
            foreach (var candidate in candidateUrls)
            {
                Assert.Equal(origin, new Uri(candidate).GetLeftPart(UriPartial.Authority));
                using var asset = await client.GetAsync(candidate);
                Assert.True(asset.IsSuccessStatusCode, $"{candidate} returned HTTP {(int)asset.StatusCode}.");
                Assert.Equal("image/webp", asset.Content.Headers.ContentType?.MediaType);
                byte[] bytes = await asset.Content.ReadAsByteArrayAsync();
                Assert.True(bytes.Length >= 12);
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task HomeDirectoryKicker_UsesTheRetained600WeightInTheRenderedPage(string culture)
    {
        var origin = new Uri(fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
        await using IBrowserContext context = await fixture.Browser.NewContextAsync();
        await using IPage page = await context.NewPageAsync();
        IResponse? response = await page.GotoAsync(
            $"{origin}/?culture={culture}&tracking=excluded",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30000 });
        Assert.NotNull(response);
        Assert.True(response.Ok);
        string weight = await page.Locator(".landing-service-directory-kicker")
            .EvaluateAsync<string>("element => getComputedStyle(element).fontWeight");
        Assert.Equal("600", weight);
    }
}
