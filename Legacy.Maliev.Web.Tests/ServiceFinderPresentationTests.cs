namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceFinderPresentationTests
{
    [Fact]
    public void ServicesDirectoryKeepsAnAccessibleNoScriptFinderSurface()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Services", "ServicesContent.razor"));
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "service-pages.css"));

        Assert.Contains("class=\"service-finder\"", content, StringComparison.Ordinal);
        Assert.Contains("data-service-finder", content, StringComparison.Ordinal);
        Assert.Contains("role=\"progressbar\"", content, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"false\"", content, StringComparison.Ordinal);
        Assert.Contains("data-finder-skip-to-results", content, StringComparison.Ordinal);
        Assert.Contains("data-finder-quotation-link", content, StringComparison.Ordinal);
        Assert.Contains("href=\"/quotation\"", content, StringComparison.Ordinal);
        Assert.Contains(".service-finder-options { display: grid; grid-template-columns: repeat(4", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-option:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains(".service-finder-option[aria-pressed=\"true\"]", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
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
