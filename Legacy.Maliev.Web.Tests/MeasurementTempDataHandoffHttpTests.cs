using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks real encrypted TempData handoff and rendered ready ordering, not producer persistence or hostile-cookie replay.</summary>
/// <param name="factory">The existing Web test factory.</param>
public sealed class MeasurementTempDataHandoffHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    /// <summary>Both renderer responses consume encrypted lead and journey events before ready, then follow cookie deletion without replay.</summary>
    /// <param name="retained">Whether Home uses retained Razor rather than active static SSR.</param>
    /// <param name="culture">The actual document culture.</param>
    /// <returns>A task completing cookie, response and event-order assertions.</returns>
    [Theory]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    public async Task BothRendererResponses_ConsumeEncryptedPairAndJourneyBeforeReadyWithoutReplay(bool retained, string culture)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Home", retained ? "false" : "true"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var scope = host.Services.CreateScope();
        var provider = Assert.IsType<CookieTempDataProvider>(scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        provider.SaveTempData(context, new Dictionary<string, object>
        {
            ["Maliev.LeadAnalyticsEvent"] = JsonSerializer.Serialize(new LeadAnalyticsEvent(
                "instant_3d_quote", "3d_printing", "request-714", true, null, null,
                "11111111-2222-3333-4444-555555555555")),
            ["Maliev.CustomerJourneyAnalyticsEvent"] = JsonSerializer.Serialize(new CustomerJourneyAnalyticsEvent("quotation-714", "accepted")),
        });
        const string prefix = "__Host-Maliev.Legacy.TempData=";
        var cookie = Assert.Single(context.Response.Headers.SetCookie,
            value => value?.StartsWith(prefix, StringComparison.Ordinal) == true)!;
        Assert.True(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.True(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.True(cookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase));
        Assert.False(cookie.Contains("request-714", StringComparison.Ordinal));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/?culture={culture}");
        request.Headers.Add("Cookie", cookie.Split(';', 2)[0]);
        using var first = await client.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var html = await first.Content.ReadAsStringAsync(timeout.Token);
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Equal(!retained, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        const string readyMarker = "window.malievAnalytics.emit({ event: 'maliev_analytics_ready' });";
        var consentIndex = html.IndexOf("window.gtag('consent', 'default'", StringComparison.Ordinal);
        var readyIndex = html.IndexOf(readyMarker, StringComparison.Ordinal);
        Assert.True(consentIndex >= 0);
        Assert.True(readyIndex > consentIndex);
        Assert.Equal(1, Regex.Matches(html, Regex.Escape(readyMarker), RegexOptions.CultureInvariant).Count);
        Assert.True(html.IndexOf("window.malievLoadGoogleTagManager =", StringComparison.Ordinal) > readyIndex);
        var events = SerializedApplicationEvents(html);
        Assert.Equal(new[] { "maliev_lead_submitted", "generate_lead", "maliev_quote_decision" },
            events.Select(item => item.GetProperty("event").GetString()).ToArray());
        foreach (var item in events.Take(2))
        {
            Assert.Equal("request-714", item.GetProperty("transaction_id").GetString());
            Assert.Equal("11111111-2222-3333-4444-555555555555", item.GetProperty("journey_id").GetString());
            Assert.DoesNotContain(item.EnumerateObject(), property => property.Name is "email" or "name" or "filename" or "access_token" or "refresh_token");
        }
        Assert.Equal("quotation-714", events[2].GetProperty("transaction_id").GetString());
        Assert.True(html.LastIndexOf("window.malievAnalytics.emit({\"event\":", StringComparison.Ordinal) < readyIndex);
        var deletion = Assert.Single(first.Headers.GetValues("Set-Cookie"), value => value.StartsWith(prefix, StringComparison.Ordinal));
        Assert.Equal(prefix, deletion.Split(';', 2)[0]);
        using var follow = new HttpRequestMessage(HttpMethod.Get, $"/?culture={culture}");
        follow.Headers.Add("Cookie", deletion.Split(';', 2)[0]);
        using var second = await client.SendAsync(follow, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Empty(SerializedApplicationEvents(await second.Content.ReadAsStringAsync(timeout.Token)));
    }

    private static JsonElement[] SerializedApplicationEvents(string html) => Regex.Matches(
        html, "window\\.malievAnalytics\\.emit\\((\\{\"event\"[^\\r\\n]*?\\})\\);", RegexOptions.CultureInvariant)
        .Select(match =>
        {
            using var document = JsonDocument.Parse(match.Groups[1].Value);
            return document.RootElement.Clone();
        }).ToArray();
}
