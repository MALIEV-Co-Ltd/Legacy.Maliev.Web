using Microsoft.Playwright;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class KnowledgeSidebarBrowserTests(CncNativeBrowserFixture fixture)
{
    [Theory]
    [InlineData("en", 375)]
    [InlineData("th", 375)]
    [InlineData("en", 1280)]
    [InlineData("th", 1280)]
    public async Task SidebarLinksRemainKeyboardOperableAcrossLocalizedDocuments(string culture, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 844 }
        });
        await using var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var origin = new Uri(fixture.CncQuotationUrl);

        foreach (var route in KnowledgeSidebarRoutes.All)
        {
            var url = new Uri(origin, $"{route.Path}?culture={culture}").ToString();
            await page.GotoAsync(url);
            var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
            if (await consent.IsVisibleAsync()) await consent.ClickAsync();
            var sidebar = page.Locator("#knowledge-navigation");
            Assert.Equal(1, await sidebar.CountAsync());
            var overview = sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions
            {
                Name = culture == "th" ? "ศูนย์ความรู้" : "Knowledge center",
                Exact = true
            });
            var specifications = sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions
            {
                Name = culture == "th" ? "เตรียมงานสำหรับแต่ละบริการ" : "Service preparation guides",
                Exact = true
            });
            Assert.Equal(1, await overview.CountAsync());
            Assert.Equal(1, await specifications.CountAsync());

            if (width < 1152)
            {
                var opener = page.Locator("[data-workspace-open]");
                await opener.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
                await page.WaitForFunctionAsync("() => document.activeElement === document.querySelector('[data-workspace-close]')");
                Assert.Equal("true", await opener.GetAttributeAsync("aria-expanded"));
                Assert.True(await sidebar.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
                Assert.True(await sidebar.Locator("[data-workspace-close]").EvaluateAsync<bool>(
                    "element => document.activeElement === element"));
                await page.Keyboard.PressAsync("Escape");
                Assert.Equal("false", await opener.GetAttributeAsync("aria-expanded"));
                Assert.True(await opener.EvaluateAsync<bool>("element => document.activeElement === element"));
                await page.Keyboard.PressAsync("Enter");
                await page.WaitForFunctionAsync("""
                    () => {
                        const bounds = document.querySelector('#knowledge-navigation')?.getBoundingClientRect();
                        return !!bounds && bounds.left >= 0 && bounds.right <= window.innerWidth;
                    }
                    """);
            }

            await OpenStartGroupAsync(sidebar);
            Assert.True(await overview.IsVisibleAsync());
            Assert.True(await specifications.IsVisibleAsync());
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"));
            if (route.Path == "/knowledges")
            {
                var screenshotDirectory = Path.GetFullPath(Path.Combine(
                    BrowserHostIdentityVerifier.SourceProjectDirectory(), "..", "Legacy.Maliev.Web.Tests",
                    "TestResults", "knowledge-navigation-browser"));
                Directory.CreateDirectory(screenshotDirectory);
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(screenshotDirectory, $"{culture}-{width}.png"),
                    FullPage = true
                });
            }

            await overview.FocusAsync();
            await ActivateLinkAsync(page, overview, new Uri(origin, $"/knowledges?culture={culture}").ToString(),
                route.Path, culture, width, "overview");

            await page.GotoAsync(url);
            if (width < 1152)
            {
                await page.Locator("[data-workspace-open]").FocusAsync();
                await page.Keyboard.PressAsync("Enter");
                // open() hands focus to Close on its next animation frame.
                // Await that handoff before focusing the destination link, or the
                // pending frame can overwrite the explicit keyboard target.
                await page.WaitForFunctionAsync("""
                    () => {
                        const drawer = document.querySelector('#knowledge-navigation');
                        return !!drawer && drawer.classList.contains('is-open') && !drawer.inert
                            && document.querySelector('[data-workspace-open]')?.getAttribute('aria-expanded') === 'true'
                            && document.activeElement === drawer.querySelector('[data-workspace-close]');
                    }
                    """);
            }
            var specificationsLink = page.Locator("#knowledge-navigation").GetByRole(AriaRole.Link,
                new LocatorGetByRoleOptions
                {
                    Name = culture == "th" ? "เตรียมงานสำหรับแต่ละบริการ" : "Service preparation guides",
                    Exact = true
                });
            await OpenStartGroupAsync(page.Locator("#knowledge-navigation"));
            await specificationsLink.FocusAsync();
            await ActivateLinkAsync(page, specificationsLink,
                new Uri(origin, $"/knowledges/specifications?culture={culture}").ToString(),
                route.Path, culture, width, "specifications");
        }

        Assert.Empty(errors);
    }

    private static async Task OpenStartGroupAsync(ILocator sidebar)
    {
        var group = sidebar.Locator("details.knowledge-nav-group").First;
        if (!await group.EvaluateAsync<bool>("element => element.open"))
        {
            await group.Locator("summary").FocusAsync();
            await group.Locator("summary").PressAsync("Enter");
        }
        Assert.True(await group.EvaluateAsync<bool>("element => element.open"));
    }

    private static async Task ActivateLinkAsync(IPage page, ILocator link, string destination,
        string sourceRoute, string culture, int width, string phase)
    {
        var events = new List<string>();
        void Record(string value)
        {
            lock (events)
            {
                if (events.Count < 64) events.Add(value);
            }
        }
        void Navigated(object? sender, IFrame frame)
        {
            if (frame == page.MainFrame && Uri.TryCreate(frame.Url, UriKind.Absolute, out var uri))
                Record($"navigated:{uri.AbsolutePath}");
        }
        void Loaded(object? sender, IPage loadedPage) => Record("load");
        page.FrameNavigated += Navigated;
        page.Load += Loaded;
        Task? navigation = null;
        try
        {
            Assert.True(await link.EvaluateAsync<bool>("element => document.activeElement === element"));
            // Register the same exact-URL, Load, 30-second observer before keyboard activation.
            navigation = page.WaitForURLAsync(destination, new PageWaitForURLOptions
            {
                WaitUntil = WaitUntilState.Load,
                Timeout = 30_000
            });
            Record("waiter-armed");
            await page.Keyboard.PressAsync("Enter");
            Record("enter-returned");
            await navigation;
        }
        catch (Exception error)
        {
            try
            {
                string? focus = null;
                try
                {
                    focus = await page.Locator("body").EvaluateAsync<string>("""
                        () => JSON.stringify({
                            readyState: document.readyState,
                            tag: document.activeElement?.tagName?.slice(0, 32),
                            id: document.activeElement?.id?.slice(0, 128),
                            href: document.activeElement?.getAttribute('href')?.slice(0, 256),
                            drawerOpen: document.querySelector('#knowledge-navigation')?.classList.contains('is-open')
                        })
                        """, options: new LocatorEvaluateOptions { Timeout = 2_000 });
                }
                catch (Exception diagnosticError)
                {
                    focus = $"diagnostic-unavailable:{diagnosticError.GetType().Name}";
                }
                var directory = Path.GetFullPath(Path.Combine(
                    BrowserHostIdentityVerifier.SourceProjectDirectory(), "..", "Legacy.Maliev.Web.Tests",
                    "TestResults", "knowledge-navigation-browser"));
                Directory.CreateDirectory(directory);
                string[] eventSnapshot;
                lock (events) eventSnapshot = events.ToArray();
                var receipt = JsonSerializer.Serialize(new
                {
                    sourceRoute,
                    culture,
                    width,
                    phase,
                    destination = new Uri(destination).AbsolutePath,
                    currentRoute = new Uri(page.Url).AbsolutePath,
                    error = error.GetType().Name,
                    events = eventSnapshot,
                    focus
                }, new JsonSerializerOptions { WriteIndented = true });
                var name = $"{culture}-{width}-{sourceRoute.Replace('/', '_')}-{phase}-{Guid.NewGuid():N}.json";
                await File.WriteAllTextAsync(Path.Combine(directory, name), receipt);
                Console.WriteLine(receipt);
            }
            catch (Exception)
            {
                // Failure evidence is best effort and must preserve the original assertion/navigation failure.
            }
            throw;
        }
        finally
        {
            page.FrameNavigated -= Navigated;
            page.Load -= Loaded;
            // Observe a pending waiter if the keyboard action itself failed first.
            if (navigation is not null) _ = navigation.ContinueWith(task => _ = task.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
