using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class SourceCommitRegisterContractTests
{
    private const string SourceCheckpoint = "fe3d824d52b2ce3f06f98211cd48349979a71527";
    private const int ExpectedCommitCount = 311;
    private const int FrozenHistoryCommitCount = 286;
    private const string FrozenHistorySha256 = "ebf9de7d1dbde6b281edd2cf836872c92763afde3073c26515efe3b7e89e3fc8";
    private const string RegisterHistorySha256 = "345585763ed975ee429a46626e3de62ad641f2c84dedb9aeb23e4a72c76d65cf";

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
        var commits = entries.Select(entry => entry.Groups[1].Value).ToArray();
        var reconciliation = File.ReadAllText(reconciliationPath);

        Assert.Contains($"Published source main: `{SourceCheckpoint}`.", register, StringComparison.Ordinal);
        Assert.Equal(ExpectedCommitCount, entries.Count);
        Assert.Equal(
            ExpectedCommitCount,
            commits.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(FrozenHistorySha256, ComputeSequenceSha256(commits[..FrozenHistoryCommitCount]));
        Assert.Equal(RegisterHistorySha256, ComputeSequenceSha256(commits));
        Assert.Equal(
            [
                "878bb8a42fe8d2fb7d6e18a0b1033fdd3619c169",
                "939184a5604a012fb65666bc4afce48a1a98cb0a",
                "fe3d824d52b2ce3f06f98211cd48349979a71527"
            ],
            commits[^3..]);
        Assert.DoesNotContain("- [ ]", register, StringComparison.Ordinal);
        Assert.All(entries.Cast<Match>(), entry =>
            Assert.Contains("[evidence](", entry.Value, StringComparison.Ordinal));
        Assert.Contains(entries.Cast<Match>(), entry =>
            entry.Groups[1].Value.Equals(SourceCheckpoint, StringComparison.Ordinal));
        Assert.Contains(entries.Cast<Match>(), entry =>
            entry.Groups[1].Value.Equals("bf079667bba7d9bc01a9688b1f7f5a5be5748a92", StringComparison.Ordinal));
        Assert.Contains("`bf079667bba7d9bc01a9688b1f7f5a5be5748a92`", register, StringComparison.Ordinal);
        Assert.Contains("`691d87c56c979613c4cd12b43a0d5e2e40994beb`", register, StringComparison.Ordinal);
        Assert.Contains("`6438973a98a3a76f724df70f4b5c8b44541c9e3b`", register, StringComparison.Ordinal);
        Assert.Contains("`da8dbfa41a0f5b444ee0375ebe0b646b11b63039`", register, StringComparison.Ordinal);
        Assert.Contains("`bbba5046b41c0b1c7fed949d29b093b555b94b9b`", register, StringComparison.Ordinal);
        Assert.Contains("`fe3d824d52b2ce3f06f98211cd48349979a71527`", register, StringComparison.Ordinal);
        Assert.Contains(
            "| `bf079667bba7d9bc01a9688b1f7f5a5be5748a92` | Migrated | `5068239880271e1a17c1d2707cf728c019d4adc2`; `691d87c56c979613c4cd12b43a0d5e2e40994beb` |",
            reconciliation,
            StringComparison.Ordinal);
        Assert.Contains(
            "| `cbbe1c3a482d0825cecb732bb1044c88bb20358c` | Migrated | `6438973a98a3a76f724df70f4b5c8b44541c9e3b` |",
            reconciliation,
            StringComparison.Ordinal);
        Assert.Contains(
            "| `ad2774760862371b4ea442d02669ad9ecd4913f5` | Non-runtime plan | None |",
            reconciliation,
            StringComparison.Ordinal);
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

    private static string ComputeSequenceSha256(IEnumerable<string> commits) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', commits))));

    [GeneratedRegex(@"(?m)^- \[x\] `([0-9a-f]{40})` — .+$", RegexOptions.CultureInvariant)]
    private static partial Regex RegisterEntryRegex();
}
