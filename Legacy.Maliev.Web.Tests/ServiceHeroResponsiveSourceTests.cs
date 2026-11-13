namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceHeroResponsiveSourceTests
{
    [Fact]
    public void LightHeroes_RetainSourceViewportAndWideScreenRules()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css"));
        Assert.Contains("min-height: min(calc(100svh - 4.5rem), 58rem)", css, StringComparison.Ordinal);
        Assert.Contains("max-height: clamp(12rem, 34svh, 23rem)", css, StringComparison.Ordinal);
        Assert.Contains("--service-width: min(92vw, 96rem)", css, StringComparison.Ordinal);
    }
}
