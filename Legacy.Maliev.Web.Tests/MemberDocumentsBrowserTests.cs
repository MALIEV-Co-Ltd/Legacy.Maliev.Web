using Legacy.Maliev.Web.Application;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real browser and SSR/BFF controls against isolated synthetic boundaries; no live owner integration.</summary>
public sealed class MemberDocumentsBrowserTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task LocalizedMemberControlsHaveLabelsCsrfAndNoProtectedMetadata(string culture)
    {
        var registry = new MemberDocumentsHttpTests.RecordingRegistry();
        await using var host = await MemberDocumentsHttpTests.HostAsync(registry, loopbackBrowser: true);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ExtraHTTPHeaders = new Dictionary<string, string> { ["Synthetic-Member"] = "yes" } });
        await using var page = await context.NewPageAsync();
        var response = await page.GotoAsync(host.Urls.Single() + "/fixture/page?culture=" + culture);
        Assert.Equal(200, response?.Status);
        var heading = await page.Locator("#member-documents-title").InnerTextAsync();
        Assert.True(culture == "en" ? heading == "Documents" : heading.Any(x => x is >= '\u0e00' and <= '\u0e7f'));
        Assert.Equal(1, await page.Locator("label[for=document-title]").CountAsync());
        Assert.Equal(1, await page.Locator("label[for=document-kind]").CountAsync());
        Assert.Equal(1, await page.Locator("label[for=document-file]").CountAsync());
        Assert.Equal(7, await page.Locator("#document-kind option").CountAsync());
        Assert.Equal("application/pdf,image/png,image/jpeg", await page.Locator("#document-file").GetAttributeAsync("accept"));
        Assert.Equal(2, await page.Locator("input[name=__RequestVerificationToken]").CountAsync());
        await page.Locator("#document-title").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.Locator("#document-kind").EvaluateAsync<bool>("element => document.activeElement === element"));
        var versionText = await page.Locator("li li").First.InnerTextAsync();
        Assert.Contains("1 - ", versionText); Assert.DoesNotContain("\u00e2\u20ac\u201d", versionText);
        var html = await page.ContentAsync();
        Assert.DoesNotContain("member-secret", html); Assert.DoesNotContain("VerifiedBySubject", html);
        Assert.Equal(42, registry.CustomerId);
        await EvidenceAsync(page, $"member-{culture}-200");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task UnavailableResponseWithStalePayloadClearsBrowserMetadataAndDisablesSubmission(string culture)
    {
        var registry = new MemberDocumentsHttpTests.RecordingRegistry { StatusCode = 503, Summary = new CustomerDocumentSummary(Guid.NewGuid(), 42, "Nda", "stale-protected-title", "Customer", 1) };
        await using var host = await MemberDocumentsHttpTests.HostAsync(registry, loopbackBrowser: true);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ExtraHTTPHeaders = new Dictionary<string, string> { ["Synthetic-Member"] = "yes" } });
        await using var page = await context.NewPageAsync();
        await page.GotoAsync(host.Urls.Single() + "/fixture/page?culture=" + culture);
        Assert.DoesNotContain("stale-protected-title", await page.ContentAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Alert).CountAsync());
        Assert.True(await page.Locator("form[action='/member/documents/upload'] button").IsDisabledAsync());
        Assert.Equal(0, await page.Locator("a[href$='/download']").CountAsync());
        await EvidenceAsync(page, $"member-{culture}-503");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task AnonymousBrowserPageRejectsBeforeDocumentRead(string culture)
    {
        var registry = new MemberDocumentsHttpTests.RecordingRegistry();
        await using var host = await MemberDocumentsHttpTests.HostAsync(registry, loopbackBrowser: true);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var page = await browser.NewPageAsync();
        var response = await page.GotoAsync(host.Urls.Single() + "/fixture/page?culture=" + culture);
        Assert.Equal(401, response?.Status); Assert.Equal(0, registry.CustomerId);
        Assert.Equal(0, await page.Locator("#member-documents-title").CountAsync());
        await EvidenceAsync(page, $"member-{culture}-401");
    }

    private static async Task EvidenceAsync(IPage page, string name)
    {
        var evidence = Environment.GetEnvironmentVariable("TASK4_BROWSER_EVIDENCE") ?? Path.GetFullPath("handoffs/customer-documents/browser-evidence");
        Directory.CreateDirectory(evidence);
        await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, name + ".png"), FullPage = true });
        await File.WriteAllTextAsync(Path.Combine(evidence, name + ".html"), await page.ContentAsync());
    }
}
