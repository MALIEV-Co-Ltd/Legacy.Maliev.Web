using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Served layout and real Chromium ordering; seeded application TempData is not producer persistence proof.</summary>
[Collection(PublicContactBrowserCollection.Name)]
public sealed class AnalyticsReadyOrderBrowserTests(PublicContactBrowserFixture browser)
{
    [Theory]
    [InlineData(false, "en", "grant")]
    [InlineData(false, "th", "grant")]
    [InlineData(true, "en", "grant")]
    [InlineData(true, "th", "grant")]
    [InlineData(false, "en", "reject")]
    [InlineData(false, "th", "reject")]
    [InlineData(true, "en", "reject")]
    [InlineData(true, "th", "reject")]
    [InlineData(false, "en", "pregranted")]
    [InlineData(false, "th", "pregranted")]
    [InlineData(true, "en", "pregranted")]
    [InlineData(true, "th", "pregranted")]
    public async Task ServedBothRenderers_OrdersApplicationQueueReadyAndLoaderOnce(
        bool retained, string culture, string decision)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = new Uri($"http://127.0.0.1:{port}");
        await using var host = new ReadyHost(retained);
        host.UseKestrel(port);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        await using var context = await browser.Browser.NewContextAsync();
        if (decision == "pregranted")
        {
            // Read the actual host cookie contract, rather than assume the framework's cookie name/value.
            var options = host.Services.GetRequiredService<IOptions<CookiePolicyOptions>>().Value;
            await context.AddCookiesAsync([new Microsoft.Playwright.Cookie
            {
                Name = options.ConsentCookie.Name!, Value = options.ConsentCookieValue, Url = origin.ToString(),
            }]);
        }

        var attemptedExternalHosts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await context.RouteAsync("**/*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            if (uri.Host != origin.Host)
            {
                // Keep every attempted host, including unknown vendors; retain no path/query or payload.
                attemptedExternalHosts.AddOrUpdate(uri.Host, 1, static (_, count) => count + 1);
                await route.AbortAsync();
                return;
            }

            await route.ContinueAsync();
        });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(new Uri(origin, $"/?culture={culture}").ToString());
        Assert.NotNull(response);
        Assert.Equal((int)HttpStatusCode.OK, response.Status);
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Equal(retained ? 0 : 1, await page.Locator("[data-migration-route-owner='blazor-static-ssr']").CountAsync());
        Assert.Equal(1, await page.Locator("script[data-migration-component='public-google-tag-manager-head']").CountAsync());
        var before = await Layer(page);
        Assert.Equal("consent", before[0][0].GetString());
        Assert.Equal("default", before[0][1].GetString());
        Assert.Equal(decision == "pregranted" ? "granted" : "denied", before[0][2].GetProperty("analytics_storage").GetString());
        Assert.DoesNotContain(before.EnumerateArray(), IsIdentity);

        if (decision != "pregranted")
        {
            Assert.Empty(Events(before));
            Assert.Empty(attemptedExternalHosts);
            Assert.Equal(0, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
            await page.Locator($"#cookieConsent [data-consent-action='{(decision == "grant" ? "accept" : "reject")}']").ClickAsync();
        }

        if (decision == "reject")
        {
            Assert.Empty(Events(await Layer(page)));
            Assert.Empty(attemptedExternalHosts);
            Assert.Equal(0, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
            return;
        }

        var granted = await Layer(page);
        Assert.Equal(new[] { "maliev_lead_submitted", "generate_lead", "maliev_quote_decision", "maliev_analytics_ready", "gtm.js" }, Events(granted));
        var ready = Assert.Single(granted.EnumerateArray(), IsReady);
        Assert.Equal("event", Assert.Single(ready.EnumerateObject()).Name);
        Assert.Equal("maliev_analytics_ready", ready.GetProperty("event").GetString());
        if (decision == "grant")
        {
            var update = Array.FindIndex(granted.EnumerateArray().ToArray(), item => item.ValueKind == JsonValueKind.Array && item[0].GetString() == "consent" && item[1].GetString() == "update");
            var firstEvent = Array.FindIndex(granted.EnumerateArray().ToArray(), item => item.ValueKind == JsonValueKind.Object);
            Assert.True(update > 0 && update < firstEvent, "Consent update must precede application queue flush.");
        }

        Assert.Equal(1, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
        await page.EvaluateAsync("""
            () => {
                window.malievAnalytics.setConsent('granted');
                window.malievLoadGoogleTagManager();
                window.malievAnalytics.setConsent('denied');
                window.malievAnalytics.emit({ event: 'withheld_after_revoke' });
                window.malievAnalytics.setConsent('granted');
                window.malievLoadGoogleTagManager();
            }
            """);
        var cycle = await Layer(page);
        Assert.Equal(Events(granted), Events(cycle));
        Assert.Single(cycle.EnumerateArray(), IsReady);
        // Same-document grant/regrant cannot discover a withheld server identity.
        Assert.DoesNotContain(cycle.EnumerateArray(), IsIdentity);
        Assert.Contains(cycle.EnumerateArray(), item => item.ValueKind == JsonValueKind.Array
            && item[0].GetString() == "set" && item[1].GetProperty("user_id").ValueKind == JsonValueKind.Null);
        Assert.Equal(1, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
    }

    private static bool IsReady(JsonElement item) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("event", out var name) && name.GetString() == "maliev_analytics_ready";

    private static bool IsIdentity(JsonElement item) => item.ValueKind == JsonValueKind.Array
        && item[0].GetString() == "set" && item[1].TryGetProperty("user_id", out var value)
        && value.ValueKind != JsonValueKind.Null;

    private static string[] Events(JsonElement layer) => layer.EnumerateArray()
        .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("event", out _))
        .Select(item => item.GetProperty("event").GetString()!).ToArray();

    private static async Task<JsonElement> Layer(IPage page)
    {
        var json = await page.EvaluateAsync<string>("() => JSON.stringify(window.dataLayer.map(item => Object.prototype.toString.call(item) === '[object Arguments]' ? Array.from(item) : item))");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class ReadyHost(bool retained)
        : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("BlazorRouting:Home", retained ? "false" : "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITempDataProvider>();
                services.AddSingleton<ITempDataProvider, SeededQueue>();
            });
        }
    }

    private sealed class SeededQueue : ITempDataProvider
    {
        private IDictionary<string, object> data = new Dictionary<string, object>
        {
            ["Maliev.LeadAnalyticsEvent"] = JsonSerializer.Serialize(new LeadAnalyticsEvent(
                "instant_3d_quote", "3d_printing", "request-714", true, null, null,
                "11111111-2222-3333-4444-555555555555")),
            ["Maliev.CustomerJourneyAnalyticsEvent"] = JsonSerializer.Serialize(new CustomerJourneyAnalyticsEvent("quotation-714", "accepted")),
        };

        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(data);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => data = new Dictionary<string, object>(values);
    }
}
