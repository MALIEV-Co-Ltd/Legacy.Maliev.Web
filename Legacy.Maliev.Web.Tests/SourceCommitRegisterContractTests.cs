using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class SourceCommitRegisterContractTests
{
    private const string SourceCheckpoint = "5ac7d045c51194edd9e64d8564f1b726b001be34";
    private const int ExpectedCommitCount = 286;

    [Fact]
    public void Register_ClassifiesEverySourceCommitWithEvidence()
    {
        var repositoryRoot = FindRepositoryRoot();
        var registerPath = Path.Combine(
            repositoryRoot,
            "docs",
            "source-commit-register-through-5ac7d04.md");
        var reconciliationPath = Path.Combine(
            repositoryRoot,
            "docs",
            "cnc-source-commit-reconciliation.md");

        var register = File.ReadAllText(registerPath);
        var entries = RegisterEntryRegex().Matches(register);

        Assert.Contains($"Published source main: `{SourceCheckpoint}`.", register, StringComparison.Ordinal);
        Assert.Equal(ExpectedCommitCount, entries.Count);
        Assert.Equal(
            ExpectedCommitCount,
            entries.Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("- [ ]", register, StringComparison.Ordinal);
        Assert.All(entries.Cast<Match>(), entry =>
            Assert.Contains("[evidence](", entry.Value, StringComparison.Ordinal));
        Assert.Contains(entries.Cast<Match>(), entry =>
            entry.Groups[1].Value.Equals(SourceCheckpoint, StringComparison.Ordinal));
        Assert.True(File.Exists(reconciliationPath));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Legacy.Maliev.Web repository root.");
    }

    [GeneratedRegex(@"(?m)^- \[x\] `([0-9a-f]{40})` — .+$", RegexOptions.CultureInvariant)]
    private static partial Regex RegisterEntryRegex();
}
