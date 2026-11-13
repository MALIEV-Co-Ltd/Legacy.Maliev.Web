using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceHeroOrderStaticSsrTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    public static IEnumerable<object[]> Cases()
    {
        string[][] routes =
        [
            ["/services/3d-design", "/Quotation?item=3d-design", "Sketch and drawing development", "พัฒนาจากภาพร่างและแบบ", "true"],
            ["/services/3d-printing", "/InstantQuotation/3D-Printing", "Instant online estimate", "ประเมินราคาออนไลน์ทันที", "true"],
            ["/services/3d-scanning", "/Quotation?item=3D-Scanning", "In-house and onsite", "ในสถานที่และนอกสถานที่", "true"],
            ["/services/cnc-machining", "/Quotation?item=CNC-Machining", "One piece welcome", "รับงานชิ้นเดียว", "true"],
            ["/services/finishing-and-color", "/quotation?item=3D-Printing", "Pantone and RAL colour matching", "เทียบสีด้วยรหัส Pantone และ RAL", "false"],
            ["/services/low-volume-injection-molding", "/Quotation?item=low-volume-injection-molding", "Up to 1,000 parts", "ไม่เกิน 1,000 ชิ้น", "true"],
            ["/services/silicone-casting", "/Quotation?item=silicone-casting", "3D-printed mold workflow", "ขั้นตอนด้วยแม่พิมพ์พิมพ์ 3 มิติ", "true"],
        ];
        foreach (var route in routes)
        {
            foreach (var culture in new[] { "en", "th" })
            {
                yield return [route[0], route[1], culture, culture == "th" ? route[3] : route[2], route[4] == "true"];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ServiceHero_RendersActionLinksImmediatelyBeforeDetails(
        string route, string primaryHref, string culture, string expectedDetail, bool hasQuickFacts)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"{route}?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        var heroes = Regex.Matches(html,
            "<section\\b[^>]*class=\"service-hero(?:\\s[^\"]*)?\"[^>]*>.*?</section>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var hero = Assert.Single(heroes.Cast<Match>());
        var ordered = Regex.Match(hero.Value,
            "<div class=\"service-actions\">(?<actions>.*?)</div>\\s*<ul class=\"service-hero-points\">(?<details>.*?)</ul>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(ordered.Success, $"{route} ({culture}) must render its actions immediately before its details.");
        Assert.Contains($"href=\"{primaryHref}\"", ordered.Groups["actions"].Value, StringComparison.Ordinal);
        Assert.True(Regex.Matches(ordered.Groups["details"].Value, "<li(?:\\s|>)").Count >= 3);
        Assert.Contains(expectedDetail, WebUtility.HtmlDecode(ordered.Groups["details"].Value), StringComparison.Ordinal);
        if (hasQuickFacts)
        {
            var quickFacts = Regex.Match(html, "<section\\b[^>]*class=\"service-quick(?:\\s[^\"]*)?\"",
                RegexOptions.CultureInvariant);
            Assert.True(quickFacts.Success && hero.Index + hero.Length <= quickFacts.Index,
                $"{route} ({culture}) must render hero details before its quick facts.");
        }
    }
}
