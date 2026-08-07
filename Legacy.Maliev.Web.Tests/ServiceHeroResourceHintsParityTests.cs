using Xunit;

namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceHeroResourceHintsParityTests
{
    public static TheoryData<string, string> ServicePages => new()
    {
        { "ThreeDimensionalDesignPage.razor", "/src/images/services/design/design-workflow.webp" },
        { "ThreeDimensionalPrintingPage.razor", "/src/images/services/printing/printing-hero.webp" },
        { "ThreeDimensionalScanningPage.razor", "/src/images/services/scanning/scanning-hero.webp" },
        { "CncMachiningPage.razor", "/src/images/services/cnc/cnc-hero.webp" },
        { "CustomManufacturingPage.razor", "/src/images/services/custom-manufacturing/custom-manufacturing-story.webp" },
        { "FinishingAndColorPage.razor", "/src/images/services/printing/printing-finish-color-approval.webp" },
        { "LowVolumeInjectionMoldingPage.razor", "/src/images/services/injection-molding/injection-service-hero-wide.png" },
        { "SiliconeCastingPage.razor", "/src/images/services/silicone-casting/silicone-casting-workflow.webp" },
    };

    [Theory]
    [MemberData(nameof(ServicePages))]
    public void ServiceHero_PreloadedAtHighPriority(string fileName, string imagePath)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Components", "Pages", "Services", fileName));

        Assert.Contains($"<link rel=\"preload\" as=\"image\" href=\"{imagePath}\" fetchpriority=\"high\" />", source, StringComparison.Ordinal);
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
