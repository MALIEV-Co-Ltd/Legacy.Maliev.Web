namespace Legacy.Maliev.Web.Tests;

public sealed class FontDeliveryContractTests
{
    [Fact]
    public void SiteAssets_SelfHostRequestedLanguageFontsWithoutGenericFallback()
    {
        var root = FindRepositoryRoot();
        var webRoot = Path.Combine(root, "Legacy.Maliev.Web");
        var packageJson = File.ReadAllText(Path.Combine(webRoot, "package.json"));
        var entry = File.ReadAllText(Path.Combine(webRoot, "assets", "site-entry.css"));
        var appCss = File.ReadAllText(Path.Combine(webRoot, "wwwroot", "src", "app", "css", "app.css"));
        var fontPartial = File.ReadAllText(Path.Combine(webRoot, "Pages", "Shared", "_FontPartial.cshtml"));
        var blazorShell = File.ReadAllText(Path.Combine(webRoot, "Components", "App.razor"));

        Assert.Contains("@fontsource/inter", packageJson, StringComparison.Ordinal);
        Assert.Contains("@fontsource/noto-sans-thai", packageJson, StringComparison.Ordinal);
        Assert.Contains("@fontsource/inter/latin-400.css", entry, StringComparison.Ordinal);
        Assert.Contains("@fontsource/noto-sans-thai/thai-400.css", entry, StringComparison.Ordinal);
        Assert.Contains("html[lang=\"en\"]", appCss, StringComparison.Ordinal);
        Assert.Contains("font-family: 'Inter';", appCss, StringComparison.Ordinal);
        Assert.Contains("html[lang=\"th\"]", appCss, StringComparison.Ordinal);
        Assert.Contains("font-family: 'Noto Sans Thai';", appCss, StringComparison.Ordinal);
        Assert.DoesNotContain("sans-serif", appCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", fontPartial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", fontPartial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", blazorShell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", blazorShell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<body style=\"font-family", blazorShell, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuiltCss_ContainsBundledInterAndNotoSansThaiFaces()
    {
        var root = FindRepositoryRoot();
        var builtCss = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "dist", "site.min.css"));

        Assert.Contains("font-family:Inter", builtCss, StringComparison.Ordinal);
        Assert.Contains("font-family:Noto Sans Thai", builtCss, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.googleapis.com", builtCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", builtCss, StringComparison.OrdinalIgnoreCase);
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
