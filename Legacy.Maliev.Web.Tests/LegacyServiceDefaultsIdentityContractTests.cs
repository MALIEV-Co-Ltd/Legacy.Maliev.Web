namespace Legacy.Maliev.Web.Tests;

public sealed class LegacyServiceDefaultsIdentityContractTests
{
    private const string ServiceDefaultsCommit = "4e9126d318d372d9cf7165cbe73da8754439ee04";

    [Fact]
    public void WebProject_UsesLegacyServiceDefaultsOnly()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Legacy.Maliev.Web.csproj"));

        Assert.Contains("Legacy.Maliev.ServiceDefaults\\src\\Legacy.Maliev.ServiceDefaults\\Legacy.Maliev.ServiceDefaults.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.Aspire\\Maliev.Aspire.ServiceDefaults", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maliev.MessagingContracts", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebDockerfile_ClonesOnlyLegacySharedDependencies()
    {
        var root = FindRepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Dockerfile"));

        Assert.Contains("github.com/MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults.git", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/dependencies/Legacy.Maliev.ServiceDefaults", dockerfile, StringComparison.Ordinal);
        Assert.Contains($"checkout {ServiceDefaultsCommit}", dockerfile, StringComparison.Ordinal);
        Assert.Contains("github.com/MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts.git", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/dependencies/Legacy.Maliev.CompatibilityContracts", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.Aspire", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maliev.MessagingContracts", dockerfile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WebBuildWorkflow_UsesLegacySharedDependencies()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));

        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.ServiceDefaults", workflow, StringComparison.Ordinal);
        Assert.Contains($"ref: {ServiceDefaultsCommit}", workflow, StringComparison.Ordinal);
        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("MALIEV-Co-Ltd/Maliev.Aspire", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MALIEV-Co-Ltd/Maliev.MessagingContracts", workflow, StringComparison.OrdinalIgnoreCase);
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
