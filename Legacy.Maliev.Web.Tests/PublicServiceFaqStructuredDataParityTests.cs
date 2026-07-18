using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class PublicServiceFaqStructuredDataParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicServiceFaqStructuredDataParityTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Theory]
    [InlineData("/services/3d-printing", "en")]
    [InlineData("/services/3d-printing", "th")]
    [InlineData("/services/3d-scanning", "en")]
    [InlineData("/services/3d-scanning", "th")]
    [InlineData("/services/cnc-machining", "en")]
    [InlineData("/services/cnc-machining", "th")]
    [InlineData("/services/custom-manufacturing", "en")]
    [InlineData("/services/custom-manufacturing", "th")]
    public async Task Route_ExposesEveryVisibleFaqItemInJsonLdWithoutHiddenItems(string route, string culture)
    {
        foreach (var useBlazorRoute in new[] { true, false })
        {
            using var routeFactory = factory.WithWebHostBuilder(builder =>
                builder.UseSetting("BlazorRouting:Services", useBlazorRoute.ToString()));
            using var client = routeFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            using var response = await client.GetAsync($"{route}?culture={culture}");
            var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var visibleFaq = DetailsRegex().Matches(source)
                .Select(match => new FaqItem(
                    NormalizeText(match.Groups["question"].Value),
                    NormalizeText(match.Groups["answer"].Value)))
                .ToArray();
            Assert.NotEmpty(visibleFaq);

            using var schema = FindFaqSchema(source);
            var structuredFaq = schema.RootElement.GetProperty("mainEntity")
                .EnumerateArray()
                .Select(item => new FaqItem(
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("acceptedAnswer").GetProperty("text").GetString()!))
                .ToArray();

            Assert.Equal(visibleFaq, structuredFaq);
            Assert.All(structuredFaq, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Question));
                Assert.False(string.IsNullOrWhiteSpace(item.Answer));
            });
        }
    }

    [Theory]
    [InlineData("/services/3d-printing", "en", "Answers before you upload")]
    [InlineData("/services/3d-printing", "th", "คำตอบก่อนอัปโหลดไฟล์")]
    [InlineData("/services/3d-scanning", "en", "Plan the right scanning scope")]
    [InlineData("/services/3d-scanning", "th", "วางขอบเขตงานสแกนให้ถูกต้อง")]
    [InlineData("/services/cnc-machining", "en", "Questions customers ask before ordering")]
    [InlineData("/services/cnc-machining", "th", "คำถามที่ลูกค้าถามก่อนสั่งผลิต")]
    [InlineData("/services/custom-manufacturing", "en", "Custom manufacturing FAQ")]
    [InlineData("/services/custom-manufacturing", "th", "คำถามเรื่องผลิตชิ้นงานตามแบบ")]
    public async Task Route_RendersFaqAsAnAccessibleStaticSsrRegion(
        string route,
        string culture,
        string heading)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync($"{route}?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-service-faq\"", source, StringComparison.Ordinal);
        Assert.Matches(
            $"<section[^>]+aria-labelledby=\"(?<id>[^\"]+)\"[^>]*>.*?<h2[^>]+id=\"\\k<id>\"[^>]*>{Regex.Escape(heading)}</h2>",
            source);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonDocument FindFaqSchema(string source)
    {
        foreach (Match match in JsonLdScriptRegex().Matches(source))
        {
            var document = JsonDocument.Parse(match.Groups["json"].Value);
            if (document.RootElement.TryGetProperty("@type", out var type) && type.GetString() == "FAQPage")
            {
                return document;
            }

            document.Dispose();
        }

        throw new Xunit.Sdk.XunitException("Rendered FAQPage JSON-LD was not found.");
    }

    private static string NormalizeText(string value) =>
        WhitespaceRegex().Replace(HtmlTagRegex().Replace(value, string.Empty), " ").Trim();

    private sealed record FaqItem(string Question, string Answer);

    [GeneratedRegex("<details[^>]*>\\s*<summary[^>]*>(?<question>.*?)</summary>\\s*<p[^>]*>(?<answer>.*?)</p>\\s*</details>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DetailsRegex();

    [GeneratedRegex("<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex JsonLdScriptRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
