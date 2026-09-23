using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class ScanningPresentationContractTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly TestingWebApplicationFactory factory;

    public ScanningPresentationContractTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("en", "What do the outputs of 3D scanning look like?", "Scan", "Before your scan", "Show us the part")]
    [InlineData("th", "ผลลัพธ์จากการสแกน 3D มีหน้าตาอย่างไร?", "สแกน", "เตรียมตัวก่อนสแกน", "ส่งรูปชิ้นงานให้เราดู")]
    public async Task ScanningRoute_RendersAccessibleComparisonAndThreePreparationNotes(
        string culture,
        string comparisonHeading,
        string scanLabel,
        string preparationHeading,
        string firstPreparationNote)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/services/3d-scanning?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        response.EnsureSuccessStatusCode();
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
        Assert.Contains($"<h2 id=\"scanning-sample-title\">{comparisonHeading}</h2>", html, StringComparison.Ordinal);
        Assert.Contains("data-scanning-comparison data-proof=\"cad\" data-mode=\"side\"", html, StringComparison.Ordinal);
        Assert.Contains("data-scanning-comparison data-proof=\"color\" data-mode=\"side\"", html, StringComparison.Ordinal);
        Assert.Equal(8, ComparisonButtonRegex().Count(html));
        Assert.Contains("role=\"slider\"", html, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-valuemin=\"0\" aria-valuemax=\"100\" aria-valuenow=\"50\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"scanning-cad-help\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"scanning-color-help\"", html, StringComparison.Ordinal);
        Assert.Contains($">{scanLabel}</figcaption>", html, StringComparison.Ordinal);
        Assert.Equal(4, ComparisonImageRegex().Count(html));
        Assert.Equal(3, ProofTabRegex().Count(html));
        Assert.Contains("id=\"scanning-report-panel\"", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "Gaussian splat (.PLY)" : "Gaussian splat point cloud (.PLY)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("input type=\"range\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<canvas", html, StringComparison.OrdinalIgnoreCase);
        var preparation = ExtractSection(html, "scanning-onsite-checklist");
        Assert.Contains($"<h2>{preparationHeading}</h2>", preparation, StringComparison.Ordinal);
        Assert.Equal(3, PreparationNoteRegex().Count(preparation));
        Assert.Contains($"<h3>{firstPreparationNote}</h3>", preparation, StringComparison.Ordinal);
        Assert.Contains("<details class=\"scanning-preparation-details\">", preparation, StringComparison.Ordinal);
        Assert.Contains("onclick=\"window.print()\"", preparation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RazorFallback_PreservesComparisonAndPreparationPresentation()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Services", "false"));
        using var client = fallbackFactory.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/services/3d-scanning?culture=en"));

        Assert.Contains("id=\"scanning-sample\"", html, StringComparison.Ordinal);
        Assert.Contains("data-scanning-comparison", html, StringComparison.Ordinal);
        Assert.Equal(3, PreparationNoteRegex().Count(ExtractSection(html, "scanning-onsite-checklist")));
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovedGreyEdgeAssets_AreExactAndRouteBundleOwnsComparisonBehavior()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cast-housing-cad-1500.webp"] = "5f52eb0e9745ae493bc7c7b8ff932b13c9c7ddfe55120952c36be732b3107563",
            ["cast-housing-cad-480.webp"] = "a207c8c85b478cb9d1acda13288bd02dbc74f6418bc9bea301e2dbdb6a26a20d",
            ["cast-housing-cad-900.webp"] = "06cbfbb0f5922d06596646848cfbd644dd54e74e9c10aec06650b068c613a722",
            ["cast-housing-scan-1500.webp"] = "b7b763abd8f3ce6247675a4aab1c5e8bbb67094084bc6e5aad08a0e7083f3ebc",
            ["cast-housing-scan-480.webp"] = "03bd9205d3f6cd3610d4899570568a2b26ab6297b8d755dd5409997f65192de9",
            ["cast-housing-scan-900.webp"] = "83ac222ea2ceb36e0f694b3a1f5c5a221f9e89b45fd2f27c7e7499faef321d92",
            ["deviation-report-cad.webp"] = "2f6992929c4eb92feffe22db01442104293a01918c7c15a23c071a8748a19bc0",
            ["deviation-report-map.webp"] = "4555606fa7d5c9c7aa9a13f6b4316385a4a3d73dd6d856a2b658694c16c19121",
            ["deviation-report-mesh.webp"] = "8e4df62611e0d90691ca46243cf20afee095cd8ee54f9b456c7ed03c3935e1e9",
            ["statue-color-scan-1500.webp"] = "d5b73e8bdcab120fbd27dec7a77d1135239a2b6624a2d6145971b002b7490e15",
            ["statue-color-scan-480.webp"] = "524727f9798304038a844d1832c8cddc71017994e7353f9c2e4c5ddfe419b763",
            ["statue-color-scan-900.webp"] = "0f81cb54e94be1da628ef1f64f2739df6325982bc3eb998671a5a68c674fd3a2",
            ["statue-raw-scan-1500.webp"] = "119cc1765dbc588c2cfdac7468bd721fe3d734b6bea729e17bd7b1422cf1fecc",
            ["statue-raw-scan-480.webp"] = "624a914072c78c1e0f8f557a53a62904b65be0056343ffa67150e07e155f2035",
            ["statue-raw-scan-900.webp"] = "26ae6311f5f3c31d36439fed62edc211496ca89d58d1102529776b1052c6ee0c",
        };
        var proofDirectory = Path.Combine(web, "wwwroot", "src", "images", "services", "scanning", "proof");

        Assert.Equal(expected.Keys.Order(), Directory.GetFiles(proofDirectory, "*.webp").Select(Path.GetFileName).Order());
        foreach (var asset in expected)
        {
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(proofDirectory, asset.Key)))).ToLowerInvariant();
            Assert.Equal(asset.Value, digest);
        }

        Assert.Contains("scanning-comparison.js", File.ReadAllText(Path.Combine(web, "assets", "route-service-scanning.js")), StringComparison.Ordinal);
        Assert.Contains("scanning-proof-tabs.js", File.ReadAllText(Path.Combine(web, "assets", "route-service-scanning.js")), StringComparison.Ordinal);
        Assert.Contains("scanning-comparison.css", File.ReadAllText(Path.Combine(web, "assets", "route-services.css")), StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static string ExtractSection(string html, string id)
    {
        var start = html.IndexOf($"<section id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected section '{id}'.");
        var end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected section '{id}' to close.");
        return html[start..(end + "</section>".Length)];
    }

    [GeneratedRegex("data-comparison-mode=\"(?:compare|scan|cad|raw|color|side)\"")]
    private static partial Regex ComparisonButtonRegex();

    [GeneratedRegex("class=\"scanning-comparison-(?:scan|cad|raw|color)\"[\\s\\S]*?<img ")]
    private static partial Regex ComparisonImageRegex();

    [GeneratedRegex("role=\"tab\" id=\"scanning-tab-(?:cad|report|color)\"")]
    private static partial Regex ProofTabRegex();

    [GeneratedRegex("<li><h3>[^<]+</h3><p>[^<]+</p></li>")]
    private static partial Regex PreparationNoteRegex();
}
