namespace Legacy.Maliev.Web.Tests;

public sealed class FacebookRetirementContractTests
{
    [Fact]
    public void ProductionWebSource_HasNoFacebookIntegrationReferences()
    {
        var root = FindRepositoryRoot();
        var productionRoots = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Web"),
            Path.Combine(root, "Legacy.Maliev.Web.Application"),
        };
        var inspectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".cshtml",
            ".css",
            ".js",
            ".json",
            ".resx",
        };
        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}",
        };
        var forbidden = new[]
        {
            "facebook",
            "connect.facebook.net",
            "facebook.com",
            "m.me/maliev.manufacturing",
            "fb:app_id",
        };

        var violations = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => inspectedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => ignoredSegments.All(segment => !path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path => forbidden
                .Where(term => File.ReadAllText(path).Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"{Path.GetRelativePath(root, path)} contains '{term}'"))
            .ToArray();

        Assert.Empty(violations);
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
