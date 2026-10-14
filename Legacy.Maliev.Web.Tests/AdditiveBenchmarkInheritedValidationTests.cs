using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maliev.AdditiveBenchmark;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises inherited validator defects through real runner and CLI file boundaries.</summary>
public sealed class AdditiveBenchmarkInheritedValidationTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "TestAssets", "AdditiveBenchmark");

    /// <summary>Catches missing/null/non-string/malformed matched-hash acceptance in both public entry points.</summary>
    [Theory]
    [InlineData("inputArtifactSha256", "missing")]
    [InlineData("inputArtifactSha256", "null")]
    [InlineData("inputArtifactSha256", "\"\"")]
    [InlineData("inputArtifactSha256", "\"not-a-digest\"")]
    [InlineData("inputArtifactSha256", "42")]
    [InlineData("inputArtifactSha256", "true")]
    [InlineData("inputArtifactSha256", "{}")]
    [InlineData("profileBundleSha256", "missing")]
    [InlineData("profileBundleSha256", "null")]
    [InlineData("profileBundleSha256", "\"\"")]
    [InlineData("profileBundleSha256", "\"not-a-digest\"")]
    [InlineData("profileBundleSha256", "42")]
    [InlineData("profileBundleSha256", "true")]
    [InlineData("profileBundleSha256", "{}")]
    public void RunAndCli_InvalidMatchedHash_ReturnStructuredInvalid(string field, string replacement)
    {
        var manifest = Manifest("2.0");
        var benchmarkCase = manifest["cases"]![0]!.AsObject();
        if (replacement == "missing") benchmarkCase.Remove(field);
        else benchmarkCase[field] = JsonNode.Parse(replacement);
        AssertInvalid(manifest, "2.0", replacement == "missing" ? "case.sha_missing" : "case.sha_invalid",
            "$.cases[0]." + field);
    }

    /// <summary>Catches typed-value exceptions for both sixteen-element matched transforms.</summary>
    [Theory]
    [InlineData("transform4x4", "null")]
    [InlineData("transform4x4", "\"not-a-number\"")]
    [InlineData("transform4x4", "true")]
    [InlineData("transform4x4", "{}")]
    [InlineData("referenceTransform4x4", "null")]
    [InlineData("referenceTransform4x4", "\"not-a-number\"")]
    [InlineData("referenceTransform4x4", "true")]
    [InlineData("referenceTransform4x4", "{}")]
    public void RunAndCli_NonnumericMatchedTransformElement_ReturnStructuredInvalid(string field, string replacement)
    {
        var manifest = Manifest("2.0");
        var values = manifest["cases"]![0]![field]!.AsArray();
        values[0] = JsonNode.Parse(replacement);
        Assert.Equal(16, values.Count);
        AssertInvalid(manifest, "2.0", "case.reference_transform_invalid", "$.cases[0].referenceTransform4x4");
    }

    /// <summary>Catches mismatched supported schema/manifest versions in both directions.</summary>
    [Theory]
    [InlineData("1.0", "2.0")]
    [InlineData("2.0", "1.0")]
    public void RunAndCli_DifferentSupportedManifestAndSchemaVersions_ReturnStructuredInvalid(string manifestVersion, string schemaVersion)
    {
        AssertInvalid(Manifest(manifestVersion), schemaVersion, "schema.manifest_version_mismatch", "$.schemaVersion");
    }

    /// <summary>Catches accidental rejection of either matched version while retaining blocked/report-v1 semantics.</summary>
    [Theory]
    [InlineData("1.0")]
    [InlineData("2.0")]
    public void Run_MatchingSupportedVersions_PreservesBlockedScaffold(string version)
    {
        InDirectory(directory =>
        {
            var input = Path.Combine(directory, "manifest.json");
            File.WriteAllText(input, Manifest(version).ToJsonString());
            var result = AdditiveBenchmarkRunner.Run(input, Schema(version));
            Assert.Equal("blocked", result.Status);
            Assert.Equal(2, result.ExitCode);
            Assert.Empty(result.Report.InvalidIssues);
            Assert.Equal("1.0", result.Report.SchemaVersion);
            var output = Path.Combine(directory, "report.json");
            Assert.Equal(2, AdditiveBenchmarkCli.Run(["--manifest", input, "--schema", Schema(version), "--report", output]));
            using var report = JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal("blocked", report.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, report.RootElement.GetProperty("invalidIssues").GetArrayLength());
        });
    }

    private static void AssertInvalid(JsonNode manifest, string schemaVersion, string reason, string path)
    {
        InDirectory(directory =>
        {
            var input = Path.Combine(directory, "manifest.json");
            var output = Path.Combine(directory, "report.json");
            File.WriteAllText(input, manifest.ToJsonString());
            // An unhandled exception fails the test; invalid JSON values must instead yield this report.
            var result = AdditiveBenchmarkRunner.Run(input, Schema(schemaVersion));
            Assert.Equal("invalid", result.Status);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == reason && issue.Path == path);
            var exit = AdditiveBenchmarkCli.Run(["--manifest", input, "--schema", Schema(schemaVersion), "--report", output]);
            Assert.Equal(1, exit);
            using var report = JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal("invalid", report.RootElement.GetProperty("status").GetString());
            Assert.Equal("1.0", report.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Contains(report.RootElement.GetProperty("invalidIssues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == reason && issue.GetProperty("path").GetString() == path);
        });
    }

    private static JsonNode Manifest(string version)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Assets, "manifest.v1.json")))!;
        manifest["schemaVersion"] = version;
        if (version == "2.0")
        {
            foreach (var item in manifest["cases"]!.AsArray())
            {
                var benchmarkCase = item!.AsObject();
                benchmarkCase["orientationPolicy"] = "fixed";
                benchmarkCase["inputArtifactSha256"] = new string('a', 64);
                benchmarkCase["profileBundleSha256"] = new string('b', 64);
                benchmarkCase["transform4x4"] = Transform();
                benchmarkCase["referenceTransform4x4"] = Transform();
                benchmarkCase["metricDefinitions"] = new JsonObject
                {
                    ["time"] = "synthetic seconds",
                    ["material"] = "synthetic grams",
                    ["support"] = "known-role positive extrusion",
                };
            }
        }
        return manifest;
    }

    private static JsonArray Transform() => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    private static string Schema(string version) => Path.Combine(Assets, version == "1.0" ? "manifest.v1.schema.json" : "manifest.v2.schema.json");
    private static void InDirectory(Action<string> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "web-additive-validator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { run(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
