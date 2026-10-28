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
    private static int ReserveFreePort() => BrowserTestServerPort.Reserve();

    [Theory]
    [InlineData("en", 320)]
    [InlineData("th", 320)]
    [InlineData("en", 375)]
    [InlineData("th", 375)]
    [InlineData("en", 820)]
    [InlineData("th", 820)]
    [InlineData("en", 992)]
    [InlineData("th", 992)]
    [InlineData("en", 1024)]
    [InlineData("th", 1024)]
    [InlineData("en", 1280)]
    [InlineData("th", 1280)]
    [InlineData("en", 1600)]
    [InlineData("th", 1600)]
    public async Task RealPartsRailExposesLongNamesAndKeyboardActions(string culture, int width)
    {
        await RunPartsRailAsync(culture, width, 800);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task CompactPartsRailKeepsFullNamesAndKeyboardActionsReachable(string culture)
    {
        await RunPartsRailAsync(culture, 1280, 720, expectCompact: true);
    }

    private async Task RunPartsRailAsync(string culture, int width, int height, bool expectCompact = false)
    {
        await using var factory = new TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory());
        var port = ReserveFreePort();
        var evidencePrefix = height == 800 ? $"sidebar-{culture}-{width}" : $"sidebar-{culture}-{width}-h{height}";
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        { ViewportSize = new() { Width = width, Height = height }, HasTouch = width <= 375 });
        await using var page = await context.NewPageAsync();
        await using var failureEvidence = new BrowserFailureEvidence(page,
            metadata => output.WriteLine("[sidebar-evidence-cleanup] " + metadata));
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
                        Path.Combine(evidenceDirectory, $"{evidencePrefix}-initial-navigation-timeout.json"),
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
        if (width == 1600)
            names = Enumerable.Range(1, 8).Select(index => $"{names[0]}-{index}.stl").ToArray();
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
        var compactDesktop = await page.EvaluateAsync<bool>(
            "() => matchMedia('(min-width: 64rem)').matches && !matchMedia('(min-height: 48rem)').matches");
        if (expectCompact) Assert.True(compactDesktop, "The compact cases must exercise the existing native-scroll height regime.");
        var rail = page.Locator("[data-workflow-parts]");
        await Assertions.Expect(rail.Locator("[data-workflow-part]")).ToHaveCountAsync(names.Length);
        await Assertions.Expect(page.Locator("[data-workflow-parts-panel] .instant-quote__parts-header [data-workflow-select-all]")).ToHaveCountAsync(1);
        await Assertions.Expect(rail.Locator("[data-workflow-select-all], [data-workflow-bulk-material]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-workflow-material-picker] [data-workflow-bulk-material]")).ToHaveCountAsync(1);
        if (width == 1600)
        {
            // Upload/reveal can leave the rail at its newest part. Exercise a forward gesture from the left edge.
            await rail.EvaluateAsync("rail => { rail.scrollLeft = 0; }");
            await rail.Locator("[data-workflow-part] button").First.HoverAsync();
            var beforeWheel = await rail.EvaluateAsync<double[]>("""
                rail => new Promise((resolve, reject) => {
                  let observer;
                  const timer = setTimeout(() => {
                    observer?.disconnect();
                    reject(new Error('The rail content-box measurement did not arrive.'));
                  }, 5000);
                  try {
                    observer = new ResizeObserver(entries => {
                      try {
                        const entry = entries.find(entry => entry.target === rail);
                        if (!entry) throw new Error('The rail content-box measurement is missing.');
                        const style = getComputedStyle(rail);
                        const paddingTop = parseFloat(style.paddingTop);
                        const paddingBottom = parseFloat(style.paddingBottom);
                        // Content-box size excludes native scrollbar space without integer CSSOM rounding.
                        resolve([rail.scrollLeft, rail.scrollWidth, rail.clientWidth, rail.scrollHeight, rail.clientHeight,
                          paddingTop, paddingBottom,
                          rail.querySelector('[data-workflow-part]')?.getBoundingClientRect().height ?? 0,
                          Math.max(0, ...Array.from(rail.children).slice(0, 16).map(child => child.getBoundingClientRect().height)),
                          entry.contentRect.height + paddingTop + paddingBottom]);
                      } catch (error) { reject(error); }
                      finally { observer.disconnect(); clearTimeout(timer); }
                    });
                    observer.observe(rail, { box: 'content-box' });
                  } catch (error) {
                    observer?.disconnect();
                    clearTimeout(timer);
                    reject(error);
                  }
                })
                """);
            Assert.Equal(0, beforeWheel[0]);
            Assert.True(beforeWheel[1] > beforeWheel[2], "Eight parts must overflow the desktop rail for this regression to be meaningful.");
            Assert.True(beforeWheel[3] <= beforeWheel[4],
                $"This gesture must exercise the horizontal rail, not native vertical scrolling. scrollHeight={beforeWheel[3]}, clientHeight={beforeWheel[4]}, paddingTop={beforeWheel[5]}, paddingBottom={beforeWheel[6]}, firstCardHeight={beforeWheel[7]}, maxChildHeight={beforeWheel[8]}, fractionalClientHeight={beforeWheel[9]}.");
            Assert.True(beforeWheel[8] + beforeWheel[5] + beforeWheel[6] <= beforeWheel[9],
                $"Full part names and 44px actions must fit inside the desktop rail padding without clipping. scrollLeft={beforeWheel[0]}, scrollWidth={beforeWheel[1]}, clientWidth={beforeWheel[2]}, scrollHeight={beforeWheel[3]}, clientHeight={beforeWheel[4]}, paddingTop={beforeWheel[5]}, paddingBottom={beforeWheel[6]}, firstCardHeight={beforeWheel[7]}, maxChildHeight={beforeWheel[8]}, fractionalClientHeight={beforeWheel[9]}.");
            await page.Mouse.WheelAsync(0, 120);
            await page.WaitForFunctionAsync("() => document.querySelector('[data-workflow-parts]').scrollLeft > 0");
            Assert.True(await rail.EvaluateAsync<double>("rail => rail.scrollLeft") > 0,
                "A vertical wheel gesture must move the desktop parts rail horizontally.");
            await Assertions.Expect(page.Locator("[data-workflow-select-all]")).ToBeInViewportAsync();
        }
        await Assertions.Expect(page.Locator("[data-workflow-parts-panel] [data-workflow-part-count]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-workflow-parts-panel] [data-workflow-part-count]")).ToHaveAttributeAsync("aria-live", "polite");
        await Assertions.Expect(page.Locator("[data-workflow-parts-panel] [data-workflow-part-count]")).ToHaveTextAsync(culture == "th" ? $"{names.Length} ชิ้น" : $"{names.Length} parts");
        await Assertions.Expect(rail.Locator(".iq-compact-dropzone strong")).ToHaveTextAsync(culture == "th" ? "เพิ่มชิ้นงาน" : "Add parts");
        var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        await Assertions.Expect(review).ToBeEnabledAsync();
        var directory = Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR");
        async Task Capture(string state)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            failureEvidence.AwaitBoundary(state + "-screenshot", false);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{evidencePrefix}-{state}.png"), FullPage = true });
            failureEvidence.AwaitBoundary(state + "-screenshot", true);
            failureEvidence.AwaitBoundary(state + "-geometry", false);
            var geometry = await rail.EvaluateAsync<JsonElement>("""
                rail => {
                  const panel=rail.closest('[data-workflow-parts-panel]');
                  const workflow=rail.closest('.instant-quote__workflow');
                  const style=getComputedStyle(rail);
                  return { viewport:innerWidth, document:document.documentElement.scrollWidth,
                    viewportHeight:innerHeight,workflowBounds:workflow.getBoundingClientRect().toJSON(),
                    panelBounds:panel.getBoundingClientRect().toJSON(),
                    headerBounds:panel.querySelector('.instant-quote__parts-header').getBoundingClientRect().toJSON(),
                    rail:{client:rail.clientWidth,scroll:rail.scrollWidth,clientHeight:rail.clientHeight,
                      scrollHeight:rail.scrollHeight,scrollLeft:rail.scrollLeft,scrollTop:rail.scrollTop,
                      paddingTop:parseFloat(style.paddingTop)||0,paddingBottom:parseFloat(style.paddingBottom)||0,
                      bounds:rail.getBoundingClientRect().toJSON()},
                    cards:[...rail.querySelectorAll('[data-workflow-part]')].slice(0,8).map(card=>{
                      const heading=card.querySelector('h4'),range=document.createRange();
                      range.selectNodeContents(heading);
                      return {width:card.clientWidth,scroll:card.scrollWidth,bounds:card.getBoundingClientRect().toJSON(),
                        headingBounds:heading.getBoundingClientRect().toJSON(),
                        nameLines:[...range.getClientRects()].slice(0,16).map(r=>[r.left,r.top,r.right,r.bottom]),
                        buttons:[...card.querySelectorAll('button')].slice(0,8).map(button=>({bounds:button.getBoundingClientRect().toJSON()}))};
                    }) };
                }
                """);
            failureEvidence.AwaitBoundary(state + "-geometry", true);
            failureEvidence.AwaitBoundary(state + "-write", false);
            await File.WriteAllTextAsync(Path.Combine(directory, $"{evidencePrefix}-{state}.json"), geometry.GetRawText());
            failureEvidence.AwaitBoundary(state + "-write", true);
        }
        if (width >= 1024)
        {
            Assert.True(await page.EvaluateAsync<bool>("""
                () => {
                  const workflow=document.querySelector('.instant-quote__workflow');
                  const viewer=workflow.querySelector(':scope > [data-workflow-viewer]').getBoundingClientRect();
                  const rail=workflow.querySelector(':scope > [data-workflow-parts-panel]').getBoundingClientRect();
                  const configuration=workflow.querySelector(':scope > [data-workflow-configuration]').getBoundingClientRect();
                  return Math.abs(rail.left-viewer.left)<=1 && Math.abs(rail.right-viewer.right)<=1
                    && rail.top>=viewer.bottom-1 && viewer.width>0 && viewer.height>0
                    && configuration.left>=viewer.right-1 && Math.abs(configuration.top-viewer.top)<=1
                    && configuration.bottom>=rail.bottom-1;
                }
                """), "The actual parts dock must sit below the preview while configuration owns the other column.");
        }
        await rail.ScrollIntoViewIfNeededAsync();
        await Capture("initial");
        Assert.Contains("/instantquotation/3d-printing", page.Url, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(await page.TitleAsync()));
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        if (width < 1024)
            Assert.True(await rail.EvaluateAsync<bool>("rail => rail.scrollWidth <= rail.clientWidth + 1"),
                "A narrow parts rail must expose names and actions without nested horizontal clipping.");
        foreach (var name in names)
        {
            var card = rail.Locator("[data-workflow-part]").Filter(new() { HasText = name });
            await card.ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(card.Locator("h4")).ToBeVisibleAsync();
            if (compactDesktop)
            {
                var heading = card.Locator("h4");
                await heading.ScrollIntoViewIfNeededAsync();
                var fullNameReachable = await heading.EvaluateAsync<bool>("""
                    heading => {
                      const card=heading.closest('[data-workflow-part]').getBoundingClientRect();
                      const rail=heading.closest('[data-workflow-parts]').getBoundingClientRect();
                      const panel=heading.closest('[data-workflow-parts-panel]').getBoundingClientRect();
                      const range=document.createRange(); range.selectNodeContents(heading);
                      const lines=[...range.getClientRects()];
                      return lines.length>0 && lines.every(r=>r.left>=Math.max(card.left,rail.left,panel.left,0)-1
                        && r.right<=Math.min(card.right,rail.right,panel.right,innerWidth)+1
                        && r.top>=Math.max(card.top,rail.top,panel.top,0)-1
                        && r.bottom<=Math.min(card.bottom,rail.bottom,panel.bottom,innerHeight)+1);
                    }
                    """);
                var compactFailureGeometry = fullNameReachable ? string.Empty
                    : (await heading.EvaluateAsync<JsonElement>("""
                        heading => {
                          const rect = element => {
                            const r=element.getBoundingClientRect();
                            return [r.left,r.top,r.right,r.bottom];
                          };
                          const box = element => {
                            const style=getComputedStyle(element);
                            return {bounds:rect(element),client:[element.clientWidth,element.clientHeight],
                              scroll:[element.scrollLeft,element.scrollTop,element.scrollWidth,element.scrollHeight],
                              padding:[parseFloat(style.paddingTop)||0,parseFloat(style.paddingBottom)||0],
                              border:[parseFloat(style.borderTopWidth)||0,parseFloat(style.borderBottomWidth)||0]};
                          };
                          const rail=heading.closest('[data-workflow-parts]');
                          const panel=heading.closest('[data-workflow-parts-panel]');
                          const workflow=heading.closest('.instant-quote__workflow');
                          const range=document.createRange(); range.selectNodeContents(heading);
                          const lines=[...range.getClientRects()];
                          return {viewport:[innerWidth,innerHeight],workflow:box(workflow),panel:box(panel),
                            header:rect(panel.querySelector('.instant-quote__parts-header')),
                            rail:box(rail),card:box(heading.closest('[data-workflow-part]')),heading:box(heading),
                            lineCount:lines.length,lines:lines.slice(0,16).map(r=>[r.left,r.top,r.right,r.bottom])};
                        }
                        """)).GetRawText();
                Assert.True(fullNameReachable,
                    "Every full part-name line must be reachable through the compact rail's native vertical scrolling. "
                    + compactFailureGeometry);
                var compactScroll = await rail.EvaluateAsync<double[]>("rail => [rail.scrollTop, rail.clientHeight, rail.scrollHeight]");
                Assert.True(compactScroll[0] > 0 && compactScroll[2] > compactScroll[1],
                    "A compact long-name case must exercise the rail's native vertical scrolling.");
            }
            var nameGeometry = await card.Locator("h4").EvaluateAsync<JsonElement>("""
                heading => {
                  const card=heading.closest('[data-workflow-part]').getBoundingClientRect();
                  const railElement=heading.closest('[data-workflow-parts]');
                  const rail=railElement.getBoundingClientRect();
                  const range=document.createRange(); range.selectNodeContents(heading);
                  const lines=[...range.getClientRects()];
                  const fits=lines.length>0 && lines.every(r=>r.left>=Math.max(card.left,rail.left)-1 && r.right<=Math.min(card.right,rail.right)+1
                    && r.top>=Math.max(card.top,rail.top)-1 && r.bottom<=Math.min(card.bottom,rail.bottom)+1);
                  const failures=[];
                  for (let index=0; index<lines.length && failures.length<8; index++) {
                    const r=lines[index];
                    const edges=(r.left<Math.max(card.left,rail.left)-1 ? 1 : 0)
                      | (r.right>Math.min(card.right,rail.right)+1 ? 2 : 0)
                      | (r.top<Math.max(card.top,rail.top)-1 ? 4 : 0)
                      | (r.bottom>Math.min(card.bottom,rail.bottom)+1 ? 8 : 0);
                    if (edges) failures.push([index,edges,r.left,r.top,r.right,r.bottom]);
                  }
                  const rect=heading.getBoundingClientRect();
                  const style=getComputedStyle(heading);
                  return { fits, viewport:[innerWidth,innerHeight],
                    card:[card.left,card.top,card.right,card.bottom],
                    rail:[rail.left,rail.top,rail.right,rail.bottom],
                    heading:[rect.left,rect.top,rect.right,rect.bottom],
                    scroll:[railElement.scrollLeft,railElement.scrollTop,railElement.clientWidth,
                      railElement.clientHeight,railElement.scrollWidth,railElement.scrollHeight],
                    typography:[parseFloat(style.fontSize) || 0,parseFloat(style.lineHeight) || 0],
                    rangeCount:lines.length, firstFailures:failures };
                }
                """);
            Assert.True(nameGeometry.GetProperty("fits").GetBoolean(),
                "Every rendered line of the full part name must fit its card and rail. "
                + "Rectangles=[left,top,right,bottom]; failures=[index,edgeMask,left,top,right,bottom], "
                + "edges=left1/right2/top4/bottom8. " + nameGeometry.GetRawText());
            var thumbnail = card.Locator("img[data-workflow-thumbnail]");
            await Assertions.Expect(thumbnail).ToHaveCountAsync(1);
            await using var thumbnailElement = await thumbnail.ElementHandleAsync()
                ?? throw new InvalidOperationException("The owning part thumbnail is missing.");
            await using var thumbnailReady = await page.WaitForFunctionAsync(
                "image => image.isConnected && image.complete && image.naturalWidth > 0 && image.naturalHeight > 0",
                thumbnailElement);
            var thumbnailGeometry = await thumbnail.EvaluateAsync<JsonElement>("""
                image => {
                  const r=image.getBoundingClientRect();
                  const card=image.closest('[data-workflow-part]').getBoundingClientRect();
                  const rail=image.closest('[data-workflow-parts]').getBoundingClientRect();
                  return {
                    loaded:image.complete && image.naturalWidth>0 && image.naturalHeight>0,
                    positive:r.width>0 && r.height>0,
                    square:Math.abs(r.width-r.height)<=1,
                    insideCard:r.left>=card.left-1 && r.right<=card.right+1
                      && r.top>=card.top-1 && r.bottom<=card.bottom+1,
                    insideRailHorizontally:r.left>=rail.left-1 && r.right<=rail.right+1,
                    objectFit:getComputedStyle(image).objectFit,
                    width:r.width,height:r.height,
                    naturalWidth:image.naturalWidth,naturalHeight:image.naturalHeight };
                }
                """);
            Assert.True(thumbnailGeometry.GetProperty("loaded").GetBoolean()
                && thumbnailGeometry.GetProperty("positive").GetBoolean()
                && thumbnailGeometry.GetProperty("square").GetBoolean()
                && thumbnailGeometry.GetProperty("insideCard").GetBoolean()
                && thumbnailGeometry.GetProperty("insideRailHorizontally").GetBoolean()
                && thumbnailGeometry.GetProperty("objectFit").GetString() == "contain",
                "The loaded square part thumbnail must fit its card and the rail width. "
                + thumbnailGeometry.GetRawText());
            var view = card.Locator("button").First;
            var remove = card.Locator("button").Last;
            var keyboardOperation = name == names[0] ? "first-keyboard" : "second-keyboard";
            failureEvidence.AwaitBoundary(keyboardOperation, false);
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
            failureEvidence.AwaitBoundary(keyboardOperation, true);
            await Capture(name == names[0] ? "first-focused" : "second-focused");
        }
        if (compactDesktop)
        {
            var selectAll = page.Locator("[data-workflow-parts-panel] [data-workflow-select-all]");
            await selectAll.FocusAsync();
            await Assertions.Expect(selectAll).ToBeFocusedAsync();
            await Assertions.Expect(selectAll).ToBeInViewportAsync();
        }
        // Historical inputs have no live FileService deletion capability. Test Remove reachability,
        // and exercise real View/Review/Customer transitions without claiming upload deletion proof.
        Assert.Equal(names, (await store.GetAsync(sessionId, null, default))!.Parts.Select(part => part.DisplayFileName));
        failureEvidence.Stage("Review");
        failureEvidence.AwaitBoundary("review", false);
        await review.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-workflow-review-part]")).ToHaveCountAsync(names.Length);
        // Complete the application's native accessibility focus transition before moving focus.
        await Assertions.Expect(page.Locator("[data-workflow-review]")).ToBeFocusedAsync();
        Assert.Equal(0, await page.Locator("[data-workflow-parts]").CountAsync());
        var continueButton = page.Locator("[data-review-continue]");
        await continueButton.FocusAsync();
        await Assertions.Expect(continueButton).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-workflow-customer-details]")).ToBeFocusedAsync();
        Assert.Equal(0, await page.Locator("[data-workflow-parts]").CountAsync());
        failureEvidence.AwaitBoundary("review", true);
        failureEvidence.Stage("CustomerInput");
        failureEvidence.AwaitBoundary("customer-input", false);
        var firstName = page.Locator("input[name='FirstName']");
        var longName = culture == "th" ? "ชื่อลูกค้าทดสอบสำหรับตรวจสอบความกว้างของแบบฟอร์ม" : "LongCustomerNameForResponsiveLayoutVerification";
        await firstName.FillAsync(longName);
        await Assertions.Expect(firstName).ToHaveValueAsync(longName);
        Assert.True(await firstName.EvaluateAsync<bool>("input => { const r=input.getBoundingClientRect(); return r.width>=44 && r.left>=0 && r.right<=innerWidth; }"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        if (!string.IsNullOrWhiteSpace(directory))
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{evidencePrefix}-customer.png"), FullPage = true });
        failureEvidence.AwaitBoundary("customer-input", true);
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
