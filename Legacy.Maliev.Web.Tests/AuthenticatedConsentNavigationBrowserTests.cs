using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises GA4 identity through normal disposable Auth, Web session and consent navigation.</summary>
/// <param name="authority">The existing joined Auth/PostgreSQL/Redis authority fixture.</param>
public sealed class AuthenticatedConsentNavigationBrowserTests(MemberAuthorityFixture authority)
    : IClassFixture<MemberAuthorityFixture>
{
    /// <summary>Normal login withholds identity until a later consented document and clears it on revoke and logout.</summary>
    /// <param name="retained">Whether to exercise the retained Razor route rather than active static SSR.</param>
    /// <param name="culture">The actual document culture.</param>
    /// <returns>A task completing all browser, cookie and authority-state assertions.</returns>
    [Theory]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    public async Task NormalHttpsLogin_ConsentGrantNextDocumentAndLogoutRespectDurableIdentity(
        bool retained, string culture)
    {
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = new Uri($"https://127.0.0.1:{port}");
        await using var parent = authority.Web(retained);
        await using var host = parent.WithWebHostBuilder(builder =>
            builder.UseContentRoot(BrowserHostIdentityVerifier.SourceProjectDirectory()));
        // The derived factory delegates CreateHost to its parent, which does not hold its port setting.
        host.UseKestrel(options => options.Listen(IPAddress.Loopback, port, endpoint => endpoint.UseHttps(certificate)));
        host.StartServer();
        using var transport = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, peer, _, _) => request.RequestUri?.Host == "127.0.0.1"
                && peer is not null && CryptographicOperations.FixedTimeEquals(peer.GetCertHash(), certificate.GetCertHash()),
        };
        using var startup = new HttpClient(transport) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15) };
        using var loginReady = await startup.GetAsync($"/Account/Login?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, loginReady.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        context.SetDefaultTimeout(30000);
        context.SetDefaultNavigationTimeout(30000);
        var external = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await context.RouteAsync("**/*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            if (uri.Authority == origin.Authority) await route.ContinueAsync();
            else
            {
                external.AddOrUpdate(uri.Host, 1, (_, count) => count + 1);
                await route.AbortAsync();
            }
        });
        await using var page = await context.NewPageAsync();
        var memberPath = $"/Member/Account?culture={culture}";
        await page.GotoAsync(new Uri(origin, $"/Account/Login?culture={culture}&returnUrl={Uri.EscapeDataString(memberPath)}").ToString());
        await page.Locator("[data-consent-action='reject']").ClickAsync();
        await page.Locator("#Email").FillAsync("member-crawl@example.test");
        await page.Locator("#Password").FillAsync(authority.Password);
        await page.Locator("#customer-login button[type='submit']").ClickAsync();
        await page.WaitForURLAsync("**/Member/Account*");
        await Assertions.Expect(page.Locator("[data-migration-component='member-account-index-content']")).ToHaveCountAsync(1);
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Equal(retained ? 0 : 1, await page.Locator("[data-migration-route-owner='blazor-static-ssr']").CountAsync());
        Assert.Empty(IdentityValues(await Layer(page)));
        Assert.Empty(external);

        var cookie = Assert.Single(await context.CookiesAsync(), value => value.Name == "__Host-Maliev.Legacy.Session");
        Assert.True(cookie.Secure && cookie.HttpOnly);
        var options = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(cookie.Value);
        Assert.NotNull(ticket);
        Assert.Equal("customer:1", ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        var sessionId = ticket.Principal.FindFirstValue(AccountSessionManager.SessionIdClaim);
        Assert.False(string.IsNullOrEmpty(sessionId));
        var store = host.Services.GetRequiredService<IAccountSessionStore>();
        var session = await store.GetAsync(sessionId!, default);
        Assert.NotNull(session);
        var authSessionId = await authority.AssertActive(session.RefreshToken);
        var authenticatedHtml = await page.ContentAsync();
        Assert.False(authenticatedHtml.Contains(session.AccessToken, StringComparison.Ordinal), "Access tokens must remain server-side.");
        Assert.False(authenticatedHtml.Contains(session.RefreshToken, StringComparison.Ordinal), "Refresh tokens must remain server-side.");

        // Only the prior denial preference is removed to expose the existing real grant UI.
        // The normally issued Secure/HttpOnly session and server store remain intact.
        await context.ClearCookiesAsync(new() { Name = "maliev_tracking_consent" });
        await page.GotoAsync(new Uri(origin, memberPath).ToString());
        Assert.Empty(IdentityValues(await Layer(page)));
        await page.Locator("[data-consent-action='accept']").ClickAsync();
        var grantedInDocument = await Layer(page);
        Assert.Empty(IdentityValues(grantedInDocument));
        AssertReadyOrder(grantedInDocument, expectIdentity: false);
        Assert.Contains(await context.CookiesAsync(), value => value.Name == ".AspNet.Consent" && value.Value == "yes");
        await page.ReloadAsync();
        var nextDocument = await Layer(page);
        Assert.Equal(["customer:1"], IdentityValues(nextDocument));
        AssertReadyOrder(nextDocument, expectIdentity: true);
        Assert.Equal(1, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
        await page.EvaluateAsync("() => { window.malievAnalytics.setConsent('denied'); window.malievAnalytics.setConsent('granted'); window.malievLoadGoogleTagManager(); }");
        var cycle = await Layer(page);
        Assert.Equal(1, await page.Locator("script[data-maliev-gtm-loader]").CountAsync());
        var lastUpdate = cycle.EnumerateArray().Last(value => value.ValueKind == JsonValueKind.Array
            && value[0].GetString() == "set" && value[1].TryGetProperty("user_id", out _));
        Assert.Equal(JsonValueKind.Null, lastUpdate[1].GetProperty("user_id").ValueKind);
        Assert.Equal(["customer:1"], IdentityValues(cycle));
        Assert.Single(cycle.EnumerateArray(), IsReady);

        await page.GotoAsync(new Uri(origin, $"/Account/Logout?culture={culture}").ToString());
        await page.Locator("main form button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        Assert.Null(await store.GetAsync(sessionId!, default));
        await authority.AssertRevoked(authSessionId);
        await page.GotoAsync(new Uri(origin, $"/?culture={culture}").ToString());
        Assert.Empty(IdentityValues(await Layer(page)));
        Assert.DoesNotContain("member-crawl@example.test", JsonSerializer.Serialize(await Layer(page)), StringComparison.Ordinal);
    }

    private static bool IsReady(JsonElement value) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("event", out var name) && name.GetString() == "maliev_analytics_ready";

    private static string[] IdentityValues(JsonElement layer) => layer.EnumerateArray()
        .Where(value => value.ValueKind == JsonValueKind.Array && value[0].GetString() == "set"
            && value[1].TryGetProperty("user_id", out var id) && id.ValueKind == JsonValueKind.String)
        .Select(value => value[1].GetProperty("user_id").GetString()!).ToArray();

    private static void AssertReadyOrder(JsonElement layer, bool expectIdentity)
    {
        var values = layer.EnumerateArray().ToArray();
        Assert.Equal("consent", values[0][0].GetString());
        Assert.Equal("default", values[0][1].GetString());
        var ready = Array.FindIndex(values, IsReady);
        Assert.True(ready > 0);
        Assert.Equal("event", Assert.Single(values[ready].EnumerateObject()).Name);
        Assert.Single(values, IsReady);
        var loader = Array.FindIndex(values, value => value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("event", out var name) && name.GetString() == "gtm.js");
        Assert.True(loader > ready);
        if (expectIdentity)
        {
            var identity = Array.FindIndex(values, value => value.ValueKind == JsonValueKind.Array
                && value[0].GetString() == "set" && value[1].TryGetProperty("user_id", out var id)
                && id.ValueKind == JsonValueKind.String);
            Assert.True(identity > 0 && identity < ready);
        }
    }

    private static async Task<JsonElement> Layer(IPage page)
    {
        var json = await page.EvaluateAsync<string>("() => JSON.stringify(window.dataLayer.map(value => Object.prototype.toString.call(value) === '[object Arguments]' ? Array.from(value) : value))");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
