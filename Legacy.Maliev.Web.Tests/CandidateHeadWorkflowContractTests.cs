namespace Legacy.Maliev.Web.Tests;

public sealed class CandidateHeadWorkflowContractTests
{
    [Theory]
    [InlineData("ci-main.yml", "${{ github.sha }}")]
    [InlineData("ci-develop.yml", "${{ github.sha }}")]
    [InlineData("ci-staging.yml", "${{ github.sha }}")]
    [InlineData("pr-validation.yml", "${{ github.event.pull_request.head.sha }}")]
    public void ExistingCaller_DeclaresItsExactEventCandidate(string file, string expression)
    {
        var source = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", file));
        Assert.Contains("uses: ./.github/workflows/_build-and-test.yml", source, StringComparison.Ordinal);
        Assert.Contains($"candidate_head: {expression}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate_head: ${{ github.event.pull_request.head.sha || github.sha }}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FullRecipe_RejectsMismatchedCheckoutBeforeAnyBoundaryBuild()
    {
        var source = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "_build-and-test.yml"));
        Assert.Contains("candidate_head:", source, StringComparison.Ordinal);
        Assert.Contains("required: true", source, StringComparison.Ordinal);
        Assert.Contains("ref: ${{ inputs.candidate_head }}", source, StringComparison.Ordinal);
        Assert.Contains("MALIEV_BILLING_CANDIDATE_HEAD: ${{ inputs.candidate_head }}", source, StringComparison.Ordinal);
        Assert.Contains("$actual -cne $expected", source, StringComparison.Ordinal);
        Assert.Contains("git status --porcelain", source, StringComparison.Ordinal);
        Assert.Contains("actualTree = $tree", source, StringComparison.Ordinal);
        Assert.Contains("eventSha = $env:GITHUB_SHA", source, StringComparison.Ordinal);
        Assert.Contains("eventPullRequestHead = $event.pull_request.head.sha", source, StringComparison.Ordinal);
        Assert.Contains("workflowSha = $env:GITHUB_WORKFLOW_SHA", source, StringComparison.Ordinal);
        Assert.Contains("runtimeAcceptance = $false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MALIEV_SOURCE_CUSTODY_DIR: ${{ runner.temp }}", source, StringComparison.Ordinal);
        Assert.Contains("Join-Path $env:RUNNER_TEMP $env:MALIEV_SOURCE_CUSTODY_DIR", source, StringComparison.Ordinal);
        Assert.Contains("path: ${{ runner.temp }}/${{ env.MALIEV_SOURCE_CUSTODY_DIR }}/source-head.json", source, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", source, StringComparison.Ordinal);
        var custody = source.IndexOf("Verify exact caller source", StringComparison.Ordinal);
        var firstBoundaryBuild = source.IndexOf("Build exact-pinned disposable profile boundary services", StringComparison.Ordinal);
        Assert.True(custody >= 0 && firstBoundaryBuild > custody);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Web source root unavailable.");
    }
}
