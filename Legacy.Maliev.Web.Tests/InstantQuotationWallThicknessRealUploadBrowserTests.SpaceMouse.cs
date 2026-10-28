using System.Net;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationWallThicknessRealUploadBrowserTests
{
    [Theory]
    [InlineData("en", "SpaceMouse help")]
    [InlineData("th", "การใช้งาน SpaceMouse")]
    public async Task PrintingUpload_UnavailableSpaceMousePreservesOwnedPreviewAndLicensedHelp(string culture, string label)
    {
        var upload = new HashCheckingUploadClient { OwnPhysicalReadLifetime = true };
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var origin = new Uri($"http://127.0.0.1:{ReserveFreePort()}");
        factory.UseKestrel(origin.Port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}&tracking=excluded").ToString();
        using var readiness = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        await using var page = await context.NewPageAsync();
        var probes = 0;
        await page.RouteAsync("https://127.51.68.120:8181/**", async route =>
        {
            Interlocked.Increment(ref probes);
            await route.FulfillAsync(new() { Status = 503, ContentType = "text/plain", Body = "Unavailable in isolated regression" });
        });
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
        var consent = page.Locator("#cookieConsent [data-consent-action='reject']");
        if (await consent.CountAsync() > 0) await consent.ClickAsync();
        await WaitForUploadInputReadyAsync(page);
        var help = page.Locator("[data-printing-spacemouse-help]");
        Assert.Equal(label, await help.InnerTextAsync());
        Assert.Equal("/help/spacemouse.html", await help.GetAttributeAsync("href"));
        Assert.Equal("_blank", await help.GetAttributeAsync("target"));
        Assert.Equal("noopener", await help.GetAttributeAsync("rel"));
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "printing-spacemouse-box.stl",
            MimeType = "model/stl",
            Buffer = WallThicknessParityBrowserFixture.Box(5),
        });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-workflow-viewer] canvas[data-spacemouse-status=unavailable]') && document.querySelectorAll('[data-workflow-part]').length === 1 && !!document.querySelector('[data-workflow-price-tier]')",
            null, new() { Timeout = 60_000 });
        Assert.True(Volatile.Read(ref probes) > 0);
        var canvas = page.Locator("[data-workflow-viewer] canvas[data-spacemouse-status]");
        await canvas.FocusAsync();
        await canvas.PressAsync("Home");
        await canvas.PressAsync("ArrowLeft");
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
        Assert.Equal(1, upload.VerifiedUploads);
        // The active workflow prices through the authoritative physical producer, not this legacy interface.
        Assert.Equal(0, pricing.CallCount);
        var session = Assert.IsType<InstantQuotationSessionState>(await factory.Services
            .GetRequiredService<IInstantQuotationSessionStore>().GetAsync(upload.SessionId, upload.OwnerIdentity, default));
        var part = Assert.Single(session.Parts);
        var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(session.QuoteAuthorization);
        Assert.Single(authorization.LineTickets);
        Assert.NotEmpty(authorization.OrderTicket);
        var receipt = Assert.Single(session.PhysicalReceipts!, item => item.PartId == part.PartId
            && item.MaterialKey == part.Configuration.MaterialKey && item.Quantity == part.Configuration.Quantity);
        Assert.Equal(session.SessionId, receipt.SessionId);
        Assert.Equal(upload.OwnerIdentity, receipt.OwnerIdentity);
        Assert.Equal(part.Geometry.Sha256, receipt.UploadSha256);
        Assert.True(receipt.DepositedMm3 > 0);
        Assert.True(receipt.MotionSeconds > 0);
        var proofDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "material-display-browser-controls");
        Directory.CreateDirectory(proofDirectory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(proofDirectory, $"printing-spacemouse-{culture}-preview.png"), FullPage = true });
        await using var helpPage = await page.RunAndWaitForPopupAsync(() => help.ClickAsync());
        await helpPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        Assert.Equal(new Uri(origin, "/help/spacemouse.html").ToString(), helpPage.Url);
        Assert.Equal(url, page.Url);
        Assert.Equal(1, await page.Locator("[data-workflow-part]").CountAsync());
        using var helpResponse = await client.GetAsync(helpPage.Url);
        Assert.NotNull(helpResponse);
        Assert.Equal(HttpStatusCode.OK, helpResponse.StatusCode);
        Assert.Contains("3Dconnexion 1992 - 2025. All rights reserved.", await helpPage.Locator("main").InnerTextAsync(), StringComparison.Ordinal);
        await helpPage.ScreenshotAsync(new() { Path = Path.Combine(proofDirectory, $"printing-spacemouse-{culture}-help.png"), FullPage = true });
    }
}
