using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Legacy.Maliev.Web.Tests;

public sealed class ScanningWorkflowSourceCommitHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task OriginalRedesign_PreservesOrderedStagesAndDistinctOutputLimits(string culture, bool staticSsr)
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BlazorRouting:Services"] = staticSsr.ToString(),
            })));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        using var response = await client.GetAsync($"/services/3d-scanning?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var start = html.IndexOf("<section id=\"scanning-selection-guide\"", StringComparison.Ordinal);
        var end = html.IndexOf("<section id=\"scanning-deliverables\"", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The workflow must precede the deliverable guide.");
        var workflow = html[start..end];
        var stages = Regex.Matches(workflow, "<li class=\"scanning-workflow-step\"[^>]*>(.*?)</li>", RegexOptions.Singleline);
        Assert.Equal(5, stages.Count);
        var expectedHeadings = culture == "th"
            ? new[] { "จัดเตรียมและเก็บข้อมูลชิ้นงาน", "Raw Scan / ข้อมูลดิบ", "Cleaned Mesh / เมชสะอาด", "Reverse Engineering / CAD", "Deviation Analysis / วิเคราะห์ความคลาดเคลื่อน" }
            : new[] { "Set up and capture the object", "Raw Scan / Raw Data", "Cleaned Mesh", "Reverse Engineering / CAD", "Deviation Analysis" };
        Assert.Equal(expectedHeadings, stages.Select(stage => Regex.Match(stage.Value, "<h3>(.*?)</h3>").Groups[1].Value));
        for (var index = 0; index < stages.Count; index++)
        {
            Assert.Contains($"aria-hidden=\"true\">{index + 1}</span>", stages[index].Value, StringComparison.Ordinal);
            Assert.Contains("class=\"scanning-workflow-output\"", stages[index].Value, StringComparison.Ordinal);
        }

        Assert.Contains("STL", stages[1].Value, StringComparison.Ordinal);
        Assert.Contains("OBJ / PLY", stages[1].Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "อาจยังมีรู" : "May still contain holes", stages[1].Value, StringComparison.Ordinal);
        Assert.Contains("WATERTIGHT", stages[2].Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ไม่ใช่ CAD" : "not editable CAD", stages[2].Value, StringComparison.Ordinal);
        Assert.Contains("STEP / IGES", stages[3].Value, StringComparison.Ordinal);
        Assert.Contains("DWG*", stages[3].Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ต้องทบทวน" : "assumptions need review", stages[3].Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ต้องมี CAD ต้นแบบ" : "Requires usable nominal CAD", stages[4].Value, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(html, "<li class=\"scanning-deliverable-card(?: [^\"]*)?\"").Count);
        Assert.Contains(culture == "th" ? "DWG" : "Can I order only 3D scanning and receive a DWG?", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "DWG เป็นแบบเขียน 2D" : "DWG is a 2D drawing", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ไม่มีตัวเลขลดราคาแบบตายตัว" : "There is no fixed reduction", html, StringComparison.Ordinal);
        var limitationsStart = html.IndexOf("<section id=\"scanning-limitations\"", StringComparison.Ordinal);
        var limitationsEnd = html.IndexOf("<section id=\"scanning-guides\"", StringComparison.Ordinal);
        Assert.True(limitationsStart >= 0 && limitationsEnd > limitationsStart);
        var limitations = html[limitationsStart..limitationsEnd];
        Assert.Contains(culture == "th" ? "ผิวเงากระจก ใส หรือโปร่งแสง" : "Shiny, mirror-like, transparent, or translucent", limitations, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "เครื่องสแกนเก็บผิวที่มองไม่เห็นไม่ได้" : "cannot capture surfaces it cannot see", limitations, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "วัตถุนิ่ม ยืดหยุ่น เคลื่อนไหว หรือร้อน" : "flexible, moving, or hot objects", limitations, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "สเปรย์ผิวด้านล้างออก" : "removable matte spray", limitations, StringComparison.Ordinal);
        Assert.Contains("href=\"/Quotation?item=3D-Scanning\"", html, StringComparison.Ordinal);
        var images = Regex.Matches(html, "<img[^>]* src=\"([^\"]+)\"")
            .Select(image => image.Groups[1].Value).Where(src => src.Contains("/services/scanning/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(24, images.Length);
        Assert.Equal(images.Length, images.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("blazor.web.js", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("scanning-art-cad-reconstruction.webp", "0c0fd9e890d1f2687d942a136b06ec67f42b6ba07b3b73bb2d5295f42a08e059")]
    [InlineData("scanning-art-clean-mesh.webp", "4e2f72440eb1a69fc2abce2b087275f7d54ef739ad593afdc0c10b2a03db2554")]
    [InlineData("scanning-art-deviation-analysis.webp", "27d0a76c5a1423ab5412a8dd1f6d66ca645253277fa49886320a8d8c283d1ebd")]
    [InlineData("scanning-art-raw-capture.webp", "e1091ee5edfceee47f75de4be800bdb52743bb51ab908996e363a517d3178506")]
    public async Task OriginalArtwork_RemainsAvailableAsExactPublicBytes(string name, string sha256)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        using var response = await client.GetAsync($"/src/images/services/scanning/art/{name}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(await response.Content.ReadAsByteArrayAsync())).ToLowerInvariant());
    }
}
