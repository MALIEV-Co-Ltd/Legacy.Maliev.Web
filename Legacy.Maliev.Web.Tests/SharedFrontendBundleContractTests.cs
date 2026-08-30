namespace Legacy.Maliev.Web.Tests;

using Legacy.Maliev.Web.Components.Layout;
using Microsoft.AspNetCore.Http;

public sealed class SharedFrontendBundleContractTests
{
    [Fact]
    public void SharedEntries_ExcludeRouteOwnedAssets()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var styles = File.ReadAllText(Path.Combine(web, "assets", "site-entry.css"));
        var scripts = File.ReadAllText(Path.Combine(web, "assets", "app-entry.js"));

        Assert.Contains("app.css", styles, StringComparison.Ordinal);
        Assert.Contains("motion.css", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("service-pages.css", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("landing.css", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("inquiry-pages.css", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("service-finder.css", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("instant-quotation.css", styles, StringComparison.Ordinal);

        Assert.Contains("app.js", scripts, StringComparison.Ordinal);
        Assert.Contains("motion.js", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("service-finder.js", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("material-comparison.js", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("finish-color-matcher", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("instant-quotation.js", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("member-order-form.js", scripts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/", "route-home.css")]
    [InlineData("/About", "route-about.css")]
    [InlineData("/Contact", "route-inquiry.css")]
    [InlineData("/Quotation", "route-inquiry.css")]
    [InlineData("/services", "route-services-index.css")]
    [InlineData("/services/cnc-machining", "route-services.css")]
    [InlineData("/InstantQuotation/3D-Printing", "route-instant-quotation.css")]
    public void RouteStyles_AreSelectedOnlyForTheirOwner(string path, string expected)
    {
        Assert.Equal([expected], PublicRouteAssets.GetStyles(new PathString(path)));
    }

    [Theory]
    [InlineData("/Contact", "route-inquiry.js")]
    [InlineData("/Quotation", "route-inquiry.js")]
    [InlineData("/services", "route-service-finder.js")]
    [InlineData("/services/3d-printing", "route-service-printing.js")]
    [InlineData("/services/3d-scanning", "route-service-scanning.js")]
    [InlineData("/services/finishing-and-color", "route-service-finishing.js")]
    [InlineData("/services/cnc-machining", "route-service-cnc.js")]
    [InlineData("/InstantQuotation/3D-Printing", "route-instant-quotation.js")]
    [InlineData("/Member/Orders/CNC-Machining", "route-member-order.js")]
    public void RouteScripts_AreSelectedOnlyForTheirOwner(string path, string expected)
    {
        Assert.Equal([expected], PublicRouteAssets.GetScripts(new PathString(path)));
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Career")]
    [InlineData("/Member")]
    [InlineData("/Member/Orders/History")]
    public void UnrelatedRoutes_DoNotLoadPageSpecificAssets(string path)
    {
        var requestPath = new PathString(path);
        Assert.Empty(PublicRouteAssets.GetStyles(requestPath));
        Assert.Empty(PublicRouteAssets.GetScripts(requestPath));
    }

    [Fact]
    public void GeneratedRouteAssets_ArePresentAndNonEmpty()
    {
        var dist = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "dist");
        var expected = new[]
        {
            "route-about.css", "route-home.css", "route-inquiry.css",
            "route-instant-quotation.css", "route-services.css", "route-services-index.css",
            "route-inquiry.js", "route-instant-quotation.js", "route-member-order.js",
            "route-service-cnc.js", "route-service-finder.js", "route-service-finishing.js", "route-service-printing.js",
            "route-service-scanning.js", "route-service-toc.js",
        };

        Assert.All(expected, asset => Assert.True(
            new FileInfo(Path.Combine(dist, asset)) is { Exists: true, Length: > 0 },
            $"Route asset is missing or empty: {asset}"));
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
