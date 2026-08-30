using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class LowVolumeInjectionMoldingParityTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public LowVolumeInjectionMoldingParityTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LowVolumeInjectionRoute_RendersTheCurrentDecisionAndSafetyGuidance()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/services/low-volume-injection-molding?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"injection-cost-benefits\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-fit\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-materials\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-gallery\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-assets-title\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-workflow\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-volume-title\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-faq\"", source, StringComparison.Ordinal);
        Assert.Contains("Lower tooling cost while you validate the part", source, StringComparison.Ordinal);
        Assert.Contains("Use the service for up to 1,000 parts; own the machine for larger runs", source, StringComparison.Ordinal);
        Assert.Contains("Manufacturer guides cite melt settings around 205–220 °C", source, StringComparison.Ordinal);
        Assert.Contains("hazardous formaldehyde-containing fumes", source, StringComparison.Ordinal);
        Assert.Contains("injection-hero", source, StringComparison.Ordinal);
        Assert.Contains("injection-quick-grid", source, StringComparison.Ordinal);
        Assert.Contains("injection-benefit-grid", source, StringComparison.Ordinal);
        Assert.Contains("injection-card-grid", source, StringComparison.Ordinal);
        Assert.Contains("id=\"injection-part-proof\"", source, StringComparison.Ordinal);
        Assert.Contains("Actual MALIEV-produced part", source, StringComparison.Ordinal);
        Assert.Contains("part-bento-pp-source-derived.webp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ai-enhanced", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("injection-volume-grid", source, StringComparison.Ordinal);
        Assert.Equal(3, ServiceCardMediaRegex().Matches(source).Count);
        Assert.Equal(7, FaqDetailsRegex().Matches(source).Count);
        Assert.Equal(7, FaqSchemaQuestionRegex().Matches(source).Count);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LowVolumeInjectionSource_PreservesMachineAssetsAndExplicitPOMSafetyNote()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var component = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "LowVolumeInjectionMoldingContent.razor"));
        var styles = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "service-pages.css"));

        Assert.Contains("service-page-toc", component, StringComparison.Ordinal);
        Assert.Contains("service-pricing-section", component, StringComparison.Ordinal);
        Assert.Contains("role=\"note\"", component, StringComparison.Ordinal);
        Assert.Contains("purge/shutdown procedures", component, StringComparison.Ordinal);
        Assert.Contains("local exhaust ventilation", component, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", component, StringComparison.Ordinal);
        Assert.Matches(
            "@media \\(max-width: 47\\.99rem\\) \\{[\\s\\S]*?#injection-part-proof \\.service-part-bento \\{ grid-template-columns: 1fr; \\}[\\s\\S]*?#injection-part-proof \\.service-part-bento-card--feature, #injection-part-proof \\.service-part-bento-card--process, #injection-part-proof \\.service-part-bento-card--guidance, #injection-part-proof \\.service-part-bento-card--cta \\{ grid-column: auto; min-height: 0; \\}",
            styles);

        foreach (var relativePath in new[]
        {
            "wwwroot/src/images/services/injection-molding/injection-service-hero-wide.webp",
            "wwwroot/src/images/services/injection-molding/pp-injection-molded-component.webp",
            "wwwroot/src/images/services/injection-molding/part-bento-pp-source-derived-640.webp",
            "wwwroot/src/images/services/injection-molding/part-bento-pp-source-derived-1024.webp",
            "wwwroot/src/images/services/injection-molding/part-bento-pp-source-derived.webp",
            "wwwroot/src/images/services/injection-molding/pimm-50g-controller.webp",
            "wwwroot/src/images/services/injection-molding/pimm-50g-nozzle.webp",
            "wwwroot/src/images/services/injection-molding/pimm-sample-mold.webp"
        })
        {
            var path = Path.Combine(root, "Legacy.Maliev.Web", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Expected injection asset '{path}'.");
            Assert.True(new FileInfo(path).Length > 0, $"Expected injection asset '{path}' to be non-empty.");
        }
    }

    [Theory]
    [InlineData("en", "Actual MALIEV-produced part", "Request Injection Review")]
    [InlineData("th", "ชิ้นงานจริงที่ MALIEV ผลิต", "ขอประเมินงานฉีด")]
    public async Task LowVolumeInjectionRoute_RendersLocalizedSourceDerivedPartProof(
        string culture,
        string provenanceLabel,
        string actionLabel)
    {
        using var client = factory.CreateClient();
        var source = WebUtility.HtmlDecode(await client.GetStringAsync($"/services/low-volume-injection-molding?culture={culture}"));

        Assert.Contains("id=\"injection-part-proof\"", source, StringComparison.Ordinal);
        Assert.Contains("data-part-proof=\"source-derived\"", source, StringComparison.Ordinal);
        Assert.Contains("part-bento-pp-source-derived-640.webp 640w", source, StringComparison.Ordinal);
        Assert.Contains("part-bento-pp-source-derived-1024.webp 1024w", source, StringComparison.Ordinal);
        Assert.Contains("part-bento-pp-source-derived.webp 1536w", source, StringComparison.Ordinal);
        Assert.Contains("sizes=\"(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 58vw\"", source, StringComparison.Ordinal);
        Assert.Contains("width=\"1536\" height=\"1025\" loading=\"lazy\" decoding=\"async\"", source, StringComparison.Ordinal);
        Assert.Contains(provenanceLabel, source, StringComparison.Ordinal);
        Assert.Contains(actionLabel, source, StringComparison.Ordinal);
        Assert.DoesNotContain("ai-enhanced", source, StringComparison.OrdinalIgnoreCase);
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

    [GeneratedRegex("class=\"service-card-media\"", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceCardMediaRegex();

    [GeneratedRegex("<details>", RegexOptions.CultureInvariant)]
    private static partial Regex FaqDetailsRegex();

    [GeneratedRegex("\\\"@type\\\":\\\"Question\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex FaqSchemaQuestionRegex();
}
