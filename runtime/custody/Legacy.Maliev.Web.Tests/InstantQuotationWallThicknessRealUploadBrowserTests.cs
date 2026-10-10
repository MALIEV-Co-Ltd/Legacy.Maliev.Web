using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationWallThicknessRealUploadBrowserTests(ITestOutputHelper output)
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
        var observation = new MaterialCompletionObservation();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing, observation);
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
                try
                {
                    await page.WaitForFunctionAsync(
                        "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                        partId);
                }
                catch (TimeoutException original)
                {
                    await SelectedPrintTimeTimeoutDiagnostics.AttachAsync(original, index + 1, partId,
                        () => page.EvaluateAsync<string>("""
                            () => {
                                const duration = document.querySelector('[data-workflow-selected-print-time]');
                                const configuration = document.querySelector('[data-workflow-material-picker]');
                                const quantity = configuration?.querySelector('input[name="quantity"]');
                                return JSON.stringify({
                                    actualPartId: duration?.getAttribute('data-part-id'),
                                    configurationPartId: quantity?.id?.replace(/^quantity-/, ''),
                                    workflow: document.querySelector('.instant-quote__workflow')?.dataset.workflowState,
                                    isRepricing: !!document.querySelector('[data-pricing-loading-status]'),
                                    material: configuration?.querySelector('select[name="material"]')?.value,
                                    quantity: quantity?.value,
                                    durationPresent: !!duration,
                                    unavailablePresent: !!configuration?.querySelector('[data-workflow-price-unavailable]')
                                });
                            }
                            """), description => output.WriteLine(description));
                    throw;
                }
                using var completionScope = factory.Services.CreateScope();
                var completionStore = completionScope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
                var beforeMaterialChange = await completionStore.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                Assert.NotNull(beforeMaterialChange);
                observation.BeginEdit();
                await page.Locator("[data-workflow-material-picker] select[name='material']").SelectOptionAsync("ABS");
                // Completion includes optional comparison callbacks under the pricing owner's
                // existing 33-second absolute budget, not merely its 30-second selected phase.
                using (var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(33)))
                {
                    try
                    {
                        while (!SelectedPrintTimeTimeoutDiagnostics.IsMaterialChangeComplete(
                            await completionStore.GetAsync(upload.SessionId, upload.OwnerIdentity, completionDeadline.Token)
                                .WaitAsync(completionDeadline.Token), beforeMaterialChange.UpdatedAt, Guid.Parse(partId!)))
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100), completionDeadline.Token);
                        }
                    }
                    catch (OperationCanceledException) when (completionDeadline.IsCancellationRequested)
                    {
                        // Bounded booleans/count only; never replace the primary timeout with a diagnostic failure.
                        try
                        {
                            output.WriteLine($"[material-services] iteration={index + 1}; {observation.Describe()}");
                            var observed = await completionStore.GetAsync(upload.SessionId, upload.OwnerIdentity, default)
                                .WaitAsync(TimeSpan.FromSeconds(1));
                            var observedPart = observed?.Parts.FirstOrDefault(candidate => candidate.PartId == Guid.Parse(partId!));
                            output.WriteLine($"[material-completion] iteration={index + 1}; revisionChanged={observed is not null && observed.UpdatedAt != beforeMaterialChange.UpdatedAt}; authorizationPresent={observed?.QuoteAuthorization is not null}; partPresent={observedPart is not null}; materialMatched={observedPart?.Configuration.MaterialKey == "ABS"}; quantityMatched={observedPart?.Configuration.Quantity == 1}; partCount={Math.Min(observed?.Parts.Count ?? 0, 9)}");
                        }
                        catch { /* Secondary observation must not mask the material-completion timeout. */ }
                        throw new TimeoutException("Protected material change completion was not observed.");
                    }
                }
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
            Assert.True(await preview.Locator(".iq-preliminary-quotation-thumbnail").EvaluateAllAsync<bool>("""
                images => images.length === 9 && images.every(image => {
                    const bounds = image.getBoundingClientRect();
                    return bounds.width >= 150 && bounds.height >= 150 && getComputedStyle(image).objectFit === 'contain';
                })
                """));
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

            await review.ClickAsync();
            await page.Locator("[data-workflow-review-content]").WaitForAsync();
            var reviewTotal = page.Locator("[data-workflow-review-total] > div")
                .Filter(new LocatorFilterOptions
                {
                    Has = page.GetByText(culture == "th" ? "รวมทั้งหมด" : "Total", new PageGetByTextOptions { Exact = true }),
                }).Locator("dd");
            Assert.Equal(1, await reviewTotal.CountAsync());
            Assert.Equal(before, await reviewTotal.InnerTextAsync());
            await page.Locator("[data-review-part-details] > summary").ClickAsync();
            var reviewedWarnings = await page.Locator("[data-review-dfm-warnings] li").AllInnerTextsAsync();
            Assert.Contains(reviewedWarnings, item => item.Contains(warningTitle, StringComparison.Ordinal)
                && item.Contains(fdmLimit, StringComparison.Ordinal));
            await AssertPrintableDfmMatchesReviewAsync(page, reviewedWarnings);
            Assert.Equal(before, await reviewTotal.InnerTextAsync());
            Assert.Equal(pricingCalls, pricing.CallCount);
            await page.Locator("[data-review-back]").ClickAsync();

            await material.SelectOptionAsync("M68");
            try
            {
                await page.WaitForFunctionAsync("() => !document.querySelector('[data-dfm-code=thin-wall]')");
            }
            catch (TimeoutException)
            {
                // One shared secondary deadline; diagnostics must not replace the original timeout.
                using var diagnosticDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    using var resinScope = factory.Services.CreateScope();
                    var resinStore = resinScope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
                    var observed = await resinStore.GetAsync(upload.SessionId, upload.OwnerIdentity, diagnosticDeadline.Token)
                        .WaitAsync(diagnosticDeadline.Token);
                    var observedPart = observed is not null && observed.Parts.Count == 1 ? observed.Parts[0] : null;
                    output.WriteLine($"[dfm-resin-authority] authorizationPresent={observed?.QuoteAuthorization is not null}; singlePartPresent={observedPart is not null}; materialMatched={observedPart?.Configuration.MaterialKey == "M68"}; uploadCount={Math.Min(upload.VerifiedUploads, 2)}; pageErrorCount={Math.Min(pageErrors.Count, 10)}");
                    var rendered = await page.EvaluateAsync<string>("""
                        limits => JSON.stringify({
                            materialMatched: document.querySelector('[data-workflow-configuration] [data-workflow-material-picker] select[name="material"]')?.value === 'M68',
                            reviewEnabled: !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)'),
                            pricingLoading: !!document.querySelector('[data-pricing-loading-status]'),
                            thinWallPresent: !!document.querySelector('[data-dfm-code=thin-wall]'),
                            thicknessAvailable: !!document.querySelector('.instant-quote__thickness-toggle:not(:disabled)'),
                            warningUsesResinLimit: !!document.querySelector('[data-dfm-code=thin-wall]')?.textContent?.includes(limits.resinLimit),
                            warningUsesFdmLimit: !!document.querySelector('[data-dfm-code=thin-wall]')?.textContent?.includes(limits.fdmLimit)
                        })
                        """, new { resinLimit, fdmLimit }).WaitAsync(diagnosticDeadline.Token);
                    output.WriteLine($"[dfm-resin-render] {rendered}");
                }
                catch
                {
                    // Secondary observation or output failure must not mask the primary timeout.
                }
                throw;
            }
            await toggle.ClickAsync();
            await page.WaitForFunctionAsync("limit => document.querySelector('.instant-quote__thickness-legend')?.textContent?.includes(limit)", resinLimit);
            Assert.DoesNotContain("needs-attention", await toggle.GetAttributeAsync("class"), StringComparison.Ordinal);
            Assert.Equal(1, upload.VerifiedUploads);

            await page.WaitForFunctionAsync("() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')");
            var resinPrice = await total.InnerTextAsync();
            await review.ClickAsync();
            await page.Locator("[data-workflow-review-content]").WaitForAsync();
            var resinWarnings = await page.Locator("[data-review-dfm-warnings] li").AllInnerTextsAsync();
            Assert.DoesNotContain(resinWarnings, item => item.Contains(warningTitle, StringComparison.Ordinal));
            Assert.Equal(resinPrice, await reviewTotal.InnerTextAsync());
            var resinPricingCalls = pricing.CallCount;
            await AssertPrintableDfmMatchesReviewAsync(page, resinWarnings);
            Assert.Equal(resinPrice, await reviewTotal.InnerTextAsync());
            Assert.Equal(resinPricingCalls, pricing.CallCount);
            Assert.Equal(1, upload.VerifiedUploads);
            Assert.Empty(pageErrors);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task AssertPrintableDfmMatchesReviewAsync(IPage page, IReadOnlyList<string> reviewedWarnings)
    {
        var preliminary = page.Locator("#preliminary-quotation-button");
        Assert.True(await preliminary.IsEnabledAsync());
        await using var preview = await page.RunAndWaitForPopupAsync(() => preliminary.ClickAsync());
        await preview.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        Assert.Equal(1, await preview.Locator(".iq-preliminary-quotation-part").CountAsync());
        var printableWarnings = await preview.Locator(".iq-preliminary-quotation-part .iq-preliminary-quotation-warnings li").AllInnerTextsAsync();
        Assert.Equal(reviewedWarnings, printableWarnings);
        Assert.Equal(reviewedWarnings.Count == 0 ? 1 : 0,
            await preview.Locator(".iq-preliminary-quotation-part .iq-preliminary-quotation-dfm-ok").CountAsync());
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

    [Theory]
    [InlineData(2, 1)]
    [InlineData(10, 2)]
    public async Task ActualUploadedParts_PreliminaryPopupExportsA4Pdf(int partCount, int minimumPages)
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
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            Locale = "en-US",
        });
        await using var page = await context.NewPageAsync();
        var pageErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        page.PageError += (_, error) => pageErrors.Enqueue(error);
        var paths = new List<string>();
        try
        {
            var response = await page.GotoAsync(quoteUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
            for (var index = 0; index < partCount; index++)
            {
                paths.Add(CreatePreliminaryPdfBoxStl(index + 1));
            }
            await page.SetInputFilesAsync("#instant-quote-files", paths);
            await page.WaitForFunctionAsync(
                "count => document.querySelectorAll('[data-workflow-part]').length === count",
                partCount, new PageWaitForFunctionOptions { Timeout = 90000 });
            Assert.Equal(partCount, upload.VerifiedUploads);
            var parts = page.Locator("[data-workflow-part]");
            var partIds = new HashSet<Guid>();
            for (var index = 0; index < partCount; index++)
            {
                var partIdText = await parts.Nth(index).GetAttributeAsync("data-part-id");
                Assert.True(Guid.TryParse(partIdText, out var partId));
                Assert.True(partIds.Add(partId));
                await parts.Nth(index).Locator("button[aria-label^='View']").ClickAsync();
                await page.WaitForFunctionAsync(
                    "id => document.querySelector('[data-workflow-selected-print-time]')?.getAttribute('data-part-id') === id",
                    partIdText);
                using var scope = factory.Services.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
                var beforeMaterialChange = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                Assert.NotNull(beforeMaterialChange);
                Assert.Equal(partCount, beforeMaterialChange.Parts.Count);
                var storedPart = Assert.Single(beforeMaterialChange.Parts, candidate => candidate.PartId == partId);
                var physical = Assert.IsType<InstantQuotationPhysicalAnalysisUpload>(storedPart.PhysicalAnalysisUpload);
                var expectedPath = Assert.Single(paths, path => Path.GetFileName(path) == storedPart.DisplayFileName);
                var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedPath))).ToLowerInvariant();
                Assert.Equal(expectedHash, physical.Sha256);
                var mesh = await new InstantQuotationAdmittedMeshService(new BrowserMultiUploadInputReader(upload, store))
                    .ReadAsync(upload.SessionId, upload.OwnerIdentity, partId, physical.FileId, physical.Sha256, default);
                Assert.True(mesh.IsReady);
                Assert.Equal(expectedHash, mesh.UploadSha256);
                Assert.Equal(12, mesh.Mesh!.Triangles.Count);
                await page.Locator("[data-workflow-material-picker] select[name='material']").SelectOptionAsync("ABS");
                using (var completionDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    try
                    {
                        while (!SelectedPrintTimeTimeoutDiagnostics.IsMaterialChangeComplete(
                            await store.GetAsync(upload.SessionId, upload.OwnerIdentity, completionDeadline.Token)
                                .WaitAsync(completionDeadline.Token), beforeMaterialChange.UpdatedAt, partId))
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100), completionDeadline.Token);
                        }
                    }
                    catch (OperationCanceledException) when (completionDeadline.IsCancellationRequested)
                    {
                        throw new TimeoutException("Protected material change completion was not observed.");
                    }
                }
                await page.WaitForFunctionAsync("() => !document.querySelector('[data-pricing-loading-status]')");
            }
            using (var scope = factory.Services.CreateScope())
            {
                var state = await scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>()
                    .GetAsync(upload.SessionId, upload.OwnerIdentity, default);
                Assert.NotNull(state);
                Assert.Equal(partCount, state.Parts.Select(part => part.PhysicalAnalysisUpload!.FileId).Distinct().Count());
                Assert.NotNull(state.QuoteAuthorization);
            }
            await page.WaitForFunctionAsync(
                "() => !!document.querySelector('[data-workflow-configuration] .instant-quote__configuration-actions button:not(:disabled)')",
                null, new PageWaitForFunctionOptions { Timeout = 90000 });
            await page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button").ClickAsync();
            await page.WaitForFunctionAsync(
                """
                count => {
                    const images = [...document.querySelectorAll('[data-workflow-review-part] [data-review-thumbnail]')];
                    return images.length === count && images.every(image => image.getAttribute('src')?.startsWith('data:image/png'));
                }
                """, partCount, new PageWaitForFunctionOptions { Timeout = 90000 });
            var reviewNames = await page.Locator("[data-workflow-review-part] h4").AllInnerTextsAsync();
            Assert.Equal(paths.Select(Path.GetFileName).Order(StringComparer.Ordinal), reviewNames.Order(StringComparer.Ordinal));
            var button = page.Locator("#preliminary-quotation-button");
            Assert.True(await button.IsEnabledAsync());
            await using var preview = await page.RunAndWaitForPopupAsync(() => button.ClickAsync());
            preview.PageError += (_, error) => pageErrors.Enqueue(error);
            await preview.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.Equal(reviewNames, await preview.Locator(".iq-preliminary-quotation-part h2").AllInnerTextsAsync());
            Assert.Equal(1, await preview.Locator(".iq-preliminary-quotation-summary").CountAsync());
            await WaitForPreliminaryPdfImagesAndFontsAsync(preview, partCount);
            Assert.StartsWith("data:image/webp;base64,", await preview.Locator(".iq-preliminary-quotation-logo").GetAttributeAsync("src"), StringComparison.Ordinal);
            Assert.True(await preview.Locator(".iq-preliminary-quotation-thumbnail").EvaluateAllAsync<bool>(
                """
                images => images.every(image => {
                    const bounds = image.getBoundingClientRect();
                    return image.getAttribute('src')?.startsWith('data:image/png')
                        && bounds.width >= 150 && bounds.height >= 150 && getComputedStyle(image).objectFit === 'contain';
                })
                """));
            var pdf = await preview.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true });
            // Retain the exact returned bytes before assertions, including a failing page floor.
            var rawPageCount = System.Text.RegularExpressions.Regex.Matches(
                System.Text.Encoding.ASCII.GetString(pdf), @"/Type\s*/Page(?:\s|/|>)").Count;
            WritePreliminaryPdfEvidence(partCount, minimumPages, pdf, rawPageCount);
            Assert.NotEmpty(pdf);
            Assert.True(rawPageCount >= minimumPages,
                $"PDF page-object floor failed: parts={partCount}; pages={rawPageCount}; minimum={minimumPages}.");
            Assert.Empty(pageErrors);
        }
        finally
        {
            foreach (var path in paths) File.Delete(path);
        }
    }

    private static async Task WaitForPreliminaryPdfImagesAndFontsAsync(IPage preview, int partCount)
    {
        await preview.EvaluateAsync(
            """
            async count => {
                const images = [...document.querySelectorAll('.iq-preliminary-quotation-logo, .iq-preliminary-quotation-thumbnail')];
                if (images.length !== count + 1) throw new Error('Unexpected preliminary PDF image count');
                let timer;
                try {
                    await Promise.race([
                        (async () => {
                            await document.fonts.ready;
                            for (const image of images) {
                                image.scrollIntoView({ block: 'center', behavior: 'instant' });
                                await new Promise(resolve => requestAnimationFrame(resolve));
                                await image.decode();
                                if (!image.complete || image.naturalWidth <= 0) throw new Error('Undecoded preliminary PDF image');
                            }
                            if (document.fonts.status !== 'loaded') throw new Error('Preliminary PDF fonts unavailable');
                            window.scrollTo(0, 0);
                        })(),
                        new Promise((_, reject) => {
                            timer = setTimeout(() => reject(new Error('Preliminary PDF readiness exceeded 30s')), 30000);
                        })
                    ]);
                } finally {
                    clearTimeout(timer);
                }
            }
            """, partCount);
    }

    private static void WritePreliminaryPdfEvidence(int partCount, int minimumPages, byte[] bytes, int rawPageCount)
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("WEB_PRELIMINARY_PDF_EVIDENCE_DIRECTORY");
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "TestResults", "preliminary-pdf-proof")
            : Path.GetFullPath(configuredDirectory);
        Directory.CreateDirectory(directory);
        var stem = $"parts-{partCount:00}";
        var pdfPath = Path.Combine(directory, stem + ".pdf");
        WritePreliminaryPdfEvidenceAtomically(pdfPath, bytes);
        var manifest = new
        {
            schema = "preliminary-popup-pdf-v1",
            partCount,
            minimumPages,
            pdfFile = stem + ".pdf",
            byteLength = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            rawPageCount,
            format = "A4",
            printBackground = true,
            fixtureBoundary = "owned-program-kestrel-in-memory-upload-native-analysis-production-popup",
        };
        WritePreliminaryPdfEvidenceAtomically(Path.Combine(directory, stem + ".json"), System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })));
    }

    private static string CreatePreliminaryPdfBoxStl(int index)
    {
        var stem = $"maliev-pdf-part-{index:00}-{Guid.NewGuid():N}";
        try
        {
            return CreateBoxStl(20, 20, 5, stem);
        }
        catch
        {
            // Existing helper chooses its final random suffix internally. Own the unique
            // prefix before entering it so even a failed write can be cleaned safely.
            foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), stem + "-*.stl")) File.Delete(path);
            throw;
        }
    }

    private static void WritePreliminaryPdfEvidenceAtomically(string target, byte[] bytes)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
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
        CountingPricingService pricing,
        MaterialCompletionObservation? observation = null) : TestingWebApplicationFactory(contentRoot)
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
                observation?.Decorate(services);
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
