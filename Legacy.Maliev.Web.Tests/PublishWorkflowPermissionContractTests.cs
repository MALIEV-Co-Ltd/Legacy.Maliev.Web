using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublishWorkflowPermissionContractTests
{
    [Fact]
    public void ReleaseAssetCoverage_PreservesNodeFailureThroughEvidencePipeline()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "release-asset-coverage.yml"));
        var step = Regex.Match(source,
            @"(?ms)^      - name: Collect and gate validator coverage\r?\n(?<body>.*?)(?=^      - name:|\z)");

        Assert.True(step.Success, "The release validator requires its independent coverage gate.");
        var body = step.Groups["body"].Value;
        Assert.True(Regex.IsMatch(body, @"(?m)^        shell: bash\r?$"),
            "Explicit bash enables pipefail so tee cannot mask a failed Node test or coverage floor.");
        Assert.Contains("--test-coverage-lines=80", body, StringComparison.Ordinal);
        Assert.Contains("| tee ../runner-results/validator-tests.txt", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationWorkflow_UsesRedactedJwtResourceScanner()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "_build-and-test.yml"));
        Assert.Contains(
            "MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/dotnet-validate@e5732037fe94b7ed6e5be2cd4c23dbf6ef5e2617",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/dotnet-validate@6017816fa67f369d785ed30794f002cfd6299af7",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PublishWorkflow_UsesGatedImmutablePublisherWithoutDirectDeployOrImagePrune()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-image.yml"));

        Assert.Contains("if: vars.LEGACY_DEPLOY_ENABLED != 'true'", source, StringComparison.Ordinal);
        Assert.Contains("if: vars.LEGACY_DEPLOY_ENABLED == 'true'", source, StringComparison.Ordinal);
        Assert.Contains(
            "uses: MALIEV-Co-Ltd/Legacy.Maliev.Workflows/.github/workflows/publish-image.yml@d583f55473f47f72d33b51060fa5d14e0974daf5",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("deploy.ps1", source, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            Regex.IsMatch(source, @"\bdocker\s+(?:image|system)\s+prune\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "The Web publish path must not prune unrelated host images.");
        Assert.False(File.Exists(Path.Combine(root, "deploy.ps1")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "deploy.ps1")));
    }

    [Fact]
    public void PublishWorkflow_ScopesOidcToPublishJobs()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRoot(),
            ".github",
            "workflows",
            "publish-image.yml"));

        var jobsIndex = source.IndexOf("\njobs:", StringComparison.Ordinal);
        Assert.True(jobsIndex > 0, "The publish workflow must define a jobs section.");
        var workflowHeader = source[..jobsIndex];
        Assert.Contains("permissions:", workflowHeader, StringComparison.Ordinal);
        Assert.Contains("  contents: read", workflowHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", workflowHeader, StringComparison.OrdinalIgnoreCase);

        var publishJobs = Regex.Matches(
            source,
            @"(?ms)^  publish(?:-[^:\r\n]+)?:\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:\s*\r?$|\z)");
        Assert.NotEmpty(publishJobs);
        foreach (Match publishJob in publishJobs)
        {
            var job = publishJob.Groups["body"].Value;
            Assert.Contains("permissions:", job, StringComparison.Ordinal);
            Assert.Contains("contents: read", job, StringComparison.Ordinal);
            Assert.Contains("actions: read", job, StringComparison.Ordinal);
            Assert.Contains("id-token: write", job, StringComparison.Ordinal);
        }

        var deploymentGate = Regex.Match(
            source,
            @"(?ms)^  deployment-gate:\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:\s*\r?$|\z)");
        if (deploymentGate.Success)
        {
            Assert.DoesNotContain("id-token: write", deploymentGate.Groups["body"].Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var workflow = Path.Combine(directory.FullName, ".github", "workflows", "publish-image.yml");
            if (File.Exists(workflow))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The publish workflow root was not found.");
    }
}
