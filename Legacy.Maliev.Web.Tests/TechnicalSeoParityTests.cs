using Xunit;

namespace Legacy.Maliev.Web.Tests;

public sealed class TechnicalSeoParityTests
{
    [Fact]
    public void Robots_AllowsGeneralAndGoogleAiCrawlersWithoutPrivateRoutes()
    {
        var path = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "robots.txt");
        var robots = File.ReadAllText(path);

        Assert.Contains("User-agent: *", robots, StringComparison.Ordinal);
        Assert.Contains("Allow: /", robots, StringComparison.Ordinal);
        Assert.DoesNotContain("User-agent: Google-Extended", robots, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Disallow: /account/*", robots, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Disallow: /member/", robots, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sitemap: https://www.maliev.com/sitemap", robots, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
