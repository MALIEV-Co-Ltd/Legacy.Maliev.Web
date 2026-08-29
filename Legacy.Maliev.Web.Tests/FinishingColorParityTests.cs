using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class FinishingColorParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public FinishingColorParityTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void FinishingColorComponent_PreservesTheSourceMatcherAndLocalizedServiceContract()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var page = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Services", "FinishingAndColorPage.razor"));
        var entry = File.ReadAllText(Path.Combine(web, "assets", "route-service-finishing.js"));
        var vendor = File.ReadAllText(Path.Combine(web, "assets", "vendor-entry.js"));
        var atlas = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "hlc-colour-atlas-data.js"));
        var core = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "finish-color-matcher-core.js"));
        var preview = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "finish-color-matcher-preview.js"));
        var matcher = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "finish-color-matcher.js"));

        Assert.Contains("@page \"/services/finishing-and-color\"", page, StringComparison.Ordinal);
        Assert.Contains("data-finish-color-matcher", page, StringComparison.Ordinal);
        Assert.Contains("data-sheen-canvas", page, StringComparison.Ordinal);
        Assert.Contains("data-guidance-topic", page, StringComparison.Ordinal);
        Assert.Contains("Mesh Splitter", page, StringComparison.Ordinal);
        Assert.Contains("HLC Colour Atlas v2.03", page, StringComparison.Ordinal);
        Assert.Contains("<ServicePageToc />", page, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", page, StringComparison.Ordinal);
        Assert.Contains("finish-color-matcher-core.js", entry, StringComparison.Ordinal);
        Assert.Contains("finish-color-matcher-preview.js", entry, StringComparison.Ordinal);
        Assert.Contains("finish-color-matcher.js", entry, StringComparison.Ordinal);
        Assert.Contains("window.THREE = THREE", vendor, StringComparison.Ordinal);
        Assert.Contains("window.MalievHlcColourAtlas", atlas, StringComparison.Ordinal);
        Assert.Contains("\"version\":\"2.03\"", atlas, StringComparison.Ordinal);
        Assert.Contains("\"publisher\":\"freieFarbe e.V.\"", atlas, StringComparison.Ordinal);
        Assert.Contains("deltaE00", core, StringComparison.Ordinal);
        Assert.Contains("materialSettings", preview, StringComparison.Ordinal);
        Assert.Contains("data-color-results", matcher, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FinishingColorRoute_RendersStaticSsrWithSeoAndMatcherMarkup()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/services/finishing-and-color?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-finish-color-matcher", source, StringComparison.Ordinal);
        Assert.Contains("HLC Colour Atlas", source, StringComparison.Ordinal);
        Assert.Contains("data-service-toc", source, StringComparison.Ordinal);
        Assert.Contains("Finishing &amp; Colour Standards", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-service-structured-data\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceVisibleFinishingCopyRemainsExact()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "FinishingAndColorPage.razor"));

        Assert.Contains("A photo or screen is affected by lighting and display settings.", page, StringComparison.Ordinal);
        Assert.Contains("ภาพถ่ายหรือหน้าจอได้รับผลจากแสงและการแสดงผล", page, StringComparison.Ordinal);
        Assert.Contains("Confirm the sheen before painting begins.", page, StringComparison.Ordinal);
        Assert.Contains("Yes. We can quote split planning, joining, filler, sanding, primer, paint, and clear coat together.", page, StringComparison.Ordinal);
        Assert.Contains("We can reduce a seam with joining, automotive putty or wood filler", page, StringComparison.Ordinal);
        Assert.Contains("a different color, sheen, or clear-coat type after approval", page, StringComparison.Ordinal);
        Assert.Contains("split and joining plan, final dimensions, quantity, use environment", page, StringComparison.Ordinal);
        Assert.Contains("แผนแบ่งและต่อชิ้นงาน ขนาดสุดท้าย จำนวน สภาพการใช้งาน", page, StringComparison.Ordinal);
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
