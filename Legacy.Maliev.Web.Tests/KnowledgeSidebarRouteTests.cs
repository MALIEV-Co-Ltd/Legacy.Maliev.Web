using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class KnowledgeSidebarRouteTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    public static TheoryData<string, string, bool> RouteCases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();
            foreach (var route in KnowledgeSidebarRoutes.All)
            {
                foreach (var culture in new[] { "en", "th" })
                {
                    foreach (var retainedRazor in new[] { false, true })
                    {
                        cases.Add(route.Path, culture, retainedRazor);
                    }
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(RouteCases))]
    public async Task BothRenderersKeepLocalizedOverviewAndSpecificationLinksInsideSidebar(
        string route, string culture, bool retainedRazor)
    {
        var feature = KnowledgeSidebarRoutes.All.Single(candidate => candidate.Path == route).Feature;
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(feature, retainedRazor ? "false" : "true"));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync($"{route}?culture={culture}");
        var document = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\"", document, StringComparison.Ordinal);
        Assert.Equal(!retainedRazor,
            document.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var sidebar = Assert.Single(SidebarRegex().Matches(document).Cast<Match>()).Groups["body"].Value;
        Assert.Equal(5, Regex.Matches(sidebar, """<details\b""", RegexOptions.CultureInvariant).Count);
        Assert.Equal(19, Regex.Matches(sidebar, "class=\"workspace-nav-link\"", RegexOptions.CultureInvariant).Count);
        Assert.Contains($">{(culture == "th" ? "เริ่มต้นที่นี่" : "Start here")}<", sidebar, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "พบ {0} รายการ" : "Matches: {0}", sidebar, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "แสดง {0} จาก {1} รายการ ลองระบุคำค้นให้เฉพาะเจาะจงขึ้น" : "Showing {0} of {1} matches. Narrow your search to see more.", sidebar, StringComparison.Ordinal);
        Assert.Contains("data-knowledge-search hidden", sidebar, StringComparison.Ordinal);
        AssertLink(sidebar, "/knowledges", culture == "th" ? "ศูนย์ความรู้" : "Knowledge center");
        AssertLink(sidebar, "/knowledges/specifications", culture == "th" ? "เตรียมงานสำหรับแต่ละบริการ" : "Service preparation guides");
        Assert.Equal(1, Regex.Matches(sidebar, """<details\b[^>]*\bopen(?:\s|=|>)""", RegexOptions.CultureInvariant).Count);
        Assert.Equal(1, Regex.Matches(sidebar, "aria-current=\"page\"", RegexOptions.CultureInvariant).Count);
        var current = Regex.Match(sidebar, "<a\\b(?=[^>]*aria-current=\"page\")[^>]*href=\"(?<href>[^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(current.Success);
        Assert.Equal(route, current.Groups["href"].Value.Split('?')[0], ignoreCase: true);
        Assert.All(LinkRegex().Matches(sidebar).Cast<Match>(), link =>
            Assert.Contains($"?culture={culture}", link.Groups["href"].Value, StringComparison.Ordinal));
        Assert.DoesNotContain("Lorem ipsum", sidebar, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertLink(string sidebar, string destination, string label)
    {
        var link = Assert.Single(LinkRegex().Matches(sidebar).Cast<Match>(), match =>
            string.Equals(match.Groups["href"].Value.Split('?')[0].TrimEnd('/'), destination, StringComparison.OrdinalIgnoreCase));
        Assert.Contains($">{label}<", link.Groups["body"].Value, StringComparison.Ordinal);
    }

    [GeneratedRegex("<aside\\b(?=[^>]*\\bid=\"knowledge-navigation\")[^>]*>(?<body>.*?)</aside>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SidebarRegex();

    [GeneratedRegex("<a\\b[^>]*\\bhref=\"(?<href>[^\"]+)\"[^>]*>(?<body>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();
}

internal static class KnowledgeSidebarRoutes
{
    internal static readonly (string Path, string Feature)[] All =
    [
        ("/knowledges", "BlazorRouting:KnowledgesIndex"),
        ("/knowledges/guidelines", "BlazorRouting:KnowledgesGuidelines"),
        ("/knowledges/workflow", "BlazorRouting:KnowledgesWorkflow"),
        ("/knowledges/specifications", "BlazorRouting:KnowledgesSpecifications"),
        ("/knowledges/specifications/3d-printing", "BlazorRouting:KnowledgesSpecifications3DPrinting"),
        ("/knowledges/specifications/3d-scanning", "BlazorRouting:KnowledgesSpecifications3DScanning")
    ];
}
