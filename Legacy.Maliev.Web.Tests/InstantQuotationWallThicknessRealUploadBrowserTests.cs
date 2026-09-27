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
    [Fact]
    public async Task ThinFdmUploadWarnsAndHeatmapDoesNotRepriceOrBlockReview()
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
        Assert.Contains("Instant 3D Printing Estimate", await page.TitleAsync(), StringComparison.Ordinal);
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
            Assert.Contains("0.80 mm", warning, StringComparison.Ordinal);

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
            });
        }
    }

    private static string CreateBoxStl(float width, float depth, float height)
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
        var path = Path.Combine(Path.GetTempPath(), $"maliev-thin-fdm-{Guid.NewGuid():N}.stl");
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

        public int VerifiedUploads => Volatile.Read(ref verifiedUploads);

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

            Interlocked.Increment(ref verifiedUploads);
            return InstantQuotationUploadResult.Succeeded(
                operationId,
                new InstantQuotationUploadReference(Guid.NewGuid().ToString("D")),
                sha256);
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
}
