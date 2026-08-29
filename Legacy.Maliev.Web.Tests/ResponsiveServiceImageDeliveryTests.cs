namespace Legacy.Maliev.Web.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class ResponsiveServiceImageDeliveryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public ResponsiveServiceImageDeliveryTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
    }

    [Theory]
    [InlineData("ThreeDimensionalPrintingPage.razor", "ThreeDimensionalPrintingContent.razor", "printing/printing-hero.webp", "printing/printing-hero", 1672)]
    [InlineData("CncMachiningPage.razor", "CncMachiningContent.razor", "cnc/cnc-hero.webp", "cnc/cnc-hero", 1672)]
    [InlineData("ThreeDimensionalScanningPage.razor", "ThreeDimensionalScanningContent.razor", "scanning/scanning-hero.webp", "scanning/scanning-hero", 1672)]
    [InlineData("CustomManufacturingPage.razor", "CustomManufacturingContent.razor", "custom-manufacturing/custom-manufacturing-story.webp", "custom-manufacturing/custom-manufacturing-story", 1536)]
    [InlineData("ThreeDimensionalDesignPage.razor", "ThreeDimensionalDesignContent.razor", "design/design-workflow.webp", "design/design-workflow", 1536)]
    [InlineData("SiliconeCastingPage.razor", "SiliconeCastingContent.razor", "silicone-casting/silicone-casting-workflow.webp", "silicone-casting/silicone-casting-workflow", 1536)]
    [InlineData("FinishingAndColorPage.razor", "FinishingAndColorPage.razor", "printing/printing-finish-color-approval.webp", "printing/printing-finish-color-approval", 1536)]
    [InlineData("LowVolumeInjectionMoldingPage.razor", "LowVolumeInjectionMoldingContent.razor", "injection-molding/injection-service-hero-wide.png", "injection-molding/injection-service-hero-wide", 2172)]
    public void ServiceHero_ProvidesResponsivePreloadAndImageSources(
        string pageName,
        string contentName,
        string originalImage,
        string responsiveStem,
        int sourceWidth)
    {
        var components = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Components", "Pages", "Services");
        var page = File.ReadAllText(Path.Combine(components, pageName));
        var content = File.ReadAllText(Path.Combine(components, contentName));
        var responsiveSet = $"/src/images/services/{responsiveStem}-640.webp 640w, /src/images/services/{responsiveStem}-960.webp 960w, /src/images/services/{originalImage} {sourceWidth}w";

        Assert.Contains($"href=\"/src/images/services/{originalImage}\"", page, StringComparison.Ordinal);
        Assert.Contains($"imagesrcset=\"{responsiveSet}\"", page, StringComparison.Ordinal);
        Assert.Contains("imagesizes=\"100vw\"", page, StringComparison.Ordinal);
        Assert.Contains($"srcset=\"{responsiveSet}\"", content, StringComparison.Ordinal);
        Assert.Contains("sizes=\"100vw\"", content, StringComparison.Ordinal);

        var imageRoot = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "src", "images", "services");
        var originalPath = Path.Combine(imageRoot, originalImage.Replace('/', Path.DirectorySeparatorChar));
        foreach (var width in new[] { "640", "960" })
        {
            var variantPath = Path.Combine(
                imageRoot,
                $"{responsiveStem.Replace('/', Path.DirectorySeparatorChar)}-{width}.webp");
            Assert.True(File.Exists(variantPath), $"Missing responsive image: {variantPath}");
            Assert.True(new FileInfo(variantPath).Length < new FileInfo(originalPath).Length);
            var signature = File.ReadAllBytes(variantPath);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(signature, 0, 4));
        }
    }

    [Fact]
    public void PrintingApplicationCards_ProvideResponsiveImageSources()
    {
        var content = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Services",
            "ThreeDimensionalPrintingContent.razor"));

        foreach (var image in new[]
        {
            "printing-fdm-application",
            "printing-resin-application",
            "printing-industrial-application",
        })
        {
            Assert.Contains(
                $"srcset=\"/src/images/services/printing/{image}-640.webp 640w, /src/images/services/printing/{image}-960.webp 960w, /src/images/services/printing/{image}.webp 1536w\"",
                content,
                StringComparison.Ordinal);
            Assert.Contains("sizes=\"(max-width: 767px) 100vw, 33vw\"", content, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("/services/3d-printing", "printing/printing-hero")]
    [InlineData("/services/cnc-machining", "cnc/cnc-hero")]
    [InlineData("/services/3d-scanning", "scanning/scanning-hero")]
    [InlineData("/services/custom-manufacturing", "custom-manufacturing/custom-manufacturing-story")]
    [InlineData("/services/3d-design", "design/design-workflow")]
    [InlineData("/services/silicone-casting", "silicone-casting/silicone-casting-workflow")]
    [InlineData("/services/finishing-and-color", "printing/printing-finish-color-approval")]
    [InlineData("/services/low-volume-injection-molding", "injection-molding/injection-service-hero-wide")]
    public async Task ServiceRoute_RendersResponsiveHeroSources(string route, string responsiveStem)
    {
        using var response = await client.GetAsync($"{route}?culture=en&tracking=excluded");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains($"/src/images/services/{responsiveStem}-640.webp 640w", html, StringComparison.Ordinal);
        Assert.Contains($"/src/images/services/{responsiveStem}-960.webp 960w", html, StringComparison.Ordinal);
        Assert.Contains("sizes=\"100vw\"", html, StringComparison.Ordinal);
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
