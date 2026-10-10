namespace Legacy.Maliev.Web.Tests;

public sealed class LegacyServiceDefaultsIdentityContractTests
{
    private const string ServiceDefaultsCommit = "4ffba0639620e7e923d331317f3f28501526b573";

    private const string DotNetPatchVersion = "10.0.12";

    [Fact]
    public void WebProject_UsesLegacyServiceDefaultsOnly()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Legacy.Maliev.Web.csproj"));

        Assert.Contains("Legacy.Maliev.ServiceDefaults\\src\\Legacy.Maliev.ServiceDefaults\\Legacy.Maliev.ServiceDefaults.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.Aspire\\Maliev.Aspire.ServiceDefaults", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Maliev.MessagingContracts", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedLibraryVersion", project, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference Include=\"Legacy.Maliev.ServiceDefaults\"", project, StringComparison.Ordinal);
        var infrastructure = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web.Infrastructure", "Legacy.Maliev.Web.Infrastructure.csproj"));
        Assert.Contains("Legacy.Maliev.ServiceDefaults\\src\\Legacy.Maliev.ServiceDefaults\\Legacy.Maliev.ServiceDefaults.csproj", infrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference Include=\"Legacy.Maliev.ServiceDefaults\"", infrastructure, StringComparison.Ordinal);
        Assert.Equal("Legacy.Maliev.ServiceDefaults",
            typeof(Microsoft.Extensions.Hosting.PrivateFailureObservationExtensions).Assembly.GetName().Name);
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

    [Fact]
    public void WebProjects_AlignDirectFrameworkPackagesWithServiceDefaults()
    {
        var root = FindRepositoryRoot();
        var webProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Legacy.Maliev.Web.csproj"));
        var testProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web.Tests", "Legacy.Maliev.Web.Tests.csproj"));

        foreach (var package in new[]
        {
            "Microsoft.AspNetCore.Authentication.JwtBearer",
            "Microsoft.AspNetCore.DataProtection.StackExchangeRedis",
            "Microsoft.AspNetCore.OpenApi",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.Caching.StackExchangeRedis",
            "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore",
        })
        {
            Assert.Contains($"Include=\"{package}\" Version=\"{DotNetPatchVersion}\"", webProject, StringComparison.Ordinal);
        }

        Assert.Contains($"Include=\"Microsoft.AspNetCore.Mvc.Testing\" Version=\"{DotNetPatchVersion}\"", testProject, StringComparison.Ordinal);
        Assert.DoesNotMatch("Microsoft\\.[^\"]+\" Version=\"10\\.0\\.(?:[0-9]|1[01])\"", webProject + testProject);
    }

    [Fact]
    public void JoinedWebGraph_UsesTheSameSelectedDefaultsWithoutChangingContracts()
    {
        var root = FindRepositoryRoot();
        using var graph = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "tests", "fx-joined", "candidate-graph.json")));
        Assert.Equal(ServiceDefaultsCommit, graph.RootElement.GetProperty("webDefaultsCommit").GetString());
        Assert.Equal("78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7",
            graph.RootElement.GetProperty("contractsCommit").GetString());
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "fx-joined.yml"));
        Assert.Contains($"ref: {ServiceDefaultsCommit}\n          path: .dependencies/Legacy.Maliev.ServiceDefaults",
            workflow.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
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
