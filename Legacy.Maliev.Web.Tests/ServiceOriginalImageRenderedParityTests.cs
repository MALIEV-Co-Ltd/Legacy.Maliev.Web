using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceOriginalImageRenderedParityTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    // Source 3e59654 enlarged this set to 27; later service content may add images.
    public static TheoryData<string, string[]> OriginalImages => new()
    {
        { "/services/cnc-machining", [
            "/src/images/services/cnc/cnc-hero.webp",
            "/src/images/services/cnc/cards/cnc-card-milled-parts.webp",
            "/src/images/services/cnc/cards/cnc-card-turned-parts.webp",
            "/src/images/services/cnc/cards/cnc-card-jigs-fixtures.webp",
            "/src/images/services/cnc/cards/cnc-card-replacement-parts.webp",
            "/src/images/services/cnc/cards/cnc-card-functional-prototype.webp",
            "/src/images/services/cnc/cards/cnc-card-repeat-production.webp",
            "/src/images/services/cnc/cnc-materials.webp"
        ] },
        { "/services/3d-printing", [
            "/src/images/services/printing/printing-hero.webp",
            "/src/images/services/printing/printing-fdm-application.webp",
            "/src/images/services/printing/printing-resin-application.webp",
            "/src/images/services/printing/printing-industrial-application.webp",
            "/src/images/services/printing/printing-materials.webp",
            "/src/images/services/printing/printing-file-manifold-check.webp",
            "/src/images/services/printing/printing-file-wall-clearance.webp",
            "/src/images/services/printing/printing-file-orientation-support.webp"
        ] },
        { "/services/3d-scanning", [
            "/src/images/services/scanning/scanning-hero.webp",
            "/src/images/services/scanning/cards/scanning-card-raw-mesh.webp",
            "/src/images/services/scanning/cards/scanning-card-reverse-engineering.webp",
            "/src/images/services/scanning/cards/scanning-card-deviation-analysis.webp",
            "/src/images/services/scanning/scanning-workflow.webp",
            "/src/images/services/scanning/cards/scanning-card-site-access.webp",
            "/src/images/services/scanning/cards/scanning-card-environment.webp",
            "/src/images/services/scanning/cards/scanning-card-previsit-planning.webp",
            "/src/images/services/scanning/cards/scanning-card-define-outcome.webp",
            "/src/images/services/scanning/cards/scanning-card-clean-stabilize.webp",
            "/src/images/services/scanning/cards/scanning-card-difficult-surfaces.webp"
        ] }
    };

    [Theory]
    [MemberData(nameof(OriginalImages))]
    public async Task ServiceDocuments_PreserveOriginalImageMembershipAcrossRouteOwners(string route, string[] expected)
    {
        foreach (var active in new[] { true, false })
        {
            await using var host = factory.WithWebHostBuilder(
                builder => builder.UseSetting("BlazorRouting:Services", active.ToString()));
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            foreach (var culture in new[] { "en", "th" })
            {
                string html = await GetHtml(client, route, culture);
                var sources = Images(html).Select(tag => Attribute(tag, "src")).ToHashSet(StringComparer.Ordinal);
                foreach (string image in expected)
                {
                    Assert.Contains(image, sources);
                }
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task PrintingDocuments_PreserveThreeProcessAndThreeFilePreparationVisuals(string culture)
    {
        string[] process = ["printing-fdm-application", "printing-resin-application", "printing-industrial-application"];
        string[] preparation = ["printing-file-manifold-check", "printing-file-wall-clearance", "printing-file-orientation-support"];
        foreach (var active in new[] { true, false })
        {
            await using var host = factory.WithWebHostBuilder(
                builder => builder.UseSetting("BlazorRouting:Services", active.ToString()));
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            string html = await GetHtml(client, "/services/3d-printing", culture);
            AssertVisualSection(html, "printing-processes", process);
            AssertVisualSection(html, "printing-file-guide", preparation);
            foreach (string name in process.Concat(preparation))
            {
                using var asset = await client.GetAsync($"/src/images/services/printing/{name}.webp");
                asset.EnsureSuccessStatusCode();
                Assert.Equal("image/webp", asset.Content.Headers.ContentType?.MediaType);
                byte[] bytes = await asset.Content.ReadAsByteArrayAsync();
                Assert.True(bytes.Length > 10000, $"{name} lost the original asset-size floor.");
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            }
        }
    }

    private static void AssertVisualSection(string html, string id, string[] expected)
    {
        var match = Assert.Single(Regex.Matches(
            html,
            $"<section\\b(?=[^>]*\\bid=\\\"{Regex.Escape(id)}\\\")[^>]*>(?<body>.*?)</section>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase).Cast<Match>());
        string section = match.Groups["body"].Value;
        Assert.False(Regex.IsMatch(section, "<i\\s", RegexOptions.IgnoreCase), $"{id} substituted icons for the source visuals.");
        string[] images = Images(section).ToArray();
        Assert.Equal(3, images.Length);
        Assert.Equal(
            expected.Select(name => $"/src/images/services/printing/{name}.webp").Order(StringComparer.Ordinal),
            images.Select(tag => Attribute(tag, "src")).Order(StringComparer.Ordinal));
        foreach (string image in images)
        {
            Assert.Equal("lazy", Attribute(image, "loading"));
            Assert.Equal("async", Attribute(image, "decoding"));
            Assert.Equal("1536", Attribute(image, "width"));
            Assert.Equal("1024", Attribute(image, "height"));
            Assert.False(string.IsNullOrWhiteSpace(Attribute(image, "alt")));
        }
    }

    private static async Task<string> GetHtml(HttpClient client, string route, string culture)
    {
        using var response = await client.GetAsync($"{route}?culture={culture}&tracking=excluded");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync();
        string root = Assert.Single(Regex.Matches(html, "<html\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>()).Value;
        Assert.Equal(culture, Attribute(root, "lang"));
        return html;
    }

    private static IEnumerable<string> Images(string html) => Regex.Matches(html, "<img\\b[^>]*>", RegexOptions.IgnoreCase)
        .Cast<Match>().Select(match => match.Value);

    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(
        Regex.Match(tag, $"\\b{Regex.Escape(name)}\\s*=\\s*[\\\"'](?<value>[^\\\"']*)[\\\"']", RegexOptions.IgnoreCase)
            .Groups["value"].Value);
}
