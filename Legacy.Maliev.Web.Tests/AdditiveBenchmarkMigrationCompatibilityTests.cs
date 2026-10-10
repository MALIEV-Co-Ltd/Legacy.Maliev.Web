using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maliev.AdditiveBenchmark;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises migration-visible tool behavior without starting a slicer or using customer files.</summary>
public sealed class AdditiveBenchmarkMigrationCompatibilityTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "TestAssets", "AdditiveBenchmark");

    /// <summary>Catches loss of v2 acceptance or an unintended change to the original report-v1 wire default.</summary>
    [Fact]
    public void Run_MatchedManifestWithBlockedEvidence_AcceptsV2AndPreservesReportV1()
    {
        var directory = CreateDirectory();
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Assets, "manifest.v1.json")))!;
            manifest["schemaVersion"] = "2.0";
            foreach (var item in manifest["cases"]!.AsArray())
            {
                var benchmarkCase = item!.AsObject();
                benchmarkCase["orientationPolicy"] = "fixed";
                benchmarkCase["inputArtifactSha256"] = new string('a', 64);
                benchmarkCase["profileBundleSha256"] = new string('b', 64);
                benchmarkCase["transform4x4"] = IdentityTransform();
                benchmarkCase["referenceTransform4x4"] = IdentityTransform();
                benchmarkCase["metricDefinitions"] = new JsonObject
                {
                    ["time"] = "synthetic total machine seconds",
                    ["material"] = "synthetic total grams",
                    ["support"] = "known-role positive extrusion",
                };
            }
            var input = Path.Combine(directory, "manifest.json");
            File.WriteAllText(input, manifest.ToJsonString());
            var result = AdditiveBenchmarkRunner.Run(input, Path.Combine(Assets, "manifest.v2.schema.json"));
            Assert.Equal("blocked", result.Status);
            Assert.Equal(2, result.ExitCode);
            Assert.Empty(result.Report.InvalidIssues);
            using var report = JsonDocument.Parse(result.ReportJson);
            Assert.Equal("1.0", report.RootElement.GetProperty("schemaVersion").GetString());
            // Historical source report v1 always reports this field as 1.0, even with manifest v2.
            // An accurate report-v2 adaptation is a separate contract change, not this migration.
            Assert.Equal("1.0", report.RootElement.GetProperty("manifestSchemaVersion").GetString());
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>Catches report-default or blocked-exit regressions in the actual CLI path.</summary>
    [Fact]
    public void Cli_V1Scaffold_WritesUnchangedGoldenReportAndReturnsBlocked()
    {
        var directory = CreateDirectory();
        try
        {
            var output = Path.Combine(directory, "report.json");
            var exit = AdditiveBenchmarkCli.Run([
                "--manifest", Path.Combine(Assets, "manifest.v1.json"),
                "--schema", Path.Combine(Assets, "manifest.v1.schema.json"),
                "--report", output]);
            Assert.Equal(2, exit);
            Assert.Equal(Normalize(File.ReadAllText(Path.Combine(Assets, "report.v1.json"))), Normalize(File.ReadAllText(output)));
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>Catches lost inventory dispatch, duplicate counting or exhausted-corpus exit semantics.</summary>
    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 2)]
    public void Cli_InventorySyntheticCorpus_DeduplicatesAndSignalsMissingEntries(int limit, int expectedExit)
    {
        var directory = CreateDirectory();
        try
        {
            var corpus = Path.Combine(directory, "corpus");
            Directory.CreateDirectory(corpus);
            File.WriteAllText(Path.Combine(corpus, "synthetic-name.stl"), "synthetic-one");
            File.WriteAllText(Path.Combine(corpus, "duplicate-name.obj"), "synthetic-one");
            File.WriteAllText(Path.Combine(corpus, "synthetic-two.3mf"), "synthetic-two");
            File.WriteAllText(Path.Combine(corpus, "ignored.txt"), "unsupported");
            var output = Path.Combine(directory, "inventory.json");
            var exit = AdditiveBenchmarkCli.Run(["inventory-corpus", "--root", corpus, "--output", output,
                "--consent-id", "synthetic-test-only", "--limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            Assert.Equal(expectedExit, exit);
            var json = File.ReadAllText(output);
            using var inventory = JsonDocument.Parse(json);
            var entries = inventory.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(2, entries.Length);
            Assert.Equal(2, entries.Select(entry => entry.GetProperty("sha256").GetString()).Distinct().Count());
            Assert.DoesNotContain(corpus, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("synthetic-name", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("duplicate-name", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>Catches a default-policy change that would silently stop orientation search for existing STL callers.</summary>
    [Fact]
    public void BuildArguments_ExistingStlCaller_RetainsSearchDefault()
    {
        var args = BambuStudioCli.BuildArguments(new BambuStudioSliceRequest(
            "synthetic.stl", "machine.json", "process.json", "filament.json", "output", "slice.3mf", "bed"));
        Assert.Contains("--orient", args);
        Assert.Contains("--arrange", args);
    }

    private static JsonArray IdentityTransform() => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "web-additive-compatibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
