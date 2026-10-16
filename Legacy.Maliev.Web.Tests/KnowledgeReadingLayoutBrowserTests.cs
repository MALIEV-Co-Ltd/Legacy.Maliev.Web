// <copyright file="KnowledgeReadingLayoutBrowserTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.Playwright;
    using Xunit;

    /// <summary>Verifies readable article orientation and the machining issue illustrations.</summary>
    [Collection(CncNativeBrowserCollection.Name)]
    public class KnowledgeReadingLayoutBrowserTests
    {
        private readonly CncNativeBrowserFixture fixture;

        /// <summary>Initializes a new instance of the <see cref="KnowledgeReadingLayoutBrowserTests"/> class.</summary>
        /// <param name="fixture">The shared local host and disposable browser.</param>
        public KnowledgeReadingLayoutBrowserTests(CncNativeBrowserFixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>The article index follows the reading order and illustrates distortion and vibration.</summary>
        /// <param name="culture">The public language.</param>
        /// <param name="width">The reading viewport width.</param>
        /// <returns>The browser verification task.</returns>
        [Theory]
        [InlineData("en", 1440)]
        [InlineData("th", 1440)]
        [InlineData("en", 375)]
        [InlineData("th", 375)]
        public async Task GeometryGuide_HasSequentialContentsAndIssueIllustrations(string culture, int width)
        {
            await using IBrowserContext context = await this.CreateContextAsync(width);
            IPage page = await context.NewPageAsync();
            string origin = new Uri(this.fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
            IResponse? response = await page.GotoAsync($"{origin}/knowledges/cnc/geometry-and-workholding?culture={culture}");
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.IsVisibleAsync()) await consent.ClickAsync();
            await page.EvaluateAsync("async () => { await document.fonts.ready; }");

            ILocator contents = page.Locator(".docs-toc");
            Assert.True(await contents.Locator("h2, :scope > strong").IsVisibleAsync());
            ILocator links = contents.Locator("a[href^='#']");
            Assert.Equal(5, await links.CountAsync());
            string[] anchors = { "thin-walls", "radii-pockets", "size-fixturing", "warpage", "chatter" };
            string[] labels = culture == "th"
                ? new[] { "ผนังและพื้นบาง", "รัศมีมุมในและโพรง", "ขนาดชิ้นงานและการจับยึด", "การแอ่นและความเค้นตกค้าง", "การสั่นสะท้านและระยะยื่น" }
                : new[] { "Thin walls and floors", "Internal radii and pockets", "Part size and fixturing", "Warpage and residual stress", "Chatter and tool reach" };

            for (int index = 0; index < anchors.Length; index++)
            {
                Assert.Equal("#" + anchors[index], await links.Nth(index).GetAttributeAsync("href"));
                Assert.Contains(labels[index], await links.Nth(index).InnerTextAsync());
                Assert.Equal(1, await page.Locator("#" + anchors[index]).CountAsync());
            }

            Assert.True(await contents.EvaluateAsync<bool>(@"element => {
                const rows = Array.from(element.querySelectorAll('a[href]')).map(link => link.getBoundingClientRect());
                return rows.every((row, index) => row.height >= 43.5 && row.width > 0
                    && Math.abs(row.left - rows[0].left) <= 1
                    && (index === 0 || row.top >= rows[index - 1].bottom - 1));
            }"), "Article links must form a single ordered column with usable targets.");
            await contents.ScrollIntoViewIfNeededAsync();
            await SaveScreenshotAsync(page, $"knowledge-reading-toc-{culture}-{width}.png");

            for (int index = 0; index < anchors.Length; index++)
            {
                await links.Nth(index).FocusAsync();
                Assert.True(await links.Nth(index).EvaluateAsync<bool>("element => document.activeElement === element"));
                await page.Keyboard.PressAsync("Enter");
                await page.WaitForFunctionAsync("anchor => location.hash === '#' + anchor", anchors[index]);
                Assert.True(await page.Locator("#" + anchors[index]).IsVisibleAsync());
            }

            foreach (string anchor in new[] { "warpage", "chatter" })
            {
                ILocator figure = page.Locator("#" + anchor + " .knowledge-issue-photo");
                Assert.Equal(1, await figure.CountAsync());
                ILocator image = figure.Locator("img");
                Assert.False(string.IsNullOrWhiteSpace(await image.GetAttributeAsync("alt")));
                await image.ScrollIntoViewIfNeededAsync();
                Assert.True(await image.EvaluateAsync<bool>("async image => { await image.decode(); return image.naturalWidth > 0 && image.naturalHeight > 0; }"));
                await SaveScreenshotAsync(page, $"knowledge-reading-{anchor}-{culture}-{width}.png");
            }

            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        }

        /// <summary>The responsive inspection hero decodes with the intended aspect ratio and descriptive alternative.</summary>
        /// <param name="culture">The public language.</param>
        /// <returns>The browser verification task.</returns>
        [Theory]
        [InlineData("en")]
        [InlineData("th")]
        public async Task InspectionGuide_HasDecodedMicrometerPhotograph(string culture)
        {
            await using IBrowserContext context = await this.CreateContextAsync(1440);
            IPage page = await context.NewPageAsync();
            string origin = new Uri(this.fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);
            IResponse? response = await page.GotoAsync($"{origin}/knowledges/inspection/measuring-parts?culture={culture}");
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.IsVisibleAsync()) await consent.ClickAsync();
            ILocator image = page.Locator(".knowledge-photo > img");
            Assert.Contains(culture == "th" ? "ไมโครมิเตอร์" : "micrometer", await image.GetAttributeAsync("alt"));
            using var decodedDocument = System.Text.Json.JsonDocument.Parse(await image.EvaluateAsync<string>("async image => { await image.decode(); return JSON.stringify({ width: image.naturalWidth, height: image.naturalHeight, source: image.currentSrc }); }"));
            var decoded = decodedDocument.RootElement;
            Assert.True(decoded.GetProperty("width").GetDouble() > 0 && decoded.GetProperty("height").GetDouble() > 0, decoded.GetRawText());
            Assert.InRange(decoded.GetProperty("width").GetDouble() / decoded.GetProperty("height").GetDouble(), 1.49, 1.51);
            Assert.Contains("/src/images/knowledge/photos/dimensional-inspection", decoded.GetProperty("source").GetString());
            await image.ScrollIntoViewIfNeededAsync();
            await SaveScreenshotAsync(page, $"knowledge-reading-inspection-{culture}-1440.png");
        }

        private static async Task SaveScreenshotAsync(IPage page, string name)
        {
            string? directory = Environment.GetEnvironmentVariable("MALIEV_KNOWLEDGE_SCREENSHOTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(directory, name) });
            }
        }

        private async Task<IBrowserContext> CreateContextAsync(int width)
        {
            IBrowserContext context = await this.fixture.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = 900 },
                ReducedMotion = ReducedMotion.Reduce,
            });
            await context.RouteAsync("**/*", route => Uri.TryCreate(route.Request.Url, UriKind.Absolute, out Uri? uri) && uri.IsLoopback
                ? route.ContinueAsync() : route.AbortAsync());
            return context;
        }
    }
}
