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
    public void ObsoleteJqueryValidationStack_IsAbsentFromSourceAndBundles()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var package = File.ReadAllText(Path.Combine(web, "package.json"));
        var packageLock = File.ReadAllText(Path.Combine(web, "package-lock.json"));
        var vendorEntry = File.ReadAllText(Path.Combine(web, "assets", "vendor-entry.js"));
        var applicationScripts = Directory.GetFiles(
                Path.Combine(web, "wwwroot", "src", "app", "js"),
                "*.js",
                SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        var components = Directory.GetFiles(
                Path.Combine(web, "Components"),
                "*.razor",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        var vendorBundle = File.ReadAllText(Path.Combine(web, "wwwroot", "dist", "vendor.min.js"));
        var applicationBundle = File.ReadAllText(Path.Combine(web, "wwwroot", "dist", "app.min.js"));

        Assert.DoesNotContain("\"jquery\"", package, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("node_modules/jquery", packageLock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", vendorEntry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(applicationScripts, source => source.Contains("$(", StringComparison.Ordinal));
        Assert.DoesNotContain(applicationScripts, source => source.Contains("jQuery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(components, source => source.Contains("data-val", StringComparison.Ordinal));
        Assert.DoesNotContain("jQuery requires a window with a document", vendorBundle, StringComparison.Ordinal);
        Assert.DoesNotContain("3.7.1", vendorBundle, StringComparison.Ordinal);
        Assert.DoesNotContain("jQuery", applicationBundle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(applicationScripts, source => source.Contains("addEventListener", StringComparison.Ordinal));
        Assert.Contains(applicationScripts, source => source.Contains("querySelectorAll", StringComparison.Ordinal));
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

    [Fact]
    public void InstantQuotationAssets_AreBundledAndKeepPricingOnServerEndpoints()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var appEntry = File.ReadAllText(Path.Combine(web, "assets", "app-entry.js"));
        var styleEntry = File.ReadAllText(Path.Combine(web, "assets", "site-entry.css"));
        var browserModule = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "instant-quotation.js"));
        var controllerModule = File.ReadAllText(Path.Combine(
            web,
            "wwwroot",
            "src",
            "app",
            "js",
            "instant-quotation-controller.mjs"));
        var module = string.Join('\n', browserModule, controllerModule);

        Assert.Contains("instant-quotation.js", appEntry, StringComparison.Ordinal);
        Assert.Contains("instant-quotation.css", styleEntry, StringComparison.Ordinal);
        Assert.Contains("[data-instant-estimate]", module, StringComparison.Ordinal);
        Assert.Contains("handler: 'GetEstimate'", module, StringComparison.Ordinal);
        Assert.Contains("handler: 'GetOrderTotal'", module, StringComparison.Ordinal);
        Assert.Contains("window.fetch.bind(window)", module, StringComparison.Ordinal);
        Assert.Contains("fetchImpl(", module, StringComparison.Ordinal);
        Assert.Contains("AbortController", module, StringComparison.Ordinal);
        Assert.DoesNotContain("$(", module, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unitPrice =", module, StringComparison.Ordinal);
        Assert.DoesNotContain("vat =", module, StringComparison.Ordinal);
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
