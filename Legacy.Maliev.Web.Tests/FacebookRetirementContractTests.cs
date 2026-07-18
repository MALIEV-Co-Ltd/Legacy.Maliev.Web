namespace Legacy.Maliev.Web.Tests;

public sealed class FacebookRetirementContractTests
{
    [Fact]
    public void ProductionWebSource_DefaultDeniesFacebookExceptExactMessengerTokens()
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
        var allowedTokensByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("Legacy.Maliev.Web", "Components", "Analytics", "PublicContactChannelAnalytics.razor")] = "facebook_messenger",
            [Path.Combine("Legacy.Maliev.Web", "Components", "Layout", "SocialLinks.razor")] = "fa-facebook-messenger",
        };

        var violations = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => inspectedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => ignoredSegments.All(segment => !path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .Select(path =>
            {
                var relativePath = Path.GetRelativePath(root, path);
                var source = File.ReadAllText(path);
                if (allowedTokensByFile.TryGetValue(relativePath, out var allowedToken))
                {
                    Assert.Equal(1, source.Split(allowedToken, StringSplitOptions.None).Length - 1);
                    source = source.Replace(allowedToken, string.Empty, StringComparison.Ordinal);
                }

                return source.Contains("facebook", StringComparison.OrdinalIgnoreCase)
                    ? $"{relativePath} contains a non-allowlisted Facebook reference"
                    : null;
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
