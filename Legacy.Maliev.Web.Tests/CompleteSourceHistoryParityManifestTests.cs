using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class CompleteSourceHistoryParityManifestTests
{
    [Fact]
    public void Manifest_AccountsForEveryWebCommitThroughTheFrozenSourceHead()
    {
        var source = File.ReadAllText(ManifestPath());
        var rows = CommitRow().Matches(source).Select(match => new
        {
            Hash = match.Groups[1].Value,
            Disposition = match.Groups[2].Value,
        }).ToArray();

        Assert.Equal(298, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.Hash).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("5fac706", rows[0].Hash);
        Assert.Equal("a40ae59", rows[104].Hash);
        Assert.Equal("dcc088f", rows[227].Hash);
        Assert.Equal("370fe20", rows[^1].Hash);
        Assert.DoesNotContain(rows, row => row.Disposition is "Gap");

        var inventory = string.Join('\n', rows.Select(row => row.Hash));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventory)))
            .ToLowerInvariant();
        Assert.Equal("46cb93af7068e81a98c87d278e34585979b73ec7dc2a95a881f4e38f80e8c467", digest);
    }

    [Fact]
    public void Manifest_UsesOnlyReviewedDispositionClassesWithFrozenTotals()
    {
        var rows = CommitRow().Matches(File.ReadAllText(ManifestPath()))
            .Select(match => match.Groups[2].Value)
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(251, rows["Migrated"]);
        Assert.Equal(16, rows["Validation translated"]);
        Assert.Equal(3, rows["No unique change"]);
        Assert.Equal(6, rows["Superseded safely"]);
        Assert.Equal(4, rows["Excluded tooling"]);
        Assert.Equal(18, rows["Release gate"]);
        Assert.Equal(298, rows.Values.Sum());
    }

    private static string ManifestPath() => Path.Combine(
        FindRepositoryRoot(),
        "docs",
        "complete-source-history-parity-through-370fe20.md");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    [GeneratedRegex(
        "^\\| `([0-9a-f]{7})` \\| [0-9]{4}-[0-9]{2}-[0-9]{2} \\| ([^|]+?) \\|",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex CommitRow();
}
