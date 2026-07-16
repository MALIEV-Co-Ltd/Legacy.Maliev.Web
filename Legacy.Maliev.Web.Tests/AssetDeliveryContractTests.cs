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
