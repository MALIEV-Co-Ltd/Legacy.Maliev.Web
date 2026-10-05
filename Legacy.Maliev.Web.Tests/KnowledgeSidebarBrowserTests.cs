using Microsoft.Playwright;

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
                Name = culture == "th" ? "ศูนย์ความรู้" : "Knowledge center", Exact = true
            });
            var specifications = sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions
            {
                Name = culture == "th" ? "ข้อแนะนำทุกบริการ" : "All service specifications", Exact = true
            });
            Assert.Equal(1, await overview.CountAsync());
            Assert.Equal(1, await specifications.CountAsync());

            if (width < 992)
            {
                var opener = page.Locator("[data-workspace-open]");
                await opener.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
                Assert.Equal("true", await opener.GetAttributeAsync("aria-expanded"));
                Assert.True(await sidebar.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
                Assert.True(await sidebar.Locator("[data-workspace-close]").EvaluateAsync<bool>(
                    "element => document.activeElement === element"));
                await page.Keyboard.PressAsync("Escape");
                Assert.Equal("false", await opener.GetAttributeAsync("aria-expanded"));
                Assert.True(await opener.EvaluateAsync<bool>("element => document.activeElement === element"));
                await page.Keyboard.PressAsync("Enter");
            }

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
                    Path = Path.Combine(screenshotDirectory, $"{culture}-{width}.png"), FullPage = true
                });
            }

            await overview.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForURLAsync(new Uri(origin, "/knowledges").ToString());

            await page.GotoAsync(url);
            if (width < 992)
            {
                await page.Locator("[data-workspace-open]").FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            }
            await page.Locator("#knowledge-navigation").GetByRole(AriaRole.Link,
                new LocatorGetByRoleOptions
                {
                    Name = culture == "th" ? "ข้อแนะนำทุกบริการ" : "All service specifications", Exact = true
                }).FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForURLAsync(new Uri(origin, "/knowledges/specifications").ToString());
        }

        Assert.Empty(errors);
    }
}
