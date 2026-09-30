using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationWallThicknessRealUploadBrowserTests
{
    [Theory]
    [InlineData(320, "th")]
    [InlineData(375, "en")]
    public async Task UploadedPartShowsOnlyPhysicallyVerifiedQuantityAndReviewActionsReachable(int width, string culture)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.True(readiness.StatusCode == HttpStatusCode.OK,
            await readiness.Content.ReadAsStringAsync());

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 700 },
            HasTouch = true,
            IsMobile = true,
        });
        await using var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateBoxStl(20, 20, 5);
        try
        {
            await page.SetInputFilesAsync("#instant-quote-files", path);
            await page.Locator("[data-workflow-price-tier]").Last.WaitForAsync();
            var viewerControlsFit = await page.Locator("[data-workflow-viewer] > button:not(.instant-quote__thickness-toggle)")
                .EvaluateAllAsync<bool>("""
                    buttons => {
                      const thickness = document.querySelector('.instant-quote__thickness-toggle').getBoundingClientRect();
                      return buttons.length === 3 && buttons.every(button => {
                        const box = button.getBoundingClientRect();
                        return box.width >= 88 && box.height >= 44 && box.right < thickness.left - 4
                          && button.scrollWidth <= button.clientWidth + 1
                          && button.scrollHeight <= button.clientHeight + 1;
                      });
                    }
                    """);
            Assert.True(viewerControlsFit, "Localized viewer controls must not collapse into vertical text columns.");
            var tiers = page.Locator("[data-workflow-price-tier]");
            Assert.Equal(1, await tiers.CountAsync());
            var tierRegion = page.Locator("[data-workflow-bulk-pricing]");
            await tierRegion.FocusAsync();
            Assert.True(await tierRegion.EvaluateAsync<bool>("element => document.activeElement === element"));
            await tiers.Last.ScrollIntoViewIfNeededAsync();
            Assert.True(await tiers.Last.EvaluateAsync<bool>(
                "element => { const r = element.getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth; }"));
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
            var uploadedPartId = Guid.Parse((await page.Locator("[data-workflow-part]")
                .GetAttributeAsync("data-part-id"))!);
            await upload.AssertAdmittedMeshMatchesBrowserUploadAsync(uploadedPartId);
            var protectedSession = await factory.Services.GetRequiredService<IInstantQuotationSessionStore>()
                .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
            Assert.NotNull(protectedSession);
            Assert.NotNull(protectedSession.QuoteAuthorization);
            var receipt = Assert.Single(protectedSession.PhysicalReceipts!,
                candidate => candidate.MaterialKey == protectedSession.Parts.Single().Configuration.MaterialKey);
            Assert.Equal(uploadedPartId, receipt.PartId);
            Assert.Equal(protectedSession.SessionId, receipt.SessionId);
            Assert.Equal(protectedSession.Parts.Single().Geometry.Sha256, receipt.UploadSha256);
            Assert.True(receipt.MotionSeconds > 0);
            Assert.Equal(0, await page.Locator("[data-workflow-price-unavailable]").CountAsync());
            var petgComparison = await page.Locator("[data-workflow-material-price='PETG'] strong").InnerTextAsync();
            Assert.DoesNotContain("฿0", petgComparison, StringComparison.Ordinal);
            var pricingCallsBeforePhysicalCheck = pricing.CallCount;

            await page.Locator("[data-workflow-physical-analysis] button").ClickAsync();
            await page.Locator("[data-physical-analysis-result]").WaitForAsync();
            Assert.Equal("ready:None", await page.Locator("[data-physical-analysis-result]")
                .GetAttributeAsync("data-physical-analysis-result") + ":" +
                await page.Locator("[data-physical-analysis-result]")
                    .GetAttributeAsync("data-physical-analysis-failure"));
            Assert.Equal(pricingCallsBeforePhysicalCheck, pricing.CallCount);

            await page.Locator("[data-workflow-material-picker] select[name='material']").SelectOptionAsync("ABS");
            await page.Locator("[data-physical-analysis-result='ready']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
            Assert.Equal(0, await page.Locator("[data-physical-analysis-result='ready']").CountAsync());
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await review.WaitForAsync();
            try
            {
                await page.WaitForFunctionAsync(
                    "() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')",
                    null,
                    new PageWaitForFunctionOptions { Timeout = 10000 });
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                      workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                      review: document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button')?.outerHTML,
                      status: document.querySelector('#instant-quote-status')?.textContent,
                      alert: document.querySelector('[role=alert]')?.textContent,
                      pricingStatus: document.querySelector('[data-pricing-loading-status]')?.textContent,
                      parts: document.querySelectorAll('[data-workflow-part]').length,
                      tiers: document.querySelectorAll('[data-workflow-price-tier]').length
                    })
                    """);
                throw new InvalidOperationException($"Uploaded part did not enable review: {state}", error);
            }
            if (culture == "th")
            {
                await review.TapAsync();
            }
            else
            {
                await review.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            }
            var part = page.Locator("[data-workflow-review-part]");
            await part.WaitForAsync();
            Assert.Equal(1, await part.CountAsync());
            Assert.Contains(Path.GetFileName(path), await part.InnerTextAsync(), StringComparison.Ordinal);
            var disclosure = part.Locator("[data-review-part-details] > summary");
            if (culture == "th")
            {
                await disclosure.TapAsync();
            }
            else
            {
                await disclosure.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            }
            Assert.True(await part.Locator("[data-review-part-details]").EvaluateAsync<bool>("element => element.open"));
            var continueButton = page.Locator("[data-review-continue]");
            await continueButton.ScrollIntoViewIfNeededAsync();
            Assert.True(await continueButton.EvaluateAsync<bool>(
                "element => { const r = element.getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth; }"));
            Assert.True(await continueButton.EvaluateAsync<bool>("element => element.getBoundingClientRect().height >= 44"));
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Empty(pageErrors);
            var skipLink = page.Locator(".maliev-skip-link").First;
            await skipLink.FocusAsync();
            Assert.True(await skipLink.EvaluateAsync<bool>("element => getComputedStyle(element).clipPath !== 'inset(50%)'"));
            await page.EvaluateAsync("() => { document.body.tabIndex = -1; document.body.focus(); }");
            Assert.True(await skipLink.EvaluateAsync<bool>("element => getComputedStyle(element).clipPath === 'inset(50%)'"));
            if (Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR") is { Length: > 0 } evidenceDirectory)
            {
                Directory.CreateDirectory(evidenceDirectory);
                await page.Locator("[data-workflow-viewer]").ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Path = Path.Combine(evidenceDirectory, $"source-576-real-upload-viewer-{culture}-{width}.png"),
                });
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(evidenceDirectory, $"source-576-real-upload-{culture}-{width}.png"),
                    FullPage = true,
                });
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SelectingUploadedPartsShowsOnlyTheirOwnPrintTime()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var small = CreateBoxStl(20, 20, 5);
        var large = CreateBoxStl(30, 30, 10);
        try
        {
            await page.SetInputFilesAsync("#instant-quote-files", [small, large]);
            var parts = page.Locator("[data-workflow-part]");
            await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 2");
            Assert.Equal(2, await parts.CountAsync());
            var selectedPrintTime = page.Locator("[data-workflow-selected-print-time]");

            var firstId = await parts.Nth(0).GetAttributeAsync("data-part-id");
            await parts.Nth(0).Locator("button[aria-label^='View']").ClickAsync();
            try
            {
                await page.WaitForFunctionAsync(
                    "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                    firstId);
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                      workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                      priceUnavailable: document.querySelectorAll('[data-workflow-price-unavailable]').length,
                      printTime: document.querySelector('[data-workflow-selected-print-time]')?.outerHTML,
                      status: document.querySelector('#instant-quote-status')?.textContent,
                      errors: [...document.querySelectorAll('[role=alert]')].map(x => x.textContent)
                    })
                    """);
                using var scope = factory.Services.CreateScope();
                var saved = await scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>()
                    .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                var analyzer = scope.ServiceProvider.GetRequiredService<InstantQuotationBoundPhysicalAnalysisService>();
                var profileVersion = scope.ServiceProvider.GetRequiredService<Legacy.Maliev.Web.Application.Pricing.FdmRuntimeProfileCatalog>().ProfileVersion;
                var failures = new List<string>();
                foreach (var savedPart in saved?.Parts ?? [])
                {
                    if (savedPart.PhysicalAnalysisUpload is not { } admitted)
                    {
                        failures.Add("upload-missing");
                        continue;
                    }

                    var binding = new InstantQuotationPhysicalAnalysisBinding(
                        saved!.SessionId, upload.OwnerIdentity, savedPart.PartId, admitted.FileId,
                        admitted.Sha256, savedPart.Configuration.MaterialKey,
                        savedPart.Configuration.BuildPreference, savedPart.Configuration.Quantity, profileVersion);
                    var analyzed = await analyzer.AnalyzeAsync(binding, default);
                    failures.Add(analyzed.Failure.ToString());
                }
                throw new InvalidOperationException(
                    $"Two-part physical quote did not become ready: {state}; physical={string.Join(',', failures)}", error);
            }
            var firstDuration = await selectedPrintTime.Locator("dd").InnerTextAsync();

            var secondId = await parts.Nth(1).GetAttributeAsync("data-part-id");
            await parts.Nth(1).Locator("button[aria-label^='View']").ClickAsync();
            await page.WaitForFunctionAsync(
                "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                secondId);
            var secondDuration = await selectedPrintTime.Locator("dd").InnerTextAsync();

            Assert.NotEqual(firstDuration, secondDuration);
            Assert.Equal(1, await selectedPrintTime.CountAsync());
            Assert.Equal(2, upload.VerifiedUploads);
            Assert.Empty(pageErrors);
        }
        finally
        {
            File.Delete(small);
            File.Delete(large);
        }
    }

    [Fact]
    public async Task NineGeneratedStlUploadsKeepDistinctReviewAndPreliminaryPreviews()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
        });
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        (float Width, float Depth, float Height)[] dimensions =
        [
            (12, 18, 7), (19, 13, 11), (24, 23, 8),
            (15, 31, 13), (34, 17, 10), (21, 27, 19),
            (39, 25, 14), (18, 41, 20), (43, 16, 27),
        ];
        var paths = dimensions.Select((size, index) =>
            CreateBoxStl(size.Width, size.Depth, size.Height, $"maliev-preview-part-{index + 1:00}")).ToArray();
        try
        {
            Assert.Equal(9, paths.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                .Distinct(StringComparer.Ordinal).Count());
            await page.SetInputFilesAsync("#instant-quote-files", paths);
            await page.WaitForFunctionAsync(
                "() => document.querySelectorAll('[data-workflow-part]').length === 9",
                null,
                new PageWaitForFunctionOptions { Timeout = 90000 });
            Assert.Equal(9, upload.VerifiedUploads);
            var parts = page.Locator("[data-workflow-part]");
            for (var index = 0; index < 9; index++)
            {
                var partId = await parts.Nth(index).GetAttributeAsync("data-part-id");
                await parts.Nth(index).Locator("button[aria-label^='View']").ClickAsync();
                await page.WaitForFunctionAsync(
                    "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                    partId);
                await page.Locator("[data-workflow-material-picker] select[name='material']").SelectOptionAsync("ABS");
                await page.WaitForFunctionAsync(
                    "() => !document.querySelector('[data-pricing-loading-status]')");
            }
            try
            {
                await page.WaitForFunctionAsync(
                    """
                    () => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')
                    """,
                    null,
                    new PageWaitForFunctionOptions { Timeout = 90000 });
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                        workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                        parts: document.querySelectorAll('[data-workflow-part]').length,
                        pendingUploads: document.querySelectorAll('[data-workflow-upload-item]').length,
                        reviewEnabled: !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)'),
                        status: document.querySelector('#instant-quote-status')?.textContent,
                        alert: document.querySelector('[role=alert]')?.textContent
                    })
                    """);
                throw new InvalidOperationException(
                    $"Nine-part upload did not reach review. State: {state}; verified uploads: {upload.VerifiedUploads}; page errors: {string.Join(" | ", pageErrors)}.",
                    error);
            }
            Assert.Equal(9, upload.VerifiedUploads);

            await page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").ClickAsync();
            await page.WaitForFunctionAsync(
                """
                () => {
                    const images = [...document.querySelectorAll('[data-workflow-review-part] [data-review-thumbnail]')];
                    return images.length === 9 && images.every(image => image.getAttribute('src')?.startsWith('data:image/png'));
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 90000 });
            var reviewNames = await page.Locator("[data-workflow-review-part] h4").AllInnerTextsAsync();
            Assert.Equal(paths.Select(Path.GetFileName).Order(StringComparer.Ordinal),
                reviewNames.Order(StringComparer.Ordinal));
            var reviewPreviews = await page.Locator("[data-workflow-review-part] [data-review-thumbnail]")
                .EvaluateAllAsync<string[]>("images => images.map(image => image.getAttribute('src'))");
            Assert.Equal(9, reviewPreviews.Distinct(StringComparer.Ordinal).Count());

            var preliminaryButton = page.Locator("#preliminary-quotation-button");
            Assert.True(await preliminaryButton.IsEnabledAsync());
            await using var preview = await page.RunAndWaitForPopupAsync(() => preliminaryButton.ClickAsync());
            await preview.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var logo = preview.Locator(".iq-preliminary-quotation-logo");
            await preview.WaitForFunctionAsync("() => document.querySelector('.iq-preliminary-quotation-logo')?.complete === true");
            Assert.True(await logo.EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0"));
            Assert.StartsWith("data:image/webp;base64,", await logo.GetAttributeAsync("src"), StringComparison.Ordinal);
            var preliminaryNames = await preview.Locator(".iq-preliminary-quotation-part h2").AllInnerTextsAsync();
            Assert.Equal(reviewNames, preliminaryNames);
            var preliminaryPreviews = await preview.Locator(".iq-preliminary-quotation-thumbnail")
                .EvaluateAllAsync<string[]>("images => images.map(image => image.getAttribute('src'))");
            Assert.Equal(9, preliminaryPreviews.Length);
            Assert.All(preliminaryPreviews, source => Assert.StartsWith("data:image/png", source, StringComparison.Ordinal));
            Assert.Equal(9, preliminaryPreviews.Distinct(StringComparer.Ordinal).Count());
            Assert.Empty(pageErrors);
        }
        finally
        {
            foreach (var path in paths) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("en", "Thin walls may print with defects", "Instant 3D Printing Estimate", "0.80 mm", "0.60 mm")]
    [InlineData("th", "ผนังบางอาจเกิดข้อบกพร่อง", "ประเมินราคาพิมพ์ 3 มิติทันที", "0.80 มม.", "0.60 มม.")]
    public async Task ThinFdmUploadWarnsAndHeatmapDoesNotRepriceOrBlockReview(
        string culture, string warningTitle, string pageTitle, string fdmLimit, string resinLimit)
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        var html = await readiness.Content.ReadAsStringAsync();
        Assert.Contains("data-migration-component=\"instant-quotation-three-dimensional-printing\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"instant-quote-files\"", html, StringComparison.Ordinal);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        });
        await using var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);
        var consoleErrors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning") consoleErrors.Add(message.Text);
        };

        var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.Equal(200, response?.Status);
        Assert.Contains(pageTitle, await page.TitleAsync(), StringComparison.Ordinal);
        Assert.Contains("/instantquotation/3d-printing", page.Url, StringComparison.OrdinalIgnoreCase);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateBoxStl(20, 20, 0.7f);
        try
        {
            await page.SetInputFilesAsync("#instant-quote-files", path);
            try
            {
                await page.Locator("[data-workflow-part]").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            }
            catch (TimeoutException error)
            {
                var state = await page.EvaluateAsync<string>("""
                    () => JSON.stringify({
                      workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                      status: document.querySelector('#instant-quote-status')?.textContent,
                      preview: document.querySelector('[data-workflow-preview-status]')?.textContent,
                      upload: document.querySelector('[data-workflow-upload-item]')?.textContent,
                      alert: document.querySelector('[role=alert]')?.textContent,
                      blazor: typeof window.Blazor,
                      selectedFiles: document.querySelector('#instant-quote-files')?.files?.length
                    })
                    """);
                throw new InvalidOperationException(
                    $"Real upload did not create a part. State: {state}. Page errors: {string.Join(" | ", pageErrors)}. Console: {string.Join(" | ", consoleErrors)}. Verified uploads: {upload.VerifiedUploads}.",
                    error);
            }
            await page.Locator("[data-dfm-code='thin-wall']").WaitForAsync(new LocatorWaitForOptions { Timeout = 90000 });
            var warning = await page.Locator("[data-dfm-code='thin-wall']").InnerTextAsync();
            Assert.Contains(warningTitle, warning, StringComparison.Ordinal);
            Assert.Contains(fdmLimit, warning, StringComparison.Ordinal);

            var material = page.Locator("[data-workflow-material-picker] select[name='material']");
            await material.SelectOptionAsync("ABS");
            var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
            await page.WaitForFunctionAsync("() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')");

            var toggle = page.Locator(".instant-quote__thickness-toggle");
            await page.WaitForFunctionAsync("() => !!document.querySelector('.instant-quote__thickness-toggle:not(:disabled)')");
            var total = page.Locator("[data-workflow-summary-dock] > summary > strong[aria-live]");
            var before = await total.InnerTextAsync();
            Assert.NotEqual("—", before);
            var pricingCalls = pricing.CallCount;

            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'true'");
            Assert.Equal("true", await toggle.GetAttributeAsync("aria-pressed"));
            Assert.True(await page.Locator(".instant-quote__thickness-legend").IsVisibleAsync());
            Assert.Equal(before, await total.InnerTextAsync());
            Assert.Equal(pricingCalls, pricing.CallCount);
            Assert.True(await review.IsEnabledAsync());

            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'false'");
            Assert.Equal("false", await toggle.GetAttributeAsync("aria-pressed"));
            Assert.False(await page.Locator(".instant-quote__thickness-legend").IsVisibleAsync());
            Assert.Equal(before, await total.InnerTextAsync());
            Assert.Equal(pricingCalls, pricing.CallCount);
            Assert.True(await review.IsEnabledAsync());
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Empty(pageErrors);

            await material.SelectOptionAsync("M68");
            await page.WaitForFunctionAsync("() => !document.querySelector('[data-dfm-code=thin-wall]')");
            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("limit => document.querySelector('.instant-quote__thickness-legend')?.textContent?.includes(limit)", resinLimit);
            Assert.DoesNotContain("needs-attention", await toggle.GetAttributeAsync("class"), StringComparison.Ordinal);
            Assert.Equal(1, upload.VerifiedUploads);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OneMillimeterFdmUploadDoesNotInventAThinWallWarning()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.Equal(200, response?.Status);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateBoxStl(20, 20, 1f, "maliev-one-millimeter-fdm");
        try
        {
            await page.SetInputFilesAsync("#instant-quote-files", path);
            await page.Locator("[data-workflow-part]").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await page.WaitForFunctionAsync("() => !!document.querySelector('.instant-quote__thickness-toggle:not(:disabled)')");
            await page.Locator("[data-workflow-material-picker] select[name='material']").SelectOptionAsync("ABS");
            await page.Locator("[data-workflow-dfm-part]").WaitForAsync();

            Assert.Equal(0, await page.Locator("[data-dfm-code='thin-wall']").CountAsync());
            var toggle = page.Locator(".instant-quote__thickness-toggle");
            Assert.DoesNotContain("needs-attention", await toggle.GetAttributeAsync("class"), StringComparison.Ordinal);
            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__thickness-toggle')?.getAttribute('aria-pressed') === 'true'");
            Assert.True(await page.Locator(".instant-quote__thickness-legend").IsVisibleAsync());
            Assert.Contains("0.80 mm", await page.Locator(".instant-quote__thickness-legend").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Equal(1, upload.VerifiedUploads);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenSheetFailsClosedInsteadOfInventingSafeThickness()
    {
        var upload = new HashCheckingUploadClient();
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var quoteUrl = new Uri(origin, "/instantquotation/3d-printing?culture=en").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = origin,
        });
        using var readiness = await client.GetAsync(quoteUrl);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.Equal(200, response?.Status);
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();

        var path = CreateOpenSheetStl();
        try
        {
            await page.SetInputFilesAsync("#instant-quote-files", path);
            await page.WaitForFunctionAsync("() => document.querySelector('.instant-quote__workflow')?.dataset.workflowState === 'error'");
            Assert.Contains("The file could not be uploaded. Try again.",
                await page.Locator("[role='alert']").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Equal(0, await page.Locator("[data-workflow-part]").CountAsync());
            Assert.Equal(0, await page.Locator("[data-workflow-dfm-clear]").CountAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RealUploadTestingWebApplicationFactory(
        string contentRoot,
        HashCheckingUploadClient upload,
        CountingPricingService pricing) : TestingWebApplicationFactory(contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationUploadClient>();
                services.AddSingleton<IInstantQuotationUploadClient>(upload);
                services.RemoveAll<IInstantQuotationPricingService>();
                services.AddSingleton<IInstantQuotationPricingService>(pricing);
                services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                services.AddScoped<IInstantQuotationPhysicalAnalysisInputReader>(provider =>
                    new BrowserMultiUploadInputReader(upload,
                        provider.GetRequiredService<IInstantQuotationSessionStore>()));
            });
        }
    }

    private static string CreateBoxStl(float width, float depth, float height, string fileStem = "maliev-thin-fdm")
    {
        float[][] points =
        [
            [0, 0, 0], [width, 0, 0], [width, depth, 0], [0, depth, 0],
            [0, 0, height], [width, 0, height], [width, depth, height], [0, depth, height],
        ];
        int[][] faces =
        [
            [0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7],
            [0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5],
            [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7],
        ];
        var path = Path.Combine(Path.GetTempPath(), $"{fileStem}-{Guid.NewGuid():N}.stl");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(new byte[80]);
        writer.Write((uint)faces.Length);
        foreach (var face in faces)
        {
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            foreach (var value in points[face[0]]) writer.Write(value);
            foreach (var value in points[face[1]]) writer.Write(value);
            foreach (var value in points[face[2]]) writer.Write(value);
            writer.Write((ushort)0);
        }

        return path;
    }

    private static string CreateOpenSheetStl()
    {
        var path = Path.Combine(Path.GetTempPath(), $"maliev-open-sheet-{Guid.NewGuid():N}.stl");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(new byte[80]);
        writer.Write((uint)2);
        var points = new float[][]
        {
            [0, 0, 0], [20, 0, 0], [20, 20, 0], [0, 20, 0],
        };
        foreach (var face in new[] { new[] { 0, 2, 1 }, new[] { 0, 3, 2 } })
        {
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            foreach (var value in points[face[0]]) writer.Write(value);
            foreach (var value in points[face[1]]) writer.Write(value);
            foreach (var value in points[face[2]]) writer.Write(value);
            writer.Write((ushort)0);
        }

        return path;
    }

    private sealed class CountingPricingService : IInstantQuotationPricingService
    {
        private readonly InstantQuotationPricingService inner = new();
        private int calls;

        public int CallCount => Volatile.Read(ref calls);

        public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state)
        {
            Interlocked.Increment(ref calls);
            return inner.Quote(state);
        }
    }

    private sealed class HashCheckingUploadClient : IInstantQuotationUploadClient
    {
        private int verifiedUploads;
        private BrowserUpload? verifiedUpload;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, BrowserUpload> uploads = new();

        public int VerifiedUploads => Volatile.Read(ref verifiedUploads);

        public string SessionId => verifiedUpload!.SessionId;

        public string? OwnerIdentity => verifiedUpload!.OwnerIdentity;

        public async Task AssertAdmittedMeshMatchesBrowserUploadAsync(Guid partId)
        {
            BrowserUpload upload = Assert.IsType<BrowserUpload>(verifiedUpload);
            var reader = new BrowserUploadInputReader(upload, partId);
            var service = new InstantQuotationAdmittedMeshService(reader);
            var mesh = await service.ReadAsync(
                upload.SessionId, upload.OwnerIdentity, partId, upload.FileId, upload.Sha256, default);
            Assert.True(mesh.IsReady);
            Assert.Equal(upload.Sha256, mesh.UploadSha256);
            Assert.Equal(12, mesh.Mesh!.Triangles.Count);
            Assert.Equal(1, reader.ReadCount);
        }

        public async Task<InstantQuotationUploadResult> UploadAsync(
            string sessionId,
            string? ownerIdentity,
            Stream content,
            string fileName,
            string contentType,
            long contentLength,
            InstantQuotationGeometryClaim geometryClaim,
            string operationId,
            CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var sha256 = Convert.ToHexString(SHA256.HashData(copy.GetBuffer().AsSpan(0, (int)copy.Length)))
                .ToLowerInvariant();
            if (copy.Length != contentLength || !string.Equals(sha256, geometryClaim.Sha256, StringComparison.Ordinal))
            {
                return InstantQuotationUploadResult.Failed(
                    operationId,
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationProblemCategory.Validation);
            }

            var fileId = Guid.NewGuid();
            verifiedUpload = new BrowserUpload(sessionId, ownerIdentity, fileId, fileName,
                copy.ToArray(), sha256);
            uploads[fileId] = verifiedUpload;
            Interlocked.Increment(ref verifiedUploads);
            return InstantQuotationUploadResult.Succeeded(
                operationId,
                new InstantQuotationUploadReference(fileId.ToString("D")),
                sha256,
                new InstantQuotationPhysicalAnalysisUpload(
                    fileId, fileName,
                    string.IsNullOrWhiteSpace(contentType) ? "model/stl" : contentType,
                    copy.Length, sha256, "clean"));
        }

        public InstantQuotationPhysicalAnalysisInputResult Read(
            string sessionId, string? ownerIdentity, Guid fileId)
        {
            uploads.TryGetValue(fileId, out BrowserUpload? upload);
            return upload is not null
                && upload.SessionId == sessionId && upload.OwnerIdentity == ownerIdentity
                ? new InstantQuotationPhysicalAnalysisInputResult(
                    upload.Bytes, InstantQuotationPhysicalAnalysisInputFailure.None,
                    upload.FileId, upload.FileName, upload.Sha256)
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable);
        }

        public Task<InstantQuotationRemoveResult> RemoveAsync(
            string sessionId,
            string? ownerIdentity,
            InstantQuotationUploadReference uploadReference,
            string operationId,
            CancellationToken cancellationToken) => Task.FromResult(InstantQuotationRemoveResult.Unavailable(operationId));

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(
            string sessionId,
            string? ownerIdentity,
            int quotationRequestId,
            IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId,
            CancellationToken cancellationToken) => Task.FromResult(InstantQuotationFinalizationResult.Unavailable(operationId));
    }

    private sealed record BrowserUpload(
        string SessionId, string? OwnerIdentity, Guid FileId, string FileName, byte[] Bytes, string Sha256);

    private sealed class BrowserMultiUploadInputReader(
        HashCheckingUploadClient upload,
        IInstantQuotationSessionStore sessionStore) : IInstantQuotationPhysicalAnalysisInputReader
    {
        public async Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            var session = await sessionStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            var fileId = session?.Parts.SingleOrDefault(part => part.PartId == partId)?
                .PhysicalAnalysisUpload?.FileId;
            return fileId is { } id
                ? upload.Read(sessionId, ownerIdentity, id)
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable);
        }
    }

    private sealed class BrowserUploadInputReader(BrowserUpload upload, Guid partId)
        : IInstantQuotationPhysicalAnalysisInputReader
    {
        public int ReadCount { get; private set; }

        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid requestedPartId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(sessionId == upload.SessionId
                && ownerIdentity == upload.OwnerIdentity
                && requestedPartId == partId
                ? new InstantQuotationPhysicalAnalysisInputResult(
                    upload.Bytes, InstantQuotationPhysicalAnalysisInputFailure.None,
                    upload.FileId, upload.FileName, upload.Sha256)
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable));
        }
    }
}
