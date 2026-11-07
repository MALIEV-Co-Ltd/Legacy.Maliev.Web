namespace Legacy.Maliev.Web.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class ResponsiveServiceImageDeliveryTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly HttpClient client;

    public ResponsiveServiceImageDeliveryTests(TestingWebApplicationFactory factory)
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    [Theory]
    [InlineData("ThreeDimensionalPrintingPage.razor", "ThreeDimensionalPrintingContent.razor", "printing/printing-hero-prototypes-transparent.webp", "printing/printing-hero-prototypes-transparent", 1536)]
    [InlineData("CncMachiningPage.razor", "CncMachiningContent.razor", "hero-illustrations/cnc-hero-light.webp", "hero-illustrations/cnc-hero-light", 1536)]
    [InlineData("ThreeDimensionalScanningPage.razor", "ThreeDimensionalScanningContent.razor", "hero-illustrations/scanning-hero-raptor-x-unbranded.webp", "hero-illustrations/scanning-hero-raptor-x-unbranded", 1536)]
    [InlineData("CustomManufacturingPage.razor", "CustomManufacturingContent.razor", "hero-illustrations/custom-hero-light.webp", "hero-illustrations/custom-hero-light", 1536)]
    [InlineData("ThreeDimensionalDesignPage.razor", "ThreeDimensionalDesignContent.razor", "hero-illustrations/design-hero-light.webp", "hero-illustrations/design-hero-light", 1774)]
    [InlineData("SiliconeCastingPage.razor", "SiliconeCastingContent.razor", "hero-illustrations/silicone-hero-light.webp", "hero-illustrations/silicone-hero-light", 1536)]
    [InlineData("FinishingAndColorPage.razor", "FinishingAndColorPage.razor", "hero-illustrations/finishing-hero-light.webp", "hero-illustrations/finishing-hero-light", 1774)]
    [InlineData("LowVolumeInjectionMoldingPage.razor", "LowVolumeInjectionMoldingContent.razor", "hero-illustrations/injection-hero-light.webp", "hero-illustrations/injection-hero-light", 1536)]
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
        var responsiveSet = $"/src/images/services/{responsiveStem}-640.webp 640w, /src/images/services/{responsiveStem}-768.webp 768w, /src/images/services/{responsiveStem}-960.webp 960w, /src/images/services/{originalImage} {sourceWidth}w";

        Assert.Contains($"href=\"/src/images/services/{originalImage}\"", page, StringComparison.Ordinal);
        Assert.Contains($"imagesrcset=\"{responsiveSet}\"", page, StringComparison.Ordinal);
        Assert.Contains("imagesizes=\"(max-width: 767px) 100vw, 50vw\"", page, StringComparison.Ordinal);
        Assert.Contains($"srcset=\"{responsiveSet}\"", content, StringComparison.Ordinal);
        Assert.Contains("sizes=\"(max-width: 767px) 100vw, 50vw\"", content, StringComparison.Ordinal);

        var imageRoot = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "src", "images", "services");
        var originalPath = Path.Combine(imageRoot, originalImage.Replace('/', Path.DirectorySeparatorChar));
        foreach (var width in new[] { "640", "768", "960" })
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
    [InlineData("3D-Printing.cshtml", "printing/printing-hero-prototypes-transparent")]
    [InlineData("CNC-Machining.cshtml", "hero-illustrations/cnc-hero-light")]
    [InlineData("3D-Scanning.cshtml", "hero-illustrations/scanning-hero-raptor-x-unbranded")]
    [InlineData("Custom-Manufacturing.cshtml", "hero-illustrations/custom-hero-light")]
    public void RetainedRazorFallbacks_ProvideRootRelative768HeroSources(string pageName, string responsiveStem)
    {
        var page = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Pages",
            "Services",
            pageName));
        var candidate = $"/src/images/services/{responsiveStem}-768.webp 768w";

        Assert.Contains($"imagesrcset=\"/src/images/services/{responsiveStem}-640.webp 640w", page, StringComparison.Ordinal);
        Assert.Contains(candidate, page, StringComparison.Ordinal);
        Assert.Contains($"srcset=\"/src/images/services/{responsiveStem}-640.webp 640w", page, StringComparison.Ordinal);
        Assert.DoesNotContain($"srcset=\"~/src/images/services/{responsiveStem}", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/services/3d-printing", "printing/printing-hero-prototypes-transparent")]
    [InlineData("/services/cnc-machining", "hero-illustrations/cnc-hero-light")]
    [InlineData("/services/3d-scanning", "hero-illustrations/scanning-hero-raptor-x-unbranded")]
    [InlineData("/services/custom-manufacturing", "hero-illustrations/custom-hero-light")]
    [InlineData("/services/3d-design", "hero-illustrations/design-hero-light")]
    [InlineData("/services/silicone-casting", "hero-illustrations/silicone-hero-light")]
    [InlineData("/services/finishing-and-color", "hero-illustrations/finishing-hero-light")]
    [InlineData("/services/low-volume-injection-molding", "hero-illustrations/injection-hero-light")]
    public async Task ServiceRoute_RendersResponsiveHeroSources(string route, string responsiveStem)
    {
        using var response = await client.GetAsync($"{route}?culture=en&tracking=excluded");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains($"/src/images/services/{responsiveStem}-640.webp 640w", html, StringComparison.Ordinal);
        Assert.Contains($"/src/images/services/{responsiveStem}-768.webp 768w", html, StringComparison.Ordinal);
        Assert.Contains($"/src/images/services/{responsiveStem}-960.webp 960w", html, StringComparison.Ordinal);
        Assert.Contains("sizes=\"(max-width: 767px) 100vw, 50vw\"", html, StringComparison.Ordinal);
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
