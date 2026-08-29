using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class LegalContentParityTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public LegalContentParityTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("en", "Privacy Policy | MALIEV", "Learn how MALIEV collects, uses, protects, and retains information when you use our website and manufacturing services.", "24 July 2026")]
    [InlineData("th", "นโยบายความเป็นส่วนตัว | MALIEV", "นโยบายความเป็นส่วนตัวของ MALIEV อธิบายการเก็บ ใช้ และคุ้มครองข้อมูลเมื่อคุณใช้เว็บไซต์และบริการผลิตของเรา", "24 กรกฎาคม 2569")]
    public async Task PrivacyPolicyRoute_PreservesSourceCurrentMetadataAndSectionAnchors(
        string culture,
        string title,
        string description,
        string effectiveDate)
    {
        var source = await GetDocumentAsync($"/legal/privacypolicy?culture={culture}");

        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains(effectiveDate, source, StringComparison.Ordinal);
        foreach (var anchor in new[] { "collection", "use", "transfer", "security", "contact" })
        {
            Assert.Contains($"id=\"{anchor}\"", source, StringComparison.Ordinal);
            Assert.Contains($"href=\"#{anchor}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("https://www.maliev.com", source, StringComparison.Ordinal);
        Assert.Contains("https://www.maliev.com/legal/termsconditions", source, StringComparison.Ordinal);
        Assert.Contains("https://www.maliev.com/contact", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Terms and Conditions | MALIEV", "Read MALIEV website, manufacturing service, file-submission, and user responsibility terms and conditions.", "24 July 2026")]
    [InlineData("th", "ข้อกำหนดและเงื่อนไขการให้บริการ | MALIEV", "อ่านข้อกำหนดการใช้เว็บไซต์ บริการผลิต การส่งข้อมูล และความรับผิดชอบของผู้ใช้ MALIEV", "24 กรกฎาคม 2569")]
    public async Task TermsConditionsRoute_PreservesSourceCurrentMetadataAndSectionAnchors(
        string culture,
        string title,
        string description,
        string effectiveDate)
    {
        var source = await GetDocumentAsync($"/legal/termsconditions?culture={culture}");

        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains(effectiveDate, source, StringComparison.Ordinal);
        foreach (var anchor in new[] { "terms-overview", "cookies", "license", "hyperlinking", "rights", "disclaimer" })
        {
            Assert.Contains($"id=\"{anchor}\"", source, StringComparison.Ordinal);
            Assert.Contains($"href=\"#{anchor}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("https://www.maliev.com", source, StringComparison.Ordinal);
        Assert.DoesNotContain("http://www.maliev.com", source, StringComparison.Ordinal);
    }

    private async Task<string> GetDocumentAsync(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync(path);
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        return source;
    }
}
