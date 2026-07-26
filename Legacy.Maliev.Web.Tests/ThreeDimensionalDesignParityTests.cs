using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class ThreeDimensionalDesignParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ThreeDimensionalDesignParityTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public async Task ThreeDimensionalDesignRoute_RendersTheCurrentReviewAndProductionGuidance()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/services/3d-design?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"design-workflow\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"design-visuals\"", source, StringComparison.Ordinal);
        Assert.Contains("class=\"service-section service-section-light-cta\"", source, StringComparison.Ordinal);
        Assert.Contains("Already have a process in mind?", source, StringComparison.Ordinal);
        Assert.Contains("What file formats can you deliver?", source, StringComparison.Ordinal);
        Assert.Contains("Can design changes during the project add cost?", source, StringComparison.Ordinal);
        Assert.Equal(2, DesignVisualRegex().Matches(source).Count);
        Assert.Equal(6, FaqDetailsRegex().Matches(source).Count);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDimensionalDesignSource_PreservesVisualAssetsAndAccessibleCards()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var component = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "ThreeDimensionalDesignContent.razor"));

        Assert.Contains("service-page-toc", component, StringComparison.Ordinal);
        Assert.Contains("service-split-image", component, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", component, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"design-faq-title\"", component, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "wwwroot/src/images/services/design/design-workflow.webp",
            "wwwroot/src/images/services/design/design-inputs.webp",
            "wwwroot/src/images/services/design/design-dfm-review.webp"
        })
        {
            var path = Path.Combine(root, "Legacy.Maliev.Web", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Expected design asset '{path}'.");
            Assert.True(new FileInfo(path).Length > 0, $"Expected design asset '{path}' to be non-empty.");
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
    private static partial Regex DesignVisualRegex();

    [GeneratedRegex("<details>", RegexOptions.CultureInvariant)]
    private static partial Regex FaqDetailsRegex();
}
