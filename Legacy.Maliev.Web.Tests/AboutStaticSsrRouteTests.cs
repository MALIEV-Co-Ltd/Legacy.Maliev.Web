using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class AboutStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly TestingWebApplicationFactory factory;

    public AboutStaticSsrRouteTests(TestingWebApplicationFactory factory) => this.factory = factory;

    [Theory]
    [InlineData("en", true, "About MALIEV | Prototype Development & Reverse Engineering", "From concept to a working part.")]
    [InlineData("th", true, "เกี่ยวกับ MALIEV | พัฒนาต้นแบบและวิศวกรรมย้อนกลับ", "จากไอเดีย สู่ชิ้นงานจริง")]
    [InlineData("en", false, "About MALIEV | Prototype Development & Reverse Engineering", "From concept to a working part.")]
    [InlineData("th", false, "เกี่ยวกับ MALIEV | พัฒนาต้นแบบและวิศวกรรมย้อนกลับ", "จากไอเดีย สู่ชิ้นงานจริง")]
    public async Task About_BothOwnersRenderLocalizedSourcePresentationAndDocumentContract(string culture, bool blazor, string title, string heading)
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:About", blazor ? "true" : "false"));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/about?culture={culture}&tracking=excluded");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", html, StringComparison.Ordinal);
        Assert.Contains($"property=\"og:title\" content=\"{title}\"", html, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", html, StringComparison.Ordinal);
        var about = Assert.Single(Regex.Matches(html,
            "<main class=\"landing-main profile-about\"[^>]*>(?<body>.*?)</main>",
            RegexOptions.Singleline).Cast<Match>()).Groups["body"].Value;
        var imageUrls = Regex.Matches(about, "<img\\b[^>]*src=\"(?<url>/src/images/[^\"]+)\"",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["url"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(imageUrls);
        foreach (var url in imageUrls)
        {
            using var image = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal(url.EndsWith(".svg", StringComparison.Ordinal) ? "image/svg+xml" : "image/webp",
                image.Content.Headers.ContentType?.MediaType);
            var expected = await File.ReadAllBytesAsync(Path.Combine(
                BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", url.TrimStart('/')));
            var actual = await image.Content.ReadAsByteArrayAsync();
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected)),
                Convert.ToHexStringLower(SHA256.HashData(actual)));
        }
        Assert.Equal(36, Regex.Matches(html, "data-customer=\"([^\"]+)\"").Count);
        Assert.Contains(culture == "th"
            ? "บริษัทที่เลือกใช้บริการพัฒนาและผลิตชิ้นงานกับ MALIEV"
            : "Companies that have chosen MALIEV for manufacturing and development work.", html, StringComparison.Ordinal);
        foreach (var customer in new[] { "alten-korea", "forth", "do-day-dream", "thai-agri-foods", "sahamitr-pressure-container", "crown-ceramics" })
        {
            Assert.Single(Regex.Matches(html, $"data-customer=\"{customer}\""));
            Assert.Contains($"/src/images/customers/{customer}.webp", html, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("index-creative-village", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Index Creative Village", html, StringComparison.Ordinal);
        Assert.DoesNotContain("อินเด็กซ์ ครีเอทีฟ วิลเลจ", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "คราวน์ เซรามิคส์" : "Crown Ceramics", html, StringComparison.Ordinal);
        foreach (var customer in new[] { "sahamitr-pressure-container", "crown-ceramics" })
        {
            var card = Assert.Single(Regex.Matches(html,
                $"<li class=\"profile-customer\" data-customer=\"{customer}\">(?<card>.*?)</li>",
                RegexOptions.Singleline)).Groups["card"].Value;
            Assert.Contains("class=\"profile-customer-logo\"", card, StringComparison.Ordinal);
            Assert.DoesNotContain("profile-customer-logo-dark", card, StringComparison.Ordinal);
        }
        Assert.Single(Regex.Matches(html, "<h1(?:\\s|>)", RegexOptions.IgnoreCase));
        Assert.Contains($"data-migration-route-owner=\"{(blazor ? "blazor-static-ssr" : "razor-static-ssr")}\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\"", html, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\"", html, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"th\"", html, StringComparison.Ordinal);
        var documentLinks = string.Join("\n", Regex.Matches(html, "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>").Select(match => match.Value));
        Assert.DoesNotContain("tracking=excluded", documentLinks, StringComparison.Ordinal);
        foreach (var destination in new[] { "/Contact", "/Services/CNC-Machining", "/Services/3D-Printing", "/Services/3D-Scanning", "/Services/3D-Design", "/Services/Silicone-Casting", "/Services/Low-Volume-Injection-Molding", "/Legal/NonDisclosureAgreement" })
        {
            Assert.Contains($"href=\"{destination}?culture={culture}\"", html, StringComparison.Ordinal);
        }
        Assert.Contains("rel=\"preload\" as=\"image\" href=\"/src/images/landing/about-customer-partnership.webp\"", html, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wowjs", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", html, StringComparison.OrdinalIgnoreCase);
    }
}
