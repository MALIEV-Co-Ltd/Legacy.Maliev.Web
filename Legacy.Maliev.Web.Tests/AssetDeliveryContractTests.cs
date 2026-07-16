namespace Legacy.Maliev.Web.Tests;

public sealed class AssetDeliveryContractTests
{
    [Fact]
    public void RazorPartials_ReferenceNegotiatedAssetsInsteadOfGzipFiles()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var razor = Directory.GetFiles(web, "*.cshtml", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(razor, source => source.Contains(".gz", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(razor, source => source.Contains("~/src/app/", StringComparison.Ordinal));
        Assert.Contains(razor, source => source.Contains("~/dist/vendor.min.js", StringComparison.Ordinal));
        Assert.Contains(razor, source => source.Contains("~/dist/app.min.js", StringComparison.Ordinal));
        Assert.Contains(razor, source => source.Contains("~/dist/site.min.css", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("vendor.min.js")]
    [InlineData("app.min.js")]
    [InlineData("site.min.css")]
    public void GeneratedProductionAsset_IsPresentAndNonEmpty(string relativePath)
    {
        var asset = Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "wwwroot",
            "dist",
            relativePath);

        var info = new FileInfo(asset);
        Assert.True(info.Exists, $"Production asset is missing: {relativePath}");
        Assert.True(info.Length > 0, $"Production asset is empty: {relativePath}");
    }

    [Fact]
    public void ObsoleteAnimationDependencies_AreAbsentFromSourceAndBundles()
    {
        var root = FindRepositoryRoot();
        var about = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            "About",
            "Index.cshtml"));
        var package = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "package.json"));
        var vendorEntry = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "assets", "vendor-entry.js"));
        var styleEntry = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "assets", "site-entry.css"));
        var scripts = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "wwwroot",
            "dist",
            "vendor.min.js"));
        var styles = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "wwwroot",
            "dist",
            "site.min.css"));

        Assert.DoesNotContain("wow", about, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", about, StringComparison.Ordinal);
        Assert.DoesNotContain("wowjs", package, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate.css", package, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wowjs", vendorEntry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate.css", styleEntry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WOW", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("animate__", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeActionLinks_KeepAccessibleButtonTextContrast()
    {
        var stylesheet = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "css",
            "application-shell.css"));

        Assert.Contains(
            ".docs-content a.maliev-button {\n    color: #fff;\n    text-decoration: none;\n}",
            stylesheet,
            StringComparison.Ordinal);
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
