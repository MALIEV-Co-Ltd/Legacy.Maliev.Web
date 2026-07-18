namespace Legacy.Maliev.Web.Tests;

public sealed class LocalPerformanceContractTests
{
    [Fact]
    public void MigrationClosure_RecordsReproducibleLocalPerformanceEvidence()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "measure-local-web-performance.ps1");
        var evidencePath = Path.Combine(root, "docs", "blazor-migration-performance.md");
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.True(File.Exists(scriptPath), "The local performance measurement script is missing.");
        Assert.True(File.Exists(evidencePath), "The Blazor migration performance evidence is missing.");

        var script = File.ReadAllText(scriptPath);
        var evidence = File.ReadAllText(evidencePath);

        Assert.Contains("TargetProcessId", script, StringComparison.Ordinal);
        Assert.Contains("P95LatencyMilliseconds", script, StringComparison.Ordinal);
        Assert.Contains("PrivateMemoryMegabytes", script, StringComparison.Ordinal);
        Assert.Contains("CpuSeconds", script, StringComparison.Ordinal);
        Assert.Contains("AssetBytes", script, StringComparison.Ordinal);
        Assert.Contains("40e2ef8769457e8ea75bb8f840aab6c8aff79091", evidence, StringComparison.Ordinal);
        Assert.Contains("b28193fb5d0477215eb964c41b1d9dd71f18cba9", evidence, StringComparison.Ordinal);
        Assert.Contains("Razor Pages/jQuery", evidence, StringComparison.Ordinal);
        Assert.Contains("Blazor static SSR", evidence, StringComparison.Ordinal);
        Assert.Contains(".NET 10 Blazor Web App", readme, StringComparison.Ordinal);
        Assert.Contains("docs/blazor-migration-performance.md", readme, StringComparison.Ordinal);
        Assert.DoesNotContain(".NET 10 Razor Pages frontend", readme, StringComparison.Ordinal);
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
