using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

public sealed class DependabotPolicyContractTests
{
    [Theory]
    [InlineData("npm", 10)]
    [InlineData("nuget", 10)]
    [InlineData("docker", 5)]
    [InlineData("github-actions", 5)]
    public void Configuration_LimitsAndGroupsCompatibleUpdatesOnly(string ecosystem, int expectedLimit)
    {
        var configuration = File.ReadAllText(FindRepositoryFile(".github", "dependabot.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var updateBlock = Regex.Match(
            configuration,
            $"(?ms)^  - package-ecosystem: {Regex.Escape(ecosystem)}\\n(?<body>.*?)(?=^  - package-ecosystem:|\\z)");

        Assert.True(updateBlock.Success, $"Missing Dependabot update block for {ecosystem}.");
        Assert.Contains($"open-pull-requests-limit: {expectedLimit}", updateBlock.Value, StringComparison.Ordinal);
        Assert.Contains("groups:", updateBlock.Value, StringComparison.Ordinal);
        Assert.Contains("update-types:", updateBlock.Value, StringComparison.Ordinal);
        Assert.Contains("- minor", updateBlock.Value, StringComparison.Ordinal);
        Assert.Contains("- patch", updateBlock.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("- major", updateBlock.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_DoesNotEnableAutomaticMerges()
    {
        var configuration = File.ReadAllText(FindRepositoryFile(".github", "dependabot.yml"));

        Assert.DoesNotContain("auto-merge", configuration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("automerge", configuration, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryFile(params string[] pathSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. pathSegments]);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathSegments)}.");
    }
}
