namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceHeroFollowupContractTests
{
    [Theory]
    [InlineData("CncMachiningContent.razor")]
    [InlineData("ThreeDimensionalPrintingContent.razor")]
    [InlineData("ThreeDimensionalScanningContent.razor")]
    public void ProcessGuidanceLink_UsesTheSharedFollowupSpacingHook(string componentName)
    {
        var component = ReadRepositoryFile(
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Services",
            componentName);

        Assert.Contains("class=\"service-hero-followup\"", component, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedServiceCss_PreservesTheSourceFollowupSpacing()
    {
        var css = ReadRepositoryFile(
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "css",
            "service-pages.css");
        var builtCss = ReadRepositoryFile(
            "Legacy.Maliev.Web",
            "wwwroot",
            "dist",
            "route-services.css");

        Assert.Contains(
            ".service-hero-followup { margin: 1.25rem 0 0; line-height: 1.6; }",
            css,
            StringComparison.Ordinal);
        Assert.Contains(
            ".service-hero-followup{margin:1.25rem 0 0;line-height:1.6}",
            builtCss,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
    }
}
