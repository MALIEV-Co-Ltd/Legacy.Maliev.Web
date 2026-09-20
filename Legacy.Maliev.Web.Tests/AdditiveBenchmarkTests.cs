using Maliev.AdditiveBenchmark;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>
/// Freezes the versioned additive benchmark contract and its blocked evidence report.
/// </summary>
public sealed class AdditiveBenchmarkTests
{
    private static readonly string AssetDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "TestAssets",
        "AdditiveBenchmark");

    private static readonly string ManifestPath = Path.Combine(AssetDirectory, "manifest.v1.json");
    private static readonly string SchemaPath = Path.Combine(AssetDirectory, "manifest.v1.schema.json");
    private static readonly string ReportPath = Path.Combine(AssetDirectory, "report.v1.json");

    /// <summary>
    /// Confirms the scaffold is valid but remains explicitly blocked on unavailable evidence.
    /// </summary>
    [Fact]
    public void Run_CheckedInManifest_IsValidAndExplicitlyBlocked()
    {
        BenchmarkRunResult result = AdditiveBenchmarkRunner.Run(ManifestPath, SchemaPath);

        Assert.Equal("blocked", result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.Empty(result.Report.InvalidIssues);
        Assert.Equal(2, result.Report.Summary.TotalCases);
        Assert.Equal(0, result.Report.Summary.ReadyCases);
        Assert.Equal(2, result.Report.Summary.BlockedCases);
        Assert.Contains(result.Report.BlockingIssues, issue => issue.Code == "coverage.model_count");
        Assert.Contains(result.Report.BlockingIssues, issue => issue.Code == "evidence.blocked");
        Assert.All(result.Report.Cases, item => Assert.Equal("blocked", item.Availability));
    }

    /// <summary>
    /// Confirms repeated runs have identical report bytes and match the reviewed error report.
    /// </summary>
    [Fact]
    public void Run_CheckedInManifest_ProducesDeterministicReviewedReport()
    {
        BenchmarkRunResult first = AdditiveBenchmarkRunner.Run(ManifestPath, SchemaPath);
        BenchmarkRunResult second = AdditiveBenchmarkRunner.Run(ManifestPath, SchemaPath);
        string reviewed = File.ReadAllText(ReportPath);

        Assert.Equal(first.ReportJson, second.ReportJson);
        Assert.Equal(first.Report.DeterministicResultSha256, second.Report.DeterministicResultSha256);
        Assert.Equal(NormalizeLineEndings(reviewed), NormalizeLineEndings(first.ReportJson));
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    /// <summary>
    /// Confirms a case cannot be marked ready while required references remain unknown.
    /// </summary>
    [Fact]
    public void Run_ReadyCaseWithNullReferences_IsInvalidInsteadOfPassing()
    {
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        JsonObject benchmarkCase = manifest["cases"]![0]!.AsObject();
        benchmarkCase["availability"] = "ready";
        benchmarkCase["missingDataReasons"] = new JsonArray();
        benchmarkCase["profileSha256"] = null;
        benchmarkCase["transform4x4"] = null;
        benchmarkCase["slicerReference"]!["modelPrintSeconds"] = null;
        benchmarkCase["slicerReference"]!["totalMachineSeconds"] = null;
        string temporaryManifest = Path.Combine(Path.GetTempPath(), $"additive-benchmark-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(temporaryManifest, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            BenchmarkRunResult result = AdditiveBenchmarkRunner.Run(temporaryManifest, SchemaPath);

            Assert.Equal("invalid", result.Status);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == "case.ready_missing_reference");
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == "case.ready_missing_transform");
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == "case.ready_missing_slicer_metric");
        }
        finally
        {
            File.Delete(temporaryManifest);
        }
    }

    /// <summary>
    /// Confirms the schema treats unknown values as null and rejects undeclared root fields.
    /// </summary>
    [Fact]
    public void ManifestSchema_VersionOne_UsesNullForUnknownValues()
    {
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(SchemaPath));
        JsonElement root = schema.RootElement;
        JsonElement nullableMetricTypes = root
            .GetProperty("$defs")
            .GetProperty("nullableMetric")
            .GetProperty("type");

        Assert.Equal("1.0", root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(nullableMetricTypes.EnumerateArray(), value => value.GetString() == "null");
        Assert.Contains(nullableMetricTypes.EnumerateArray(), value => value.GetString() == "number");
    }

    /// <summary>
    /// Confirms the executable validator enforces the schema's closed-object contract.
    /// </summary>
    [Fact]
    public void Run_UndeclaredProperties_AreInvalid()
    {
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        manifest["inventedProfile"] = "must-not-pass";
        manifest["cases"]![0]!["inventedConstant"] = 42;
        string temporaryManifest = Path.Combine(Path.GetTempPath(), $"additive-benchmark-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(temporaryManifest, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            BenchmarkRunResult result = AdditiveBenchmarkRunner.Run(temporaryManifest, SchemaPath);

            Assert.Equal("invalid", result.Status);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Path == "$.inventedProfile");
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Path == "$.cases[0].inventedConstant");
        }
        finally
        {
            File.Delete(temporaryManifest);
        }
    }

    /// <summary>
    /// Confirms customer CAD is not included in the checked-in benchmark asset directory.
    /// </summary>
    [Fact]
    public void TestAssets_DoNotCommitCustomerCad()
    {
        string[] forbiddenExtensions = [".stl", ".3mf", ".step", ".stp", ".obj"];
        string[] files = Directory.GetFiles(AssetDirectory, "*", SearchOption.AllDirectories);

        Assert.DoesNotContain(files, file => forbiddenExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
    }
}
