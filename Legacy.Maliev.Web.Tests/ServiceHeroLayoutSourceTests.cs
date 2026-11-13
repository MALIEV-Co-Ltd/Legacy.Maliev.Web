namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceHeroLayoutSourceTests
{
    [Theory]
    [InlineData("CncMachiningContent.razor", true)]
    [InlineData("CustomManufacturingContent.razor", true)]
    [InlineData("FinishingAndColorPage.razor", false)]
    [InlineData("LowVolumeInjectionMoldingContent.razor", true)]
    [InlineData("SiliconeCastingContent.razor", true)]
    [InlineData("ThreeDimensionalDesignContent.razor", true)]
    [InlineData("ThreeDimensionalPrintingContent.razor", true)]
    [InlineData("ThreeDimensionalScanningContent.razor", true)]
    public void ServiceHero_ContainsBreadcrumbAndQuickFactsBeforeContents(string file, bool hasQuickFacts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", file));
        var hero = source.IndexOf("<section class=\"service-hero", StringComparison.Ordinal);
        var end = source.IndexOf("</section>", hero, StringComparison.Ordinal);
        var breadcrumb = source.IndexOf("<ServiceBreadcrumb ", StringComparison.Ordinal);
        if (breadcrumb < 0) breadcrumb = source.IndexOf("<nav class=\"service-breadcrumb\"", StringComparison.Ordinal);
        var toc = source.IndexOf("<ServicePageToc", StringComparison.Ordinal);
        var quick = source.IndexOf("class=\"service-quick\"", StringComparison.Ordinal);
        Assert.True(hero >= 0 && breadcrumb > hero && breadcrumb < end, file);
        Assert.True(toc > end, file);
        Assert.Equal(hasQuickFacts, quick > hero && quick < end);
    }
}
