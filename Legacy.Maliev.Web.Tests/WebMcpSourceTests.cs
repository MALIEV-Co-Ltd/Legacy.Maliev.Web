namespace Legacy.Maliev.Web.Tests;

public sealed class WebMcpSourceTests
{
    [Fact]
    public void PublicBundle_ContainsOnlyApprovedWebMcpCapabilities()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var entry = File.ReadAllText(Path.Combine(web, "assets", "app-entry.js"));
        var sourcePath = Path.Combine(web, "wwwroot", "src", "app", "js", "webmcp.js");
        var bundlePath = Path.Combine(web, "wwwroot", "dist", "app.min.js");

        Assert.True(File.Exists(sourcePath), "The public WebMCP source must exist.");
        var source = File.ReadAllText(sourcePath);
        var bundle = File.ReadAllText(bundlePath);

        Assert.Contains("webmcp.js", entry, StringComparison.Ordinal);
        foreach (var tool in new[]
                 {
                     "maliev.list_services",
                     "maliev.open_service",
                     "maliev.start_quotation",
                     "maliev.review_quotation_form",
                 })
        {
            Assert.Contains(tool, source, StringComparison.Ordinal);
            Assert.Contains(tool, bundle, StringComparison.Ordinal);
        }

        Assert.Contains("document.modelContext", source, StringComparison.Ordinal);
        Assert.Contains("AbortController", source, StringComparison.Ordinal);
        Assert.Contains("querySelectorAll(':invalid')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("maliev.submit", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maliev.upload", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maliev.authenticate", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".value", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstantQuotationForm_ProvidesTheReviewToolBoundary()
    {
        var form = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation",
            "InstantQuotationCustomerForm.razor"));

        Assert.Contains("id=\"instant-quotation-form\"", form, StringComparison.Ordinal);
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
