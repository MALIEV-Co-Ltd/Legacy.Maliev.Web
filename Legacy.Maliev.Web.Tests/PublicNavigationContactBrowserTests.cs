using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

[Collection(PublicContactBrowserCollection.Name)]
public sealed class PublicNavigationContactBrowserTests(PublicContactBrowserFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task LoginSubmit_RemainsDistinctFromHiddenLanguageOptions(string culture)
    {
        await using var context = await fixture.Browser.NewContextAsync();
        await context.RouteAsync("**/*", route => new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority)
            == fixture.Origin.GetLeftPart(UriPartial.Authority) ? route.ContinueAsync() : route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(fixture.Origin, $"/Account/Login?culture={culture}").ToString());
        var login = page.Locator("#customer-login button[type=submit]");
        await Assertions.Expect(login).ToHaveCountAsync(1);
        await Assertions.Expect(login).ToBeVisibleAsync();
        Assert.True(await page.Locator(".maliev-language-option[type=submit]").CountAsync() > 0);
        await Assertions.Expect(page.Locator(".maliev-language-option[type=submit]").First).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("en", 1600, "Contact")]
    [InlineData("th", 1600, "ติดต่อเรา")]
    [InlineData("en", 390, "Contact")]
    [InlineData("th", 390, "ติดต่อเรา")]
    [InlineData("en", 320, "Contact")]
    [InlineData("th", 320, "ติดต่อเรา")]
    public async Task ContactMenu_KeyboardAndOutsideClickCloseWithoutExternalNavigation(string culture, int width, string label)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        await context.RouteAsync("**/*", async route =>
        {
            if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != fixture.Origin.GetLeftPart(UriPartial.Authority))
                await route.AbortAsync();
            else
                await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(fixture.Origin, $"/legal?culture={culture}").ToString());
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        var menu = page.Locator(".landing-contact-details");
        var toggle = menu.Locator("summary");
        await Assertions.Expect(toggle).ToContainTextAsync(label);
        Assert.Equal(6, await menu.Locator(".landing-contact-menu a").CountAsync());
        await Assertions.Expect(menu.Locator("[data-maliev-contact-destination='whatsapp_business']")).ToHaveAttributeAsync("href", "https://wa.me/66898950690");
        await Assertions.Expect(menu.Locator("a[href^='https://line.me/']")).ToHaveAttributeAsync("href", "https://line.me/ti/p/@maliev");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(menu).ToHaveAttributeAsync("open", "");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        await CaptureAsync(page, $"contact-{culture}-{width}.png");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await toggle.ClickAsync();
        await Assertions.Expect(menu).ToHaveAttributeAsync("open", "");
        await page.Locator(".landing-header").ClickAsync(new() { Position = new() { X = 2, Y = 2 } });
        await Assertions.Expect(menu).Not.ToHaveAttributeAsync("open", "");
    }

    [Theory]
    [InlineData("en", 1600, "th")]
    [InlineData("th", 1600, "en")]
    [InlineData("en", 390, "th")]
    [InlineData("th", 390, "en")]
    [InlineData("en", 320, "th")]
    [InlineData("th", 320, "en")]
    public async Task LanguageMenu_KeyboardSelectsTheOtherCulture(string culture, int width, string target)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        await context.RouteAsync("**/*", async route =>
        {
            if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != fixture.Origin.GetLeftPart(UriPartial.Authority))
                await route.AbortAsync();
            else
                await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(fixture.Origin, $"/legal?culture={culture}").ToString());
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        if (width < 1440)
            await page.Locator(".landing-nav-toggle").ClickAsync();
        var toggle = page.Locator("#maliev-language-menu");
        await Assertions.Expect(toggle).ToHaveAccessibleNameAsync(culture == "th" ? "เลือกภาษา" : "Select language");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        var option = page.Locator($".maliev-language-option[value='{target}']");
        await option.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", target);
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == ".AspNetCore.Culture" && cookie.Value.Contains(target, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("en", "th", "/legal", 1600)]
    [InlineData("th", "en", "/legal?culture=en", 1600)]
    [InlineData("en", "th", "/legal", 390)]
    [InlineData("th", "en", "/legal?culture=en", 390)]
    [InlineData("en", "th", "/legal", 320)]
    [InlineData("th", "en", "/legal?culture=en", 320)]
    public async Task LanguageSelector_NoJavaScriptSubmitsProtectedLocalReturn(string culture, string target, string expectedPath, int width)
    {
        await using var context = await fixture.Browser.NewContextAsync(new() { JavaScriptEnabled = false, ViewportSize = new() { Width = width, Height = 900 } });
        await context.RouteAsync("**/*", async route =>
        {
            if (new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) != fixture.Origin.GetLeftPart(UriPartial.Authority))
                await route.AbortAsync();
            else
                await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(fixture.Origin, $"/legal?culture={culture}").ToString());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        var selector = page.Locator("#maliev-navbar-language");
        var submit = page.Locator(".maliev-language-form noscript button");
        Assert.Equal(width < 1440 ? "block" : "flex", await page.Locator(".landing-nav-content").EvaluateAsync<string>("element => getComputedStyle(element).display"));
        if (width < 1440)
        {
            await Assertions.Expect(page.Locator(".landing-nav-toggle")).ToBeHiddenAsync();
            Assert.True(await page.Locator("main").EvaluateAsync<bool>("element => document.querySelector('.landing-header').getBoundingClientRect().bottom <= element.getBoundingClientRect().top + 1"));
        }
        await Assertions.Expect(selector).ToHaveAccessibleNameAsync(culture == "th" ? "เลือกภาษา" : "Select language");
        await Assertions.Expect(submit).ToHaveAccessibleNameAsync(culture == "th" ? "ไป" : "Go");
        Assert.True(await selector.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
        Assert.True(await submit.EvaluateAsync<bool>("element => { const box = element.getBoundingClientRect(); return box.width >= 44 && box.height >= 44; }"));
        await selector.FocusAsync();
        await Assertions.Expect(selector).ToBeFocusedAsync();
        await selector.SelectOptionAsync(target);
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(submit).ToBeFocusedAsync();
        await CaptureAsync(page, $"language-nojs-{culture}-{width}.png");
        await submit.ClickAsync();
        Assert.EndsWith(expectedPath, page.Url, StringComparison.Ordinal);
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == ".AspNetCore.Culture" && cookie.Value.Contains(target, StringComparison.Ordinal));
    }

    private static async Task CaptureAsync(IPage page, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("MALIEV_WEB793_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, fileName) });
        }
    }
}
