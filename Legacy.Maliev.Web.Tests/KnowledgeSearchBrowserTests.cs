// <copyright file="KnowledgeSearchBrowserTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Playwright;
    using Xunit;

    /// <summary>Verifies local article search, keyboard access and recoverable index loading.</summary>
    [Collection(CncNativeBrowserCollection.Name)]
    public class KnowledgeSearchBrowserTests
    {
        private readonly CncNativeBrowserFixture fixture;

        /// <summary>Initializes a new instance of the <see cref="KnowledgeSearchBrowserTests"/> class.</summary>
        /// <param name="fixture">The shared local browser fixture.</param>
        public KnowledgeSearchBrowserTests(CncNativeBrowserFixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>Search finds body-only numerical knowledge in either language without eager indexing.</summary>
        /// <param name="culture">The public language.</param>
        /// <param name="width">The viewport width.</param>
        /// <returns>The verification task.</returns>
        [Theory]
        [InlineData("en", 1440)]
        [InlineData("th", 1440)]
        [InlineData("en", 375)]
        [InlineData("th", 375)]
        public async Task Search_FindsArticleSectionsAndRestoresBrowsing(string culture, int width)
        {
            await using IBrowserContext context = await this.CreateContextAsync(width);
            IPage page = await context.NewPageAsync();
            int backgroundRequests = 0;
            int indexedPhotographs = 0;
            page.Request += (_, request) =>
            {
                if (request.ResourceType == "fetch" && new Uri(request.Url).AbsolutePath.StartsWith("/knowledges/", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref backgroundRequests);
                }

                string path = new Uri(request.Url).AbsolutePath;
                if (request.ResourceType == "image" && path.Contains("/knowledge/photos/", StringComparison.Ordinal)
                    && !path.EndsWith("-small.webp", StringComparison.Ordinal) && !path.EndsWith("/library.webp", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref indexedPhotographs);
                }
            };
            await page.GotoAsync($"{this.Origin}/knowledges?culture={culture}");
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.IsVisibleAsync()) await consent.ClickAsync();
            await page.WaitForFunctionAsync("() => { const search = document.querySelector('[data-knowledge-search]'); return search && !search.hidden; }");
            Assert.Equal(0, backgroundRequests);
            if (width < 1152)
            {
                await page.Locator("[data-workspace-open]").ClickAsync();
                await page.WaitForFunctionAsync("() => document.activeElement === document.querySelector('[data-workspace-close]')");
                await page.Keyboard.PressAsync("Tab");
                Assert.True(await page.Locator("[data-knowledge-search-input]").EvaluateAsync<bool>("element => element === document.activeElement"));
            }

            if (width >= 1152)
            {
                await page.Locator(".knowledge-directory-list a").First.HoverAsync();
                await SaveScreenshotAsync(page, $"knowledge-directory-hover-{culture}-{width}.png");
            }

            ILocator input = page.Locator("[data-knowledge-search-input]");
            await input.FillAsync("feeds 3819.7");
            ILocator match = page.Locator("[data-knowledge-search-results] a[href*='feeds-and-speeds'][href*='#milling']").First;
            await match.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            string? href = await match.GetAttributeAsync("href");
            Assert.Contains($"culture={culture}", href);
            Assert.True(await page.Locator("[data-knowledge-browse]").EvaluateAsync<bool>("element => element.hidden"));
            Assert.False(string.IsNullOrWhiteSpace(await page.Locator("[data-knowledge-search-status]").InnerTextAsync()));
            Assert.True(backgroundRequests > 0);
            Assert.Equal(0, indexedPhotographs);
            await input.FocusAsync();
            await input.PressAsync("Enter");
            Assert.True(await page.Locator("[data-knowledge-search-results] a").First.EvaluateAsync<bool>("element => element === document.activeElement"));
            await SaveScreenshotAsync(page, $"knowledge-search-{culture}-{width}.png");
            await page.Locator("[data-knowledge-search-clear]").ClickAsync();
            Assert.Equal(string.Empty, await input.InputValueAsync());
            Assert.False(await page.Locator("[data-knowledge-browse]").EvaluateAsync<bool>("element => element.hidden"));
            Assert.Equal(0, await page.Locator("[data-knowledge-search-results] a").CountAsync());
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        }

        /// <summary>A failed background article request offers retry and recovers without reloading the page.</summary>
        /// <param name="culture">The public language.</param>
        /// <returns>The verification task.</returns>
        [Theory]
        [InlineData("en")]
        [InlineData("th")]
        public async Task Search_RetriesFailedArticleIndex(string culture)
        {
            await using IBrowserContext context = await this.CreateContextAsync(1440);
            IPage page = await context.NewPageAsync();
            await page.GotoAsync($"{this.Origin}/knowledges?culture={culture}");
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.IsVisibleAsync()) await consent.ClickAsync();
            const string failedRoute = "**/knowledges/cnc/feeds-and-speeds*";
            await page.RouteAsync(failedRoute, route => route.AbortAsync());
            await page.Locator("[data-knowledge-search-input]").FillAsync("3819.7");
            ILocator retry = page.Locator("[data-knowledge-search-retry]");
            await retry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            string error = await page.Locator("[data-knowledge-search-status]").InnerTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(error));
            if (culture == "th")
            {
                Assert.Matches("[ก-๙]", error);
            }

            await page.UnrouteAsync(failedRoute);
            await retry.ClickAsync();
            await page.Locator("[data-knowledge-search-results] a[href*='feeds-and-speeds'][href*='#milling']").First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            Assert.False(await retry.IsVisibleAsync());
        }

        /// <summary>Heading-ID sections are searchable and receive focus when selected in the mobile drawer.</summary>
        /// <param name="culture">The public language.</param>
        /// <returns>The verification task.</returns>
        [Theory]
        [InlineData("en")]
        [InlineData("th")]
        public async Task Search_NavigatesToHeadingAnchorsInCurrentGuide(string culture)
        {
            await using IBrowserContext context = await this.CreateContextAsync(375);
            IPage page = await context.NewPageAsync();
            await page.GotoAsync($"{this.Origin}/knowledges/specifications?culture={culture}");
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.IsVisibleAsync()) await consent.ClickAsync();
            await page.Locator("[data-workspace-open]").ClickAsync();
            await page.Locator("[data-knowledge-search-input]").FillAsync(culture == "th" ? "ซิลิโคน" : "silicone");
            ILocator match = page.Locator("[data-knowledge-search-results] a[href*='/specifications?'][href$='#other-processes']");
            await match.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await match.ClickAsync();
            await page.WaitForURLAsync("**#other-processes");
            Assert.True(await page.Locator("#other-processes").EvaluateAsync<bool>("element => element === document.activeElement"));
            Assert.True(await page.Locator("#knowledge-navigation").EvaluateAsync<bool>("element => element.inert"));
            Assert.False(await page.Locator("#knowledge-content").EvaluateAsync<bool>("element => element.inert"));
        }

        /// <summary>Client-formatted count placeholders render literally without a server formatting exception.</summary>
        /// <param name="culture">The requested public language.</param>
        /// <returns>The page-render regression task.</returns>
        [Theory]
        [InlineData("en")]
        [InlineData("th")]
        public async Task Specifications_RendersLiteralSearchCountTemplates(string culture)
        {
            await using IBrowserContext context = await this.CreateContextAsync(1440);
            IPage page = await context.NewPageAsync();
            IResponse? response = await page.GotoAsync($"{this.Origin}/knowledges/specifications?culture={culture}");
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            Assert.Equal(1, await page.Locator(".knowledge-document h1").CountAsync());
            ILocator search = page.Locator("[data-knowledge-search]");
            Assert.Equal(culture == "th" ? "พบ {0} รายการ" : "Matches: {0}",
                await search.GetAttributeAsync("data-search-count"));
            Assert.Equal(culture == "th" ? "แสดง {0} จาก {1} รายการ ลองระบุคำค้นให้เฉพาะเจาะจงขึ้น"
                : "Showing {0} of {1} matches. Narrow your search to see more.",
                await search.GetAttributeAsync("data-search-limited"));
        }


        /// <summary>Suspension rejects late index responses and BFCache resumes a bounded inert index.</summary>
        [Theory]
        [InlineData("en")]
        [InlineData("th")]
        public async Task Search_PageHideRejectsLateResponsesAndPageShowResumesSafely(string culture)
        {
            await using IBrowserContext context = await this.CreateContextAsync(1440);
            IPage page = await context.NewPageAsync();
            await page.GotoAsync($"{this.Origin}/knowledges?culture={culture}");
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.IsVisibleAsync()) await consent.ClickAsync();
            Assert.Equal(0, await page.Locator("#cookieConsent").CountAsync());
            await page.WaitForFunctionAsync("() => document.querySelector('[data-knowledge-search]')?.dataset.searchReady === 'true'");
            int photographs = 0;
            page.Request += (_, request) =>
            {
                if (request.ResourceType == "image" && request.Url.Contains("/index-must-not-load.png", StringComparison.Ordinal))
                    Interlocked.Increment(ref photographs);
            };
            await page.EvaluateAsync("""
                () => {
                    const probe = window.__knowledgeLifecycleProbe = { pending: [], aborted: 0, peak: 0, active: 0, restored: false };
                    window.fetch = (url, options) => {
                        if (new URL(url).origin !== location.origin || options.credentials !== 'same-origin'
                            || options.redirect !== 'error') throw new Error('Index escaped its request contract');
                        probe.active++;
                        probe.peak = Math.max(probe.peak, probe.active);
                        options.signal.addEventListener('abort', () => probe.aborted++, { once: true });
                        const html = title => '<article class="knowledge-document"><h1>' + title + '</h1>'
                            + '<section id="number"><h2>Numeric example</h2><p>3819.7</p></section>'
                            + '<script>window.__indexedScriptRan = true;<\/script><img src="/index-must-not-load.png"></article>';
                        if (probe.restored) {
                            return Promise.resolve({ ok: true, text: async () => {
                                probe.active--;
                                return html('Restored 3819.7');
                            } });
                        }
                        // Deliberately resolve even after AbortSignal, to test the stale-generation fence.
                        return new Promise(resolve => probe.pending.push(() => {
                            probe.active--;
                            resolve({ ok: true, text: async () => html('STALE 3819.7') });
                        }));
                    };
                }
                """);
            await page.Locator("[data-knowledge-search-input]").FillAsync("3819.7");
            await page.WaitForFunctionAsync("() => window.__knowledgeLifecycleProbe.pending.length === 3");
            await page.EvaluateAsync("""
                () => {
                    dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                    const probe = window.__knowledgeLifecycleProbe;
                    probe.pending.forEach(resolve => resolve());
                }
                """);
            Assert.Equal(3, await page.EvaluateAsync<int>("() => window.__knowledgeLifecycleProbe.aborted"));
            Assert.Equal(0, await page.Locator("[data-knowledge-search-results] a").CountAsync());
            Assert.False(await page.Locator("[data-knowledge-search-retry]").IsVisibleAsync());
            await page.EvaluateAsync("""
                () => {
                    window.__knowledgeLifecycleProbe.restored = true;
                    dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
                }
                """);
            await page.Locator("[data-knowledge-search-results] a").First.WaitForAsync();
            Assert.Equal(8, await page.Locator("[data-knowledge-search-results] a").CountAsync());
            Assert.DoesNotContain("STALE", await page.Locator("[data-knowledge-search-results]").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains(culture == "th" ? "แสดง 8 จาก 36 รายการ" : "Showing 8 of 36 matches",
                await page.Locator("[data-knowledge-search-status]").InnerTextAsync(), StringComparison.Ordinal);
            Assert.InRange(await page.EvaluateAsync<int>("() => window.__knowledgeLifecycleProbe.peak"), 1, 3);
            Assert.False(await page.EvaluateAsync<bool>("() => window.__indexedScriptRan === true"));
            Assert.Equal(0, photographs);
        }

        private string Origin => new Uri(this.fixture.CncQuotationUrl).GetLeftPart(UriPartial.Authority);

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
