namespace Legacy.Maliev.Web.Tests;

public sealed class FacebookRetirementContractTests
{
    [Fact]
    public void ProductionWebSource_DoesNotRestoreRetiredFacebookSdkOrPixelRuntime()
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
            ".razor",
            ".resx",
        };
        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}",
        };
        var forbiddenTokens = new[]
        {
            "connect.facebook.net",
            "_FacebookSdkPartial",
            "facebook-jssdk",
            "fb:app_id",
            "fbq(",
        };

        var violations = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => inspectedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => ignoredSegments.All(segment => !path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .Select(path =>
            {
                var relativePath = Path.GetRelativePath(root, path);
                var source = File.ReadAllText(path);
                var token = forbiddenTokens.FirstOrDefault(candidate =>
                    source.Contains(candidate, StringComparison.OrdinalIgnoreCase));
                return token is null ? null : $"{relativePath} contains retired token '{token}'";
            })
            .Where(violation => violation is not null)
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
