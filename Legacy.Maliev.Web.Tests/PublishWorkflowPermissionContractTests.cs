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

    [Fact]
    public void ValidationWorkflow_RequiredCheckFailsWhenFocusedValidationDidNotSucceed()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "_build-and-test.yml"));
        var full = ValidationJob(source, "validate");
        Assert.Contains("    name: validate\n", full, StringComparison.Ordinal);
        Assert.Contains("    needs: [focused, resource_checkpoint_qualification]\n    if: always()\n", full, StringComparison.Ordinal);
        var firstStep = full.IndexOf("      - name:", StringComparison.Ordinal);
        var guard = full.IndexOf("      - name: Require successful focused validation", StringComparison.Ordinal);
        var checkout = full.IndexOf("      - name: Check out Web", StringComparison.Ordinal);
        Assert.Equal(firstStep, guard);
        Assert.True(guard >= 0 && checkout > guard);
        var body = full[guard..checkout];
        Assert.Contains("FOCUSED_RESULT: $" + "{{ needs.focused.result }}", body, StringComparison.Ordinal);
        Assert.Contains("if [[ \"$FOCUSED_RESULT\" != \"success\" || \"$RESOURCE_QUALIFICATION_RESULT\" != \"success\" ]]; then", body, StringComparison.Ordinal);
        Assert.Contains("RESOURCE_QUALIFICATION_RESULT: $" + "{{ needs.resource_checkpoint_qualification.result }}", body, StringComparison.Ordinal);
        Assert.Contains("exit 1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationWorkflow_FreshRunnerRetainsTheUnfilteredCompositeAndRawCoverage()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "_build-and-test.yml"));
        var full = ValidationJob(source, "validate");
        var focused = ValidationJob(source, "focused");
        const string action = "MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/dotnet-validate@e5732037fe94b7ed6e5be2cd4c23dbf6ef5e2617";
        Assert.Contains(action, full, StringComparison.Ordinal);
        Assert.DoesNotContain(action, focused, StringComparison.Ordinal);
        Assert.Contains("solution: Legacy.Maliev.Web.slnx", full, StringComparison.Ordinal);
        Assert.Contains("use-local-maliev-dependencies: 'true'", full, StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", full, StringComparison.Ordinal);
        Assert.DoesNotContain("--list-tests", full, StringComparison.Ordinal);
        Assert.Contains("./tests/WebFxRawCoverage.Tests.ps1", full, StringComparison.Ordinal);
        Assert.Contains("./tests/Test-WebFxRawCoverage.ps1 -ResultsRoot ./Legacy.Maliev.Web.Tests/TestResults", full, StringComparison.Ordinal);
        Assert.DoesNotContain("-MinimumLinePercent", full, StringComparison.Ordinal);
        foreach (var job in new[] { focused, full })
        {
            Assert.Contains("runs-on: ubuntu-latest", job, StringComparison.Ordinal);
            Assert.Contains("prepare-profile-producer-boundary.ps1", job, StringComparison.Ordinal);
            Assert.Contains("prepare-member-auth-indexing-proof.ps1", job, StringComparison.Ordinal);
            Assert.Contains("prepare-billing-catalog-proof.ps1", job, StringComparison.Ordinal);
            Assert.Contains("prepare-scb-payment-boundary.ps1", job, StringComparison.Ordinal);
            Assert.Contains("playwright.ps1 install --with-deps chromium", job, StringComparison.Ordinal);
            Assert.Contains("probe-startup-dockerhub-quota.py", job, StringComparison.Ordinal);
            Assert.Contains("--require-hashes", job, StringComparison.Ordinal);
            Assert.Contains("Remove only the owned preliminary PDF verification runtime\n        if: always()", job, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ValidationWorkflow_KeepsSeparateArtifactsAndOriginalEventCheckout()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "_build-and-test.yml"));
        var full = ValidationJob(source, "validate");
        var focused = ValidationJob(source, "focused");
        const string combined = "name: web-combined-native-$" + "{{ github.sha }}";
        const string focus = "name: web-focused-native-$" + "{{ github.sha }}";
        Assert.Contains(combined, full, StringComparison.Ordinal);
        Assert.DoesNotContain(combined, focused, StringComparison.Ordinal);
        Assert.Contains(focus, focused, StringComparison.Ordinal);
        Assert.DoesNotContain(focus, full, StringComparison.Ordinal);
        Assert.Contains("full-suite-identity/receipt.json", full, StringComparison.Ordinal);
        foreach (var job in new[] { focused, full })
        {
            var checkout = Regex.Match(job,
                @"(?ms)^      - name: Check out Web\r?\n(?<body>.*?)(?=^      - name:|\z)");
            Assert.True(checkout.Success);
            Assert.DoesNotContain("ref:", checkout.Groups["body"].Value, StringComparison.Ordinal);
            Assert.Contains("persist-credentials: false", checkout.Groups["body"].Value, StringComparison.Ordinal);
        }
    }

    private static string ValidationJob(string source, string name)
    {
        Assert.Matches(@"(?m)^jobs:\r?$", source);
        var matches = Regex.Matches(source,
            @"(?ms)^  " + Regex.Escape(name) + @":\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:\s*\r?$|\z)");
        return Assert.Single(matches.Cast<Match>()).Groups["body"].Value;
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
