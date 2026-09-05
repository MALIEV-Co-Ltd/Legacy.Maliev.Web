using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class NoWeaponsStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public NoWeaponsStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresTheNoWeaponsRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Legal", "NoWeaponsPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Legal", "NoWeaponsContent.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Legal", "NoWeapons.cshtml"));

        Assert.Contains("BlazorRouting:NoWeapons", program, StringComparison.Ordinal);
        Assert.Contains("\"/Legal/NoWeapons\"", program, StringComparison.Ordinal);
        Assert.Contains("\"NoWeapons\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(NoWeaponsContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Weapons manufacturing policy | MALIEV",
        "Why MALIEV does not manufacture, assemble, repair or modify weapons and firearm parts, with relevant Thai law and a downloadable reference Act.",
        "Weapons manufacturing policy",
        "does not hold an arms-factory licence",
        "as prescribed by ministerial regulation",
        "not a consolidated current edition",
        "Weapons manufacturing policy")]
    [InlineData(
        "th",
        "นโยบายไม่รับผลิตอาวุธและชิ้นส่วนปืน | MALIEV",
        "MALIEV ไม่รับผลิต ประกอบ ซ่อมแซม หรือดัดแปลงอาวุธและชิ้นส่วนปืน อ่านเหตุผล ข้อกฎหมายไทยที่เกี่ยวข้อง และเอกสารกฎหมายฉบับอ้างอิง",
        "นโยบายไม่รับผลิตอาวุธและชิ้นส่วนปืน",
        "ไม่มีใบอนุญาตประกอบกิจการโรงงานทำอาวุธ",
        "ตามที่กำหนดในกฎกระทรวง",
        "ไม่ใช่ฉบับรวมการแก้ไขล่าสุด",
        "นโยบายไม่รับผลิตอาวุธ")]
    public async Task NoWeaponsRoute_RendersLocalizedAccessibleStaticSsrWithFooterOnlyDiscoveryAndLegalQualifications(
        string culture,
        string title,
        string description,
        string heading,
        string licenceText,
        string regulationText,
        string currencyNotice,
        string footerLabel)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/no-weapons?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains(licenceText, source, StringComparison.Ordinal);
        Assert.Contains(regulationText, source, StringComparison.Ordinal);
        Assert.Contains(currencyNotice, source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/no-weapons\">{footerLabel}</a>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/no-weapons\"", source[..source.IndexOf("<footer", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("download=\"arms-factory-act-2550.pdf\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/documents/arms-factory-act-2550.pdf\"", source, StringComparison.Ordinal);
        Assert.Contains("<nav class=\"legal-toc\"", source, StringComparison.Ordinal);
        foreach (var section in new[] { "policy", "law", "components", "questions", "references" })
        {
            Assert.Contains($"<section id=\"{section}\"", source, StringComparison.Ordinal);
            Assert.Contains($"href=\"#{section}\"", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoWeaponsRoute_IsIncludedInThePublicSitemapWithLocalizedAlternates()
    {
        using var client = CreateClient(factory);
        var sitemap = WebUtility.HtmlDecode(await client.GetStringAsync("/sitemap"));

        Assert.Contains("<loc>https://www.maliev.com/no-weapons</loc>", sitemap, StringComparison.Ordinal);
        Assert.Contains("href=\"https://www.maliev.com/no-weapons?culture=en\"", sitemap, StringComparison.Ordinal);
        Assert.Contains("href=\"https://www.maliev.com/no-weapons\"", sitemap, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/no-weapons?culture=en", "https://www.maliev.com/no-weapons?culture=en", "https://www.maliev.com/no-weapons")]
    [InlineData("th", "https://www.maliev.com/no-weapons", "https://www.maliev.com/no-weapons?culture=en", "https://www.maliev.com/no-weapons")]
    public async Task NoWeaponsRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/no-weapons?culture={culture}&tracking=excluded"));

        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoWeaponsDocument_IsServedAsTheExactFirstPartyReferencePdf()
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/documents/arms-factory-act-2550.pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(227537, bytes.Length);
        Assert.Equal("A54F4D58B025A2C46123E8DCA1E26829821239154959EE1A521EF4889EE651C1", Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task DisabledNoWeaponsRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:NoWeapons", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/no-weapons?culture=en"));

        Assert.Contains("<title>Weapons manufacturing policy | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static int CountLink(string source, string relation, string url) =>
        Regex.Matches(source, $"<link(?=[^>]*rel=\"{Regex.Escape(relation)}\")(?=[^>]*href=\"{Regex.Escape(url)}\")[^>]*>", RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        Regex.Matches(source, $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{Regex.Escape(url)}\")(?=[^>]*hreflang=\"{Regex.Escape(language)}\")[^>]*>", RegexOptions.CultureInvariant).Count;

    private static string ExtractDocumentLinks(string source) => string.Join(
        Environment.NewLine,
        Regex.Matches(source, "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => match.Value));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
