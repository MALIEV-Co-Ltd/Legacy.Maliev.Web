using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Original browser friendship-response control; stubbed SDK is not live LINE qualification.</summary>
public sealed class LineFriendConfirmationOriginalBrowserTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("en", true, 1)]
    [InlineData("en", false, 0)]
    [InlineData("th", true, 1)]
    [InlineData("th", false, 0)]
    public async Task FriendshipResponse_ControlsConsentGatedSecondaryDiagnostic(string culture, bool friendFlag, int expectedCount)
    {
        using var custody = LineFriendBrowserCleanup.Reserve();
        WebApplicationFactory<Program>? factory = null;
        HttpClient? client = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        Exception? primary = null;
        try
        {
            factory = new LineFriendHost(BrowserHostIdentityVerifier.SourceProjectDirectory());
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var origin = new Uri($"http://127.0.0.1:{port}");
            factory.UseKestrel(port);
            client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.RouteAsync("https://static.line-scdn.net/liff/edge/2/sdk.js", route => route.FulfillAsync(new()
            {
                ContentType = "application/javascript",
                Body = "window.liff={init:()=>Promise.resolve(),isLoggedIn:()=>true,getFriendship:()=>Promise.resolve({friendFlag:"
                    + friendFlag.ToString().ToLowerInvariant() + "})};"
            }));
            await page.GotoAsync(new Uri(origin, $"/contact/line?culture={culture}").ToString());
            await page.WaitForFunctionAsync("() => document.body.dataset.lineBridgeReady === 'true'");
            Assert.Equal(0, await page.EvaluateAsync<int>("() => (window.dataLayer || []).filter(x => x && x.event === 'line_friend_confirmed').length"));
            await page.Locator("#cookieConsent [data-consent-action='accept']").ClickAsync();
            using var eventDocument = JsonDocument.Parse(await page.EvaluateAsync<string>("() => JSON.stringify((window.dataLayer || []).filter(x => x && x.event === 'line_friend_confirmed'))"));
            var events = eventDocument.RootElement;
            Assert.Equal(expectedCount, events.GetArrayLength());
            if (friendFlag)
            {
                var emitted = events.EnumerateArray().Single();
                Assert.Equal("line", emitted.GetProperty("channel").GetString());
                Assert.Equal("line_oa", emitted.GetProperty("destination").GetString());
                Assert.Equal("secondary", emitted.GetProperty("conversion_tier").GetString());
                Assert.Equal(new[] { "channel", "conversion_tier", "destination", "event" },
                    emitted.EnumerateObject().Select(property => property.Name).Order().ToArray());
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            await LineFriendBrowserCleanup.CloseAsync(custody,
                [
                    new("context", () => context?.CloseAsync() ?? Task.CompletedTask),
                    new("browser", () => browser?.CloseAsync() ?? Task.CompletedTask, ["context"]),
                    new("playwright", () => { playwright?.Dispose(); return Task.CompletedTask; }, ["browser"]),
                    new("client", () => { client?.Dispose(); return Task.CompletedTask; }),
                    new("factory", () => factory?.DisposeAsync().AsTask() ?? Task.CompletedTask, ["context", "browser", "client"])
                ], primary, TimeSpan.FromSeconds(10),
                receipts => output.WriteLine("line-friend-cleanup: " + JsonSerializer.Serialize(receipts)));

        }
    }
    [Fact]
    public void BrowserJsonText_PreservesAndRejectsRealExtraMetadata()
    {
        using var document = JsonDocument.Parse("""
            [{"$id":"controlled","event":"line_friend_confirmed","channel":"line","destination":"line_oa","conversion_tier":"secondary"}]
            """);
        var emitted = document.RootElement.EnumerateArray().Single();
        Assert.Equal("controlled", emitted.GetProperty("$id").GetString());
        Assert.Throws<Xunit.Sdk.EqualException>(() => Assert.Equal(
            new[] { "channel", "conversion_tier", "destination", "event" },
            emitted.EnumerateObject().Select(property => property.Name).Order().ToArray()));
    }

    private sealed class LineFriendHost(string contentRoot) : TestingWebApplicationFactory(contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting($"{LineLiffOptions.SectionName}:LiffId", "2000000000-AbCd_1234");
        }
    }
}
