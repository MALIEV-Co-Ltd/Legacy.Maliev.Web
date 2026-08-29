using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class SiliconeCastingParityTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public SiliconeCastingParityTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SiliconeCastingRoute_RendersTheCurrentMoldPricingAndPerformanceGuidance()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/services/silicone-casting?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"silicone-visuals\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"silicone-pricing\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"silicone-performance\"", source, StringComparison.Ordinal);
        Assert.Contains("class=\"service-section service-section-light-cta\"", source, StringComparison.Ordinal);
        Assert.Contains("Choose the trade-off that fits the project", source, StringComparison.Ordinal);
        Assert.Contains("Silicone performance depends on the application", source, StringComparison.Ordinal);
        Assert.Equal(6, FaqDetailsRegex().Matches(source).Count);
        Assert.Equal(2, ServiceCardMediaRegex().Matches(source).Count);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SiliconeCastingSource_PreservesTheMoldVisualAssetsAndAccessibleSections()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var component = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "SiliconeCastingContent.razor"));

        Assert.Contains("service-page-toc", component, StringComparison.Ordinal);
        Assert.Contains("silicone-pricing", component, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"silicone-faq-title\"", component, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", component, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "wwwroot/src/images/services/silicone-casting/silicone-casting-workflow.webp",
            "wwwroot/src/images/services/silicone-casting/silicone-mold-prep.webp",
            "wwwroot/src/images/services/silicone-casting/silicone-pour-cure.webp"
        })
        {
            var path = Path.Combine(root, "Legacy.Maliev.Web", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Expected silicone asset '{path}'.");
            Assert.True(new FileInfo(path).Length > 0, $"Expected silicone asset '{path}' to be non-empty.");
        }
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

    [GeneratedRegex("class=\"service-card-media\"")]
    private static partial Regex ServiceCardMediaRegex();

    [GeneratedRegex("<details>", RegexOptions.CultureInvariant)]
    private static partial Regex FaqDetailsRegex();
}
