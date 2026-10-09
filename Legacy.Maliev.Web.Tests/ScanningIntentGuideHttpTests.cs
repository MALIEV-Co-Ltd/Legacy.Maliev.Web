using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Legacy.Maliev.Web.Tests;

public sealed class ScanningIntentGuideHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task ScanningPage_RendersOriginalIntentChoicesAndReachableDeliverables(string culture, bool staticSsr)
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
        var section = Regex.Match(html, "<section id=\"scanning-why\"[^>]*>(.*?)</section>", RegexOptions.Singleline);
        Assert.True(section.Success, "The visible scanning intent guide is missing.");
        Assert.Contains("aria-labelledby=\"scanning-why-title\"", section.Value, StringComparison.Ordinal);
        Assert.Contains("id=\"scanning-why-title\"", section.Value, StringComparison.Ordinal);
        var headings = Regex.Matches(section.Value, "<h3>(.*?)</h3>", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(culture == "th"
            ? new[] { "เก็บรูปทรง", "สร้างแบบที่ใช้งานต่อได้", "ตรวจเทียบกับแบบอ้างอิง" }
            : new[] { "Keep the shape", "Rebuild a usable design", "Check against a reference" }, headings);
        Assert.Contains(culture == "th"
            ? "เก็บเมชเพื่อแสดงผล เก็บถาวร หรือประมวลผลต่อเอง"
            : "Capture a mesh for visualization, archiving, or your own processing.", section.Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "สร้าง CAD ที่แก้ไขได้จากพื้นผิวที่วัด"
            : "Reconstruct editable CAD from measured surfaces", section.Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "จัดแนวข้อมูลสแกนกับ CAD ต้นแบบที่ใช้ได้"
            : "Align scan data with usable nominal CAD", section.Value, StringComparison.Ordinal);
        Assert.Contains("href=\"#scanning-deliverables\"", section.Value, StringComparison.Ordinal);
        Assert.Contains("id=\"scanning-deliverables\"", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("id=\"scanning-why\"", StringComparison.Ordinal)
            < html.IndexOf("id=\"scanning-sample\"", StringComparison.Ordinal));
        Assert.DoesNotContain(culture == "th" ? "Why scan a physical part?" : "ทำไมต้องสแกน 3D ชิ้นงานจริง?", section.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", html, StringComparison.OrdinalIgnoreCase);
    }
}
