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
        var retainedLayout = File.ReadAllText(Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"));
        var motionCss = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "motion.css"));
        var motionJs = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "motion.js"));

        Assert.Contains("motion.js", appEntry, StringComparison.Ordinal);
        Assert.Contains("motion.css", siteEntry, StringComparison.Ordinal);
        Assert.Contains("data-motion", app, StringComparison.Ordinal);
        Assert.Contains("__malievMotionFailsafe", app, StringComparison.Ordinal);
        foreach (var renderer in new[] { app, retainedLayout })
        {
            Assert.Contains("document.documentElement.dataset.motion = \"ready\";", renderer, StringComparison.Ordinal);
            Assert.Contains("window.__malievMotionFailsafe = window.setTimeout(function () {", renderer, StringComparison.Ordinal);
            Assert.Contains("document.documentElement.removeAttribute(\"data-motion\");", renderer, StringComparison.Ordinal);
            Assert.Contains("}, 2500);", renderer, StringComparison.Ordinal);
            Assert.True(renderer.IndexOf("document.documentElement.dataset.motion", StringComparison.Ordinal)
                < renderer.IndexOf("</head>", StringComparison.Ordinal));
        }
        Assert.Contains("prefers-reduced-motion: reduce", motionCss, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", motionJs, StringComparison.Ordinal);
        Assert.Contains("data-reveal", motionJs, StringComparison.Ordinal);
        Assert.Contains("[data-motion=\"ready\"]", motionCss, StringComparison.Ordinal);
        Assert.Contains("if (root.dataset.motion !== 'ready')", motionJs, StringComparison.Ordinal);
        Assert.Contains("root.dataset.motionInit === 'true'", motionJs, StringComparison.Ordinal);
        Assert.Contains("root.removeAttribute('data-motion');", motionJs, StringComparison.Ordinal);
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
