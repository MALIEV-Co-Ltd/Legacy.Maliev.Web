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
    public void AboutTimeline_UsesWowContractCompatibleWithBundledAnimateCss()
    {
        var root = FindRepositoryRoot();
        var about = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            "About",
            "Index.cshtml"));
        var styles = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "wwwroot",
            "dist",
            "site.min.css"));

        Assert.Contains("wow animate__fadeIn", about, StringComparison.Ordinal);
        Assert.Contains("animateClass: 'animate__animated'", about, StringComparison.Ordinal);
        Assert.DoesNotContain("wow fadeIn", about, StringComparison.Ordinal);
        Assert.Contains(".animate__animated", styles, StringComparison.Ordinal);
        Assert.Contains(".animate__fadeIn", styles, StringComparison.Ordinal);
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
