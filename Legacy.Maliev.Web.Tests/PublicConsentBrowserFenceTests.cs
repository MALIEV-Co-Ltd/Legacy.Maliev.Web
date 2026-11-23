using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicConsentBrowserFenceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public async Task RoutedConsent_RetriesUnreadableGrantAndRevokesAcrossTabs()
    {
        const string home = "https://localhost/?culture=en";
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(home),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var html = await client.GetStringAsync(home);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", html, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", html, StringComparison.Ordinal);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = ["--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE localhost"]
        });
        await using var context = await browser.NewContextAsync(new() { ServiceWorkers = ServiceWorkerPolicy.Block });
        context.SetDefaultTimeout(10000);
        var tagAttempts = new ConcurrentQueue<string>();
        await context.RouteWebSocketAsync("**/*", route => route.CloseAsync());
        await context.RouteAsync("**/*", async route =>
        {
            if (route.Request.IsNavigationRequest && route.Request.Method == "GET" && route.Request.Url == home)
            {
                await route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html });
                return;
            }

            if (new Uri(route.Request.Url).Host == "www.googletagmanager.com")
                tagAttempts.Enqueue(route.Request.Url);
            await route.AbortAsync();
        });

        await using var first = await context.NewPageAsync();
        await using var second = await context.NewPageAsync();
        await first.GotoAsync(home);
        await second.GotoAsync(home);
        var dialog = first.Locator("#cookieConsent");
        var accept = first.Locator("[data-consent-action='accept']");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        var pair = (await accept.GetAttributeAsync("data-cookie-string"))!.Split(';', 2)[0];
        Assert.Equal(".AspNet.Consent=yes", pair);

        await first.EvaluateAsync("() => window.malievLoadGoogleTagManager()");
        Assert.False(await IsGranted(first));
        Assert.Empty(tagAttempts);
        Assert.Equal(0, await first.Locator("script[data-maliev-gtm-loader]").CountAsync());

        // Refuse only this page's grant write while keeping native cookie reads.
        await first.EvaluateAsync("""
            () => {
                const native = Object.getOwnPropertyDescriptor(Document.prototype, 'cookie');
                Object.defineProperty(document, 'cookie', {
                    configurable: true,
                    get() { return native.get.call(this); },
                    set(value) {
                        if (!value.startsWith('.AspNet.Consent=')) native.set.call(this, value);
                    }
                });
            }
            """);
        await accept.ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(accept).ToBeEnabledAsync();
        Assert.DoesNotContain(pair, await first.EvaluateAsync<string>("() => document.cookie"));
        Assert.False(await IsGranted(first));
        Assert.Empty(tagAttempts);
        Assert.Equal(0, await first.Locator("script[data-maliev-gtm-loader]").CountAsync());

        await first.EvaluateAsync("() => { delete document.cookie; }");
        await accept.ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        Assert.Contains(pair, await first.EvaluateAsync<string>("() => document.cookie"));
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == ".AspNet.Consent"
            && cookie.Value == "yes" && cookie.Secure && !cookie.HttpOnly && cookie.Path == "/");
        Assert.True(await IsGranted(first));
        await Assertions.Expect(first.Locator("script[data-maliev-gtm-loader]")).ToHaveCountAsync(1);
        await first.WaitForFunctionAsync("() => window.dataLayer.some(value => value?.event === 'gtm.js')");

        await second.Locator("[data-consent-action='reject']").ClickAsync();
        await Assertions.Expect(second.Locator("#cookieConsent")).ToBeHiddenAsync();
        Assert.Contains("maliev_tracking_consent=denied", await first.EvaluateAsync<string>("() => document.cookie"));
        await first.EvaluateAsync("() => window.dispatchEvent(new Event('focus'))");
        // Check the event's side effects before calling the bridge's guarded getter.
        Assert.True(await first.EvaluateAsync<bool>("""
            () => {
                const updates = window.dataLayer.filter(value => value?.[0] === 'consent' && value[1] === 'update');
                return updates.at(-1)?.[2]?.analytics_storage === 'denied'
                    && window.dataLayer.some(value => value?.[0] === 'set' && value[1]?.user_id === null);
            }
            """));
        Assert.False(await IsGranted(first));
        await first.EvaluateAsync("""
            () => {
                window.malievAnalytics.emit({ event: 'synthetic_after_denial' });
                window.malievLoadGoogleTagManager();
            }
            """);
        Assert.False(await first.EvaluateAsync<bool>(
            "() => window.dataLayer.some(value => value?.event === 'synthetic_after_denial')"));
        Assert.Single(tagAttempts);
        Assert.False(await first.EvaluateAsync<bool>("""
            () => window.dataLayer.some(value => value?.[0] === 'set' && value[1]?.user_id != null)
            """));
    }

    private static Task<bool> IsGranted(IPage page) =>
        page.EvaluateAsync<bool>("() => window.malievAnalytics.isConsentGranted()");
}
