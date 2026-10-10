using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

// Same normal cookie/protected historical-input seam as MaterialPricingDisplayBrowserTests.
// This proves real Razor/circuit layout, not FileService admission or commercial pricing.
public sealed class InstantQuotationSidebarLayoutBrowserTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("en", 320)]
    [InlineData("th", 320)]
    [InlineData("en", 375)]
    [InlineData("th", 375)]
    [InlineData("en", 820)]
    [InlineData("th", 820)]
    [InlineData("en", 992)]
    [InlineData("th", 992)]
    [InlineData("en", 1280)]
    [InlineData("th", 1280)]
    public async Task RealPartsRailExposesLongNamesAndKeyboardActions(string culture, int width)
    {
        await using var factory = new TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        { ViewportSize = new() { Width = width, Height = 800 }, HasTouch = width <= 375 });
        await using var page = await context.NewPageAsync();
        using var failureEvidence = new BrowserFailureEvidence(page);
        failureEvidence.Stage("Navigation");
        var errors = new List<string>();
        var consoleErrors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, message) => { if (message.Type == "error") consoleErrors.Add(message.Text); };
        var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        const int maximumNavigationEvents = 200;
        const int maximumOutstandingRequests = 100;
        var navigationEvidenceLock = new object();
        var navigationRequests = new Dictionary<IRequest, string>();
        var navigationEvents = new List<object>();
        string SafeRequestPath(IRequest request)
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)) return "unparseable";
            var path = $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}";
            return path.Length <= 256 ? path : path[..256];
        }
        EventHandler<IRequest> onNavigationRequest = (_, request) =>
        {
            lock (navigationEvidenceLock)
            {
                if (navigationRequests.Count < maximumOutstandingRequests)
                    navigationRequests.TryAdd(request, SafeRequestPath(request));
                if (navigationEvents.Count < maximumNavigationEvents)
                    navigationEvents.Add(new { Event = "request", Path = SafeRequestPath(request), request.ResourceType });
            }
        };
        EventHandler<IRequest> onNavigationRequestFinished = (_, request) =>
        {
            lock (navigationEvidenceLock) navigationRequests.Remove(request);
        };
        EventHandler<IRequest> onNavigationRequestFailed = (_, request) =>
        {
            lock (navigationEvidenceLock)
            {
                navigationRequests.Remove(request);
                if (navigationEvents.Count < maximumNavigationEvents)
                    navigationEvents.Add(new { Event = "failed", Path = SafeRequestPath(request), request.ResourceType });
            }
        };
        EventHandler<IResponse> onNavigationResponse = (_, response) =>
        {
            lock (navigationEvidenceLock)
            {
                if (navigationEvents.Count < maximumNavigationEvents)
                    navigationEvents.Add(new { Event = "response", Path = SafeRequestPath(response.Request), response.Status });
            }
        };
        page.Request += onNavigationRequest;
        page.RequestFinished += onNavigationRequestFinished;
        page.RequestFailed += onNavigationRequestFailed;
        page.Response += onNavigationResponse;
        try
        {
            Assert.Equal(200, (await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle }))?.Status);
        }
        catch (TimeoutException)
        {
            try
            {
                var evidenceDirectory = Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(evidenceDirectory))
                {
                    string evidence;
                    lock (navigationEvidenceLock)
                    {
                        evidence = JsonSerializer.Serialize(new
                        {
                            Culture = culture,
                            Width = width,
                            Stage = "initial-navigation-networkidle",
                            Events = navigationEvents.ToArray(),
                            Outstanding = navigationRequests.Values.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                            PageErrorCount = errors.Count,
                            ConsoleErrorCount = consoleErrors.Count,
                        });
                    }
                    Directory.CreateDirectory(evidenceDirectory);
                    using var evidenceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await File.WriteAllTextAsync(
                        Path.Combine(evidenceDirectory, $"sidebar-{culture}-{width}-initial-navigation-timeout.json"),
                        evidence,
                        evidenceDeadline.Token);
                }
            }
            catch (Exception)
            {
                // Diagnostic failure must never replace the original navigation timeout.
                try
                {
                    Console.Error.WriteLine("Sidebar initial-navigation evidence could not be retained.");
                }
                catch (Exception)
                {
                    // Best-effort reporting only; preserve the original timeout.
                }
            }
            throw;
        }
        finally
        {
            page.Request -= onNavigationRequest;
            page.RequestFinished -= onNavigationRequestFinished;
            page.RequestFailed -= onNavigationRequestFailed;
            page.Response -= onNavigationResponse;
        }
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.IsVisibleAsync()) await consent.ClickAsync();
        var cookie = Assert.Single(await context.CookiesAsync(), item => item.Name == InstantQuotationSessionIdentityCookie.CookieName);
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{cookie.Name}={cookie.Value}";
        var sessionId = factory.Services.GetRequiredService<InstantQuotationSessionIdentityCookie>().TryRead(http)!;
        Assert.False(string.IsNullOrEmpty(sessionId));
        var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        var session = (await store.GetAsync(sessionId, null, default))!;
        var names = new[]
        {
            culture == "th" ? "ชิ้นส่วนทดสอบชื่อยาวสำหรับตรวจสอบการแสดงผลครบถ้วน-รุ่นหนึ่ง.stl" : "long-customer-item-name-without-truncated-identification-first.stl",
            culture == "th" ? "ชิ้นส่วนทดสอบชื่อยาวสำหรับตรวจสอบการแสดงผลครบถ้วน-รุ่นสอง.stl" : "long-customer-item-name-without-truncated-identification-second.stl",
        };
        var parts = names.Select(name =>
        {
            var digest = new string('a', 64);
            var reference = new InstantQuotationUploadReference(Guid.NewGuid().ToString("D"));
            var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                InstantQuotationUploadResult.Succeeded("synthetic-historical-browser", reference, digest),
                new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                    Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(), 12, 1, true, false, false, 1))!;
            return new InstantQuotationPart(Guid.NewGuid(), name, reference, geometry, new("M68", "White", 1));
        }).ToArray();
        Assert.True(await store.PutAsync(session with { RequestState = new(parts) }, null, default));
        failureEvidence.Stage("Reload");
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        failureEvidence.Stage("PartsRail");
        var rail = page.Locator("[data-workflow-parts]");
        await Assertions.Expect(rail.Locator("[data-workflow-part]")).ToHaveCountAsync(2);
        var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        await Assertions.Expect(review).ToBeEnabledAsync();
        var directory = Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR");
        async Task Capture(string state)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"sidebar-{culture}-{width}-{state}.png"), FullPage = true });
            var geometry = await rail.EvaluateAsync<JsonElement>("""
                rail => ({ viewport:innerWidth, document:document.documentElement.scrollWidth,
                  rail:{client:rail.clientWidth,scroll:rail.scrollWidth},
                  cards:[...rail.querySelectorAll('[data-workflow-part]')].map(card=>({
                    width:card.clientWidth,scroll:card.scrollWidth,name:card.querySelector('h4').textContent,
                    bounds:card.getBoundingClientRect().toJSON(),
                    buttons:[...card.querySelectorAll('button')].map(button=>({text:button.textContent,bounds:button.getBoundingClientRect().toJSON()}))})) })
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, $"sidebar-{culture}-{width}-{state}.json"), geometry.GetRawText());
        }
        await rail.ScrollIntoViewIfNeededAsync();
        await Capture("initial");
        Assert.Contains("/instantquotation/3d-printing", page.Url, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(await page.TitleAsync()));
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        if (width < 992)
            Assert.True(await rail.EvaluateAsync<bool>("rail => rail.scrollWidth <= rail.clientWidth + 1"),
                "A narrow parts rail must expose names and actions without nested horizontal clipping.");
        foreach (var name in names)
        {
            var card = rail.Locator("[data-workflow-part]").Filter(new() { HasText = name });
            await card.ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(card.Locator("h4")).ToBeVisibleAsync();
            Assert.True(await card.Locator("h4").EvaluateAsync<bool>("""
                heading => {
                  const card=heading.closest('[data-workflow-part]').getBoundingClientRect();
                  const rail=heading.closest('[data-workflow-parts]').getBoundingClientRect();
                  const range=document.createRange(); range.selectNodeContents(heading);
                  const lines=[...range.getClientRects()];
                  return lines.length>0 && lines.every(r=>r.left>=Math.max(card.left,rail.left)-1 && r.right<=Math.min(card.right,rail.right)+1
                    && r.top>=Math.max(card.top,rail.top)-1 && r.bottom<=Math.min(card.bottom,rail.bottom)+1);
                }
                """), "Every rendered line of the full part name must fit its card and rail.");
            var view = card.Locator("button").First;
            var remove = card.Locator("button").Last;
            await view.FocusAsync();
            Assert.True(await view.EvaluateAsync<bool>("""
                button => { const r=button.getBoundingClientRect();
                  return r.width>=44 && r.height>=44
                    && button.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)); }
                """), "The keyboard View action must be exposed and usable.");
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(view).ToHaveAttributeAsync("aria-pressed", "true");
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await remove.EvaluateAsync<bool>("button => document.activeElement === button"));
            Assert.True(await remove.EvaluateAsync<bool>("""
                button => { const r=button.getBoundingClientRect(), style=getComputedStyle(button);
                  return r.width>=44 && r.height>=44 && style.outlineStyle!=='none'
                    && button.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)); }
                """), "The focused Remove action must be exposed, usable, and visibly focused.");
            await Capture(name == names[0] ? "first-focused" : "second-focused");
        }
        // Historical inputs have no live FileService deletion capability. Test Remove reachability,
        // and exercise real View/Review/Customer transitions without claiming upload deletion proof.
        Assert.Equal(names, (await store.GetAsync(sessionId, null, default))!.Parts.Select(part => part.DisplayFileName));
        failureEvidence.Stage("Review");
        await review.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-workflow-review-part]")).ToHaveCountAsync(2);
        // Complete the application's native accessibility focus transition before moving focus.
        await Assertions.Expect(page.Locator("[data-workflow-review]")).ToBeFocusedAsync();
        var continueButton = page.Locator("[data-review-continue]");
        await continueButton.FocusAsync();
        await Assertions.Expect(continueButton).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-workflow-customer-details]")).ToBeFocusedAsync();
        failureEvidence.Stage("CustomerInput");
        var firstName = page.Locator("input[name='FirstName']");
        var longName = culture == "th" ? "ชื่อลูกค้าทดสอบสำหรับตรวจสอบความกว้างของแบบฟอร์ม" : "LongCustomerNameForResponsiveLayoutVerification";
        await firstName.FillAsync(longName);
        await Assertions.Expect(firstName).ToHaveValueAsync(longName);
        Assert.True(await firstName.EvaluateAsync<bool>("input => { const r=input.getBoundingClientRect(); return r.width>=44 && r.left>=0 && r.right<=innerWidth; }"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        if (!string.IsNullOrWhiteSpace(directory))
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"sidebar-{culture}-{width}-customer.png"), FullPage = true });
        try
        {
            Assert.Empty(errors);
            Assert.Empty(consoleErrors);
        }
        catch (Exception original)
        {
            await failureEvidence.AttachAsync(original, () => page.EvaluateAsync<string>("""
                () => JSON.stringify({
                  workflow:document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                  parts:document.querySelectorAll('[data-workflow-part]').length,
                  uploadRows:document.querySelectorAll('[data-workflow-upload-item]').length,
                  progress:document.querySelectorAll('[data-workflow-upload-item] progress').length > 0,
                  errorVisible:document.querySelector('#blazor-error-ui')?.style.display === 'block'
                })
                """), metadata => output.WriteLine("[sidebar-failure] " + metadata));
            throw;
        }
    }
}
