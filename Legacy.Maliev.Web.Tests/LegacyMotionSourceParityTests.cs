namespace Legacy.Maliev.Web.Tests;

public sealed class LegacyMotionSourceParityTests
{
    [Fact]
    public void MotionAssetsAreGatedAndKeepReducedMotionFailSafe()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var appEntry = File.ReadAllText(Path.Combine(web, "assets", "app-entry.js"));
        var siteEntry = File.ReadAllText(Path.Combine(web, "assets", "site-entry.css"));
        var app = File.ReadAllText(Path.Combine(web, "Components", "App.razor"));
        var motionCss = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "motion.css"));
        var motionJs = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "motion.js"));

        Assert.Contains("motion.js", appEntry, StringComparison.Ordinal);
        Assert.Contains("motion.css", siteEntry, StringComparison.Ordinal);
        Assert.Contains("data-motion", app, StringComparison.Ordinal);
        Assert.Contains("__malievMotionFailsafe", app, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", motionCss, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", motionJs, StringComparison.Ordinal);
        Assert.Contains("data-reveal", motionJs, StringComparison.Ordinal);
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
