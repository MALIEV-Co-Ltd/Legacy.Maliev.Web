using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    public async Task ExplicitMetreUpload_KeepsOneHundredMillimetresAcrossNextChoiceAndMaterialChanges(string culture)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(AdmittedGltfMeshReaderTests.OriginalGltf)!;
        document["nodes"]![0]!["scale"] = new System.Text.Json.Nodes.JsonArray(0.01, 0.01, 0.01);
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var upload = new HashCheckingUploadClient();
        await using var factory = new RealUploadTestingWebApplicationFactory(
            BrowserHostIdentityVerifier.SourceProjectDirectory(), upload, new CountingPricingService());
        var port = ReserveFreePort();
        var origin = new Uri($"http://127.0.0.1:{port}");
        factory.UseKestrel(port);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin });
        using var readiness = await client.GetAsync($"/instantquotation/3d-printing?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 1000 } });
        await page.GotoAsync(new Uri(origin, $"/instantquotation/3d-printing?culture={culture}").ToString(),
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
        await WaitForBatchInputAsync(page);
        var units = page.Locator("[data-workflow-gltf-unit]");
        await Assertions.Expect(units).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("label[for='instant-quote-gltf-unit']")).ToHaveTextAsync(
            culture == "th" ? "หน่วยสำหรับไฟล์ GLB/GLTF ที่จะอัปโหลดถัดไป" : "Units for the next GLB/GLTF upload");
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "units-not-selected.gltf",
            MimeType = "model/gltf+json",
            Buffer = bytes,
        });
        var failed = page.Locator("article[data-workflow-failed-preview]");
        await Assertions.Expect(failed).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("[data-workflow-file-error]")).ToContainTextAsync(
            culture == "th" ? "เลือกหน่วยก่อนนำเข้าไฟล์ GLB/GLTF" : "Choose units before importing GLB/GLTF");
        Assert.Equal(0, upload.StartedUploads);
        await failed.Locator("button").Last.ClickAsync();
        await Assertions.Expect(failed).ToHaveCountAsync(0);
        await units.SelectOptionAsync("metres");
        await page.SetInputFilesAsync("#instant-quote-files", new FilePayload
        {
            Name = "standard-0.1-metre.gltf",
            MimeType = "model/gltf+json",
            Buffer = bytes,
        });
        var review = page.Locator("[data-workflow-configuration] .instant-quote__configuration-actions button");
        await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
        var store = factory.Services.GetRequiredService<IInstantQuotationSessionStore>();
        var saved = (await store.GetAsync(upload.SessionId, upload.OwnerIdentity, default))!;
        var part = Assert.Single(saved.Parts);
        Assert.Equal(100, part.Geometry.DimensionXmm, 3);
        Assert.Equal(100, part.Geometry.DimensionYmm, 3);
        Assert.Equal(100, part.Geometry.DimensionZmm, 3);
        Assert.Equal(1_000_000, part.Geometry.VolumeMm3, 0);
        Assert.Equal(digest, part.Geometry.Sha256);
        Assert.Equal(new InstantQuotationGltfUnitPolicy(1, InstantQuotationGltfLengthUnit.Metres),
            part.PhysicalAnalysisUpload!.GltfUnitPolicy);
        Assert.NotNull(saved.QuoteAuthorization);
        Assert.Equal(part.PhysicalAnalysisUpload.GltfUnitPolicy, Assert.Single(saved.PhysicalReceipts!, receipt => receipt.PartId == part.PartId && receipt.MaterialKey == "PLA").GltfUnitPolicy);

        await units.SelectOptionAsync("millimetres");
        await MaterialCardBrowserActions.SelectAsync(page, "M68");
        await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
        saved = await SavedMaterialAsync("M68");
        var resin = Assert.Single(saved.Parts);
        Assert.Equal("M68", resin.Configuration.MaterialKey);
        Assert.Equal(100, resin.Geometry.DimensionXmm, 3);
        Assert.Equal(1_000_000, resin.Geometry.VolumeMm3, 0);
        Assert.Equal(part.PhysicalAnalysisUpload.GltfUnitPolicy, resin.PhysicalAnalysisUpload!.GltfUnitPolicy);
        Assert.NotNull(saved.QuoteAuthorization);

        await MaterialCardBrowserActions.SelectAsync(page, "PETG");
        await Assertions.Expect(review).ToBeEnabledAsync(new() { Timeout = 90000 });
        saved = await SavedMaterialAsync("PETG");
        var repriced = Assert.Single(saved.Parts);
        Assert.Equal("PETG", repriced.Configuration.MaterialKey);
        Assert.Equal(digest, repriced.PhysicalAnalysisUpload!.Sha256);
        Assert.Equal(part.PhysicalAnalysisUpload.GltfUnitPolicy, repriced.PhysicalAnalysisUpload.GltfUnitPolicy);
        Assert.Equal(repriced.PhysicalAnalysisUpload.GltfUnitPolicy, Assert.Single(saved.PhysicalReceipts!, receipt => receipt.PartId == repriced.PartId && receipt.MaterialKey == "PETG").GltfUnitPolicy);
        Assert.NotNull(saved.QuoteAuthorization);

        async Task<InstantQuotationSessionState> SavedMaterialAsync(string material)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                var current = await store.GetAsync(upload.SessionId, upload.OwnerIdentity, deadline.Token);
                if (current?.QuoteAuthorization is not null
                    && current.Parts.Single().Configuration.MaterialKey == material)
                    return current;
                await Task.Delay(20, deadline.Token);
            }
        }
    }
}
