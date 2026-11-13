using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class IllustratedServiceHeroHttpTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/services/3d-printing", "printing/printing-hero-prototypes-transparent", 1536, 1024, "Illustration of 3D-printed engineering prototypes in nylon, plastic and resin", "ภาพประกอบต้นแบบชิ้นงานพิมพ์ 3 มิติจากไนลอน พลาสติก และเรซิ่น", true)]
    [InlineData("/services/3d-scanning", "hero-illustrations/scanning-hero-raptor-x-unbranded", 1536, 1024, "Illustration of a 3D scanner, a metal housing and its digital mesh", "ภาพประกอบเครื่องสแกน 3 มิติ ชิ้นงานโลหะและ mesh ดิจิทัล", true)]
    [InlineData("/services/cnc-machining", "hero-illustrations/cnc-hero-light", 1536, 1024, "Illustration of machined aluminium components", "ภาพประกอบชิ้นส่วนอะลูมิเนียมที่ผ่านการกัด CNC", true)]
    [InlineData("/services/custom-manufacturing", "hero-illustrations/custom-hero-light", 1536, 1024, "Illustration of custom metal and polymer components", "ภาพประกอบชิ้นส่วนโลหะและพลาสติกที่ผลิตตามแบบ", true)]
    [InlineData("/services/3d-design", "hero-illustrations/design-hero-light", 1774, 887, "Illustration of a sketch, CAD model and manufactured bracket", "ภาพประกอบภาพร่าง แบบ CAD และชิ้นส่วนที่ผลิต", false)]
    [InlineData("/services/finishing-and-color", "hero-illustrations/finishing-hero-light", 1774, 887, "Illustration of painted enclosures with different surface finishes", "ภาพประกอบกล่องชิ้นงานที่พ่นสีและเก็บผิวต่างกัน", false)]
    [InlineData("/services/low-volume-injection-molding", "hero-illustrations/injection-hero-light", 1536, 1024, "Illustration of molded plastic parts and a mold", "ภาพประกอบชิ้นส่วนพลาสติกฉีดและแม่พิมพ์", false)]
    [InlineData("/services/silicone-casting", "hero-illustrations/silicone-hero-light", 1536, 1024, "Illustration of a mold and a flexible silicone component", "ภาพประกอบแม่พิมพ์และชิ้นส่วนซิลิโคนยืดหยุ่น", false)]
    public async Task IllustratedHero_RendersSourceAssetsAndLocalizedAltAcrossRouteOwners(
        string route, string stem, int width, int height, string english, string thai, bool hasFallback)
    {
        foreach (bool active in hasFallback ? new[] { true, false } : new[] { true })
        {
            await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Services", active.ToString()));
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            foreach (var (culture, alt) in new[] { ("en", english), ("th", thai) })
            {
                var html = WebUtility.HtmlDecode(await client.GetStringAsync($"{route}?culture={culture}&tracking=excluded"));
                var hero = Assert.Single(Regex.Matches(html, "<img\\b(?=[^>]*class=\"service-hero-media)[^>]*>", RegexOptions.IgnoreCase).Cast<Match>()).Value;
                var source = $"/src/images/services/{stem}.webp";
                var candidates = $"/src/images/services/{stem}-640.webp 640w, /src/images/services/{stem}-768.webp 768w, /src/images/services/{stem}-960.webp 960w, {source} {width}w";
                Assert.Contains($"src=\"{source}\"", hero, StringComparison.Ordinal);
                Assert.Contains($"srcset=\"{candidates}\"", hero, StringComparison.Ordinal);
                Assert.Contains("sizes=\"(max-width: 767px) 100vw, 50vw\"", hero, StringComparison.Ordinal);
                Assert.Contains($"width=\"{width}\"", hero, StringComparison.Ordinal);
                Assert.Contains($"height=\"{height}\"", hero, StringComparison.Ordinal);
                Assert.Contains($"alt=\"{alt}\"", hero, StringComparison.Ordinal);
                Assert.Contains($"imagesrcset=\"{candidates}\"", html, StringComparison.Ordinal);
                Assert.Contains("imagesizes=\"(max-width: 767px) 100vw, 50vw\"", html, StringComparison.Ordinal);
                foreach (var suffix in new[] { "-640", "-768", "-960", "" })
                {
                    using var asset = await client.GetAsync($"/src/images/services/{stem}{suffix}.webp");
                    asset.EnsureSuccessStatusCode();
                    Assert.Equal("image/webp", asset.Content.Headers.ContentType?.MediaType);
                }
            }
        }
    }
}
