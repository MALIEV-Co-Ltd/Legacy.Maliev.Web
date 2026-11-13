using System.Net;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationWallThicknessRealUploadBrowserTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task RetainedPartRemovalFailure_KeepsAlertAboveUnitSelectorAndWorkingPanes(string culture)
    {
        var upload = new HashCheckingUploadClient { AllowVerifiedRemoval = false };
        var pricing = new CountingPricingService();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, pricing);
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        var url = new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString();
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        using var readiness = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "retained-part.stl",
            MimeType = "model/stl",
            Buffer = WallThicknessParityBrowserFixture.Box(5),
        });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[data-workflow-part]').length === 1 && !document.querySelector('#instant-quote-files').disabled",
            null, new() { Timeout = 90000 });
        await Assertions.Expect(page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions > button")).ToBeEnabledAsync();
        var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        var before = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.NotNull(before?.QuoteAuthorization);

        await page.Locator("[data-workflow-part] > button").Last.ClickAsync();

        await Assertions.Expect(page.Locator(".instant-quote__workflow")).ToHaveAttributeAsync("data-workflow-state", "error");
        await Assertions.Expect(page.Locator(".instant-quote__workflow > p[role='alert']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-workflow-part]")).ToHaveCountAsync(1);
        var geometry = await page.EvaluateAsync<double[]>("""
            () => {
                const root=document.querySelector('.instant-quote__workflow');
                const box=selector=>root.querySelector(':scope > '+selector).getBoundingClientRect();
                const alert=box('p[role="alert"]'),units=box('.instant-quote__gltf-unit');
                const viewer=box('[data-workflow-viewer]'),parts=box('[data-workflow-parts-panel]');
                const configuration=box('[data-workflow-configuration]');
                return [alert.top,alert.bottom,units.top,units.bottom,viewer.top,viewer.bottom,
                    parts.top,parts.bottom,configuration.top,configuration.bottom,
                    alert.left,alert.right,viewer.left,configuration.right];
            }
            """);
        var evidence = Environment.GetEnvironmentVariable("MALIEV_BROWSER_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence);
            await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, $"retained-part-error-{culture}.png"), FullPage = true });
        }
        Assert.True(geometry[1] <= geometry[2] + 1 && geometry[3] <= geometry[4] + 1
            && geometry[6] >= geometry[5] - 1 && Math.Abs(geometry[8] - geometry[4]) <= 1
            && geometry[9] >= geometry[7] - 1 && geometry[10] <= geometry[12] + 1
            && geometry[11] >= geometry[13] - 1,
            "The retained-part alert must precede the units and both working panes: " + JsonSerializer.Serialize(geometry));
        var after = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default);
        Assert.Equal(Assert.Single(before!.Parts).PartId, Assert.Single(after!.Parts).PartId);
        Assert.Equal(JsonSerializer.Serialize(before.QuoteAuthorization), JsonSerializer.Serialize(after.QuoteAuthorization));
        Assert.Equal(1, upload.VerifiedUploads);
    }
}
