using System.Globalization;
using System.Text.Json;
using Legacy.Maliev.Web.Components.Metadata;

namespace Legacy.Maliev.Web.Tests;

public sealed class SourceAssetParityTests
{
    [Fact]
    public void MigratedAssets_IncludeCurrentSourceReferencedFiles()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in new[]
        {
            "Legacy.Maliev.Web/wwwroot/src/images/f-ogo_RGB_HEX-58.webp",
            "Legacy.Maliev.Web/wwwroot/src/images/services/injection-molding/injection-service-hero.webp",
            "Legacy.Maliev.Web/wwwroot/src/images/services/scanning/art/scanning-art-cad-reconstruction.webp",
            "Legacy.Maliev.Web/wwwroot/src/images/services/scanning/art/scanning-art-deviation-analysis.webp",
            "Legacy.Maliev.Web/wwwroot/favicon-16x16.webp",
            "Legacy.Maliev.Web/wwwroot/favicon-32x32.webp",
            "Legacy.Maliev.Web/wwwroot/apple-touch-icon.webp",
            "Legacy.Maliev.Web/wwwroot/favicon.webp",
        })
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Expected migrated asset '{path}'.");
            Assert.True(new FileInfo(path).Length > 0, $"Expected migrated asset '{path}' to be non-empty.");
        }
    }

    [Fact]
    public void ServiceFinderScript_IsCompleteAndMaterialDetailsAreWired()
    {
        var root = FindRepositoryRoot();
        var finder = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "service-finder.js"));
        var printing = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor"));

        Assert.DoesNotContain("…", finder, StringComparison.Ordinal);
        Assert.Contains("getContextualServiceIds", finder, StringComparison.Ordinal);
        Assert.Contains("service_finder_results_viewed", finder, StringComparison.Ordinal);
        Assert.Contains("data-material-details-url", printing, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3D Design", "https://www.maliev.com/services/3d-design", "3D Design Services")]
    [InlineData("Silicone Casting", "https://www.maliev.com/services/silicone-casting", "Silicone Casting Services")]
    [InlineData("Low-volume Injection Molding", "https://www.maliev.com/services/low-volume-injection-molding", "Low-Volume Injection Molding")]
    public void ServiceStructuredData_UsesTheCurrentServiceCatalog(string service, string url, string englishName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            using var document = JsonDocument.Parse(PublicServiceStructuredDataDisplayModel.Create(service).ServiceJson);
            Assert.Equal(url, document.RootElement.GetProperty("url").GetString());
            Assert.Equal(englishName, document.RootElement.GetProperty("name").GetString());
            Assert.Contains("https://www.maliev.com/src/images/services/", document.RootElement.GetProperty("image").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
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
}
