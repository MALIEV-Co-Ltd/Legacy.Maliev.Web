using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class AboutPageContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] EvidenceUrls =
    [
        "https://www.dataforthai.com/company/0125561001573/",
        "https://www.print3dd.com/maliev-co-ltd/",
        "https://waa.inter.nstda.or.th/stks/pub/2022/20220613-procurement-winner-co-2565-q02.pdf",
        "https://fprocurement.egat.co.th/storage/documents/dprocurement/result/domestic_july2024.pdf",
        "https://shop.maliev.com/blogs/news",
        "https://shop.maliev.com/collections/sim-racing-solutions",
    ];

    private readonly HttpClient client;

    public AboutPageContractTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
    }

    [Fact]
    public void AboutComponent_SourceContainsResearchedSemanticRecord()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "About", "AboutContent.razor"));

        Assert.Contains("class=\"about-page\"", source, StringComparison.Ordinal);
        Assert.Contains("<ol class=\"about-records\"", source, StringComparison.Ordinal);
        Assert.Equal(6, Regex.Matches(source, "class=\"about-record(?:\\s|\")").Count);
        Assert.Contains("href=\"/Quotation\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services\"", source, StringComparison.Ordinal);
        Assert.All(EvidenceUrls, url => Assert.Contains(url, source, StringComparison.Ordinal));
        Assert.DoesNotContain("direction-l", source, StringComparison.Ordinal);
        Assert.DoesNotContain("direction-r", source, StringComparison.Ordinal);
        Assert.DoesNotContain("first in Thailand", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machine learning", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wow", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.Ordinal);
        Assert.DoesNotContain("facebook", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AboutPage_UsesDedicatedStaticStylesWithoutMotionOrFontOverrides()
    {
        var root = FindRepositoryRoot();
        var entry = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "assets", "site-entry.css"));
        var stylesPath = Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "about-page.css");

        Assert.True(File.Exists(stylesPath), "The About page stylesheet must exist.");
        var styles = File.ReadAllText(stylesPath);
        Assert.Contains("about-page.css", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("timeline.css", entry, StringComparison.Ordinal);
        Assert.Contains(".about-page", styles, StringComparison.Ordinal);
        Assert.Contains(".about-record", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("animation:", styles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("font-family:", styles, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "From a family workshop to connected manufacturing.", "The manufacturing record", "About MALIEV | Connected manufacturing in Thailand")]
    [InlineData("th", "จากเวิร์กช็อปของครอบครัว สู่ระบบการผลิตที่เชื่อมต่อกัน", "บันทึกเส้นทางการผลิต", "เกี่ยวกับ MALIEV | ระบบการผลิตที่เชื่อมต่อกันในประเทศไทย")]
    public async Task AboutPage_RendersLocalizedChronologyMetadataAndPreservesContracts(
        string culture,
        string hero,
        string recordHeading,
        string title)
    {
        using var response = await client.GetAsync($"/about?culture={culture}");
        var source = await response.Content.ReadAsStringAsync();
        var content = WebUtility.HtmlDecode(source);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Regex.Matches(content, "<h1(?:\\s|>)", RegexOptions.IgnoreCase));
        Assert.Contains(hero, content, StringComparison.Ordinal);
        Assert.Contains(recordHeading, content, StringComparison.Ordinal);
        Assert.Contains("class=\"about-records\"", content, StringComparison.Ordinal);
        Assert.Contains("href=\"/Quotation\"", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/Services\"", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<title>{title}</title>", content, StringComparison.Ordinal);
        Assert.Contains($"property=\"og:title\" content=\"{title}\"", content, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\"", source, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\"", source, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"th\"", source, StringComparison.Ordinal);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
        Assert.DoesNotContain("https://www.facebook.com", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connect.facebook.net", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fb:app_id", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.server.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
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
}
