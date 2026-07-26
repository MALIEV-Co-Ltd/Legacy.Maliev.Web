using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class LowVolumeInjectionMoldingParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public LowVolumeInjectionMoldingParityTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
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
        Assert.Contains("injection-volume-grid", source, StringComparison.Ordinal);
        Assert.Equal(3, ServiceCardMediaRegex().Matches(source).Count);
        Assert.Equal(6, FaqDetailsRegex().Matches(source).Count);
        Assert.Equal(6, FaqSchemaQuestionRegex().Matches(source).Count);
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

        Assert.Contains("service-page-toc", component, StringComparison.Ordinal);
        Assert.Contains("service-pricing-section", component, StringComparison.Ordinal);
        Assert.Contains("role=\"note\"", component, StringComparison.Ordinal);
        Assert.Contains("purge/shutdown procedures", component, StringComparison.Ordinal);
        Assert.Contains("local exhaust ventilation", component, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", component, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "wwwroot/src/images/services/injection-molding/injection-service-hero-wide.png",
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
