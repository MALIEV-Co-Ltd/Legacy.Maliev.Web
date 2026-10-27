using Maliev.AdditiveBenchmark;
using System;
using System.Collections.Generic;
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
    private static readonly string MatchedSchemaPath = Path.Combine(AssetDirectory, "manifest.v2.schema.json");

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

    /// <summary>Prevents a reference generated in a different pose from certifying an estimate.</summary>
    [Fact]
    public void ReferenceComparability_DifferentPose_IsRejected()
    {
        double[] requested = [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
        ];
        double[] rotated = [
            0, -1, 0, 0,
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
        ];

        BenchmarkComparisonResult result = BenchmarkReferenceValidator.Validate(
            requested,
            rotated,
            "fixed");

        Assert.False(result.IsComparable);
        Assert.Equal("reference_pose_mismatch", result.ReasonCode);
    }

    /// <summary>Requires prepared 3MF input for a fixed-pose Bambu reference.</summary>
    [Fact]
    public void BambuCli_FixedPose_UsesPreparedProjectWithoutAutoOrientation()
    {
        var request = new BambuStudioSliceRequest(
            "fixture.3mf",
            "machine.json",
            "process.json",
            "filament.json",
            "output",
            "slice.3mf",
            "High Temp Plate",
            BambuStudioOrientationPolicy.Fixed);

        IReadOnlyList<string> arguments = BambuStudioCli.BuildArguments(request);

        Assert.DoesNotContain("--orient", arguments);
        Assert.DoesNotContain("--arrange", arguments);
    }

    /// <summary>Tracks extrusion through absolute/relative modes, resets, retractions and arc moves.</summary>
    [Fact]
    public void ParseGCodeEvidence_TracksModesResetsToolsArcsAndSupport()
    {
        const string gcode = """
            M82
            ; FEATURE: Outer wall
            G1 X10 E2
            G1 E1.5
            G92 E0
            T1
            ; FEATURE: Support
            G2 X20 Y10 I5 J0 E3
            M83
            G1 E-0.4
            G1 X30 E1.2
            ; FEATURE: Custom
            G1 X31 E0.2
            """;

        GCodeExtrusionEvidence result = BambuStudioResultParser.ParseGCodeEvidence(gcode);

        Assert.Equal(6.4, result.TotalPositiveExtrusionMillimetres, 6);
        Assert.Equal(4.2, result.SupportExtrusionMillimetres, 6);
        Assert.Null(result.SupportExtrusionMillimetresForCertification);
        Assert.Contains("ambiguous_feature_role", result.ReasonCodes);
    }

    /// <summary>Requires matched transforms and provenance for certification-ready v2 evidence.</summary>
    [Fact]
    public void Run_VersionTwoPoseMismatch_IsInvalid()
    {
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!;
        JsonNode schema = JsonNode.Parse(File.ReadAllText(SchemaPath))!;
        manifest["schemaVersion"] = "2.0";
        schema["properties"]!["schemaVersion"]!["const"] = "2.0";
        JsonObject benchmarkCase = manifest["cases"]![0]!.AsObject();
        benchmarkCase["orientationPolicy"] = "fixed";
        benchmarkCase["inputArtifactSha256"] = benchmarkCase["modelSha256"]!.DeepClone();
        benchmarkCase["profileBundleSha256"] = benchmarkCase["profileSha256"]!.DeepClone();
        benchmarkCase["referenceTransform4x4"] = new JsonArray(
            0, -1, 0, 0,
            1, 0, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
        benchmarkCase["metricDefinitions"] = new JsonObject
        {
            ["time"] = "Bambu total_predication seconds",
            ["material"] = "Bambu total_used_g grams",
            ["support"] = "feature-tagged positive extrusion converted with pinned filament geometry",
        };

        string temporaryManifest = Path.Combine(Path.GetTempPath(), $"additive-benchmark-v2-{Guid.NewGuid():N}.json");
        string temporarySchema = Path.Combine(Path.GetTempPath(), $"additive-benchmark-v2-schema-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(temporaryManifest, manifest.ToJsonString());
            File.WriteAllText(temporarySchema, schema.ToJsonString());

            BenchmarkRunResult result = AdditiveBenchmarkRunner.Run(temporaryManifest, temporarySchema);

            Assert.Equal("invalid", result.Status);
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == "reference_pose_mismatch");
            Assert.Contains(result.Report.InvalidIssues,
                issue => issue.Code == "reference_pose_mismatch"
                    && issue.Path == "$.cases[0].referenceTransform4x4");
        }
        finally
        {
            File.Delete(temporaryManifest);
            File.Delete(temporarySchema);
        }
    }

    /// <summary>Freezes the additional matched-input fields as a closed versioned schema.</summary>
    [Fact]
    public void ManifestSchema_VersionTwo_RequiresMatchedInputProvenance()
    {
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(MatchedSchemaPath));
        JsonElement benchmarkCase = schema.RootElement.GetProperty("$defs").GetProperty("benchmarkCase");
        string[] required = benchmarkCase.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        Assert.Equal("2.0", schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());
        Assert.False(benchmarkCase.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("orientationPolicy", required);
        Assert.Contains("referenceTransform4x4", required);
        Assert.Contains("inputArtifactSha256", required);
        Assert.Contains("profileBundleSha256", required);
        Assert.Contains("metricDefinitions", required);
    }

    /// <summary>Reports the consumed v2 contract while leaving historical v1 snapshots unchanged.</summary>
    [Fact]
    public void Run_VersionTwoReport_IdentifiesTheConsumedManifestVersion()
    {
        BenchmarkRunResult result = RunMatchedFixture();

        Assert.Equal("blocked", result.Status);
        Assert.Empty(result.Report.InvalidIssues);
        Assert.Equal("2.0", result.Report.SchemaVersion);
        Assert.Equal("2.0", result.Report.ManifestSchemaVersion);
        using JsonDocument wire = JsonDocument.Parse(result.ReportJson);
        Assert.Equal("2.0", wire.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("2.0", wire.RootElement.GetProperty("manifestSchemaVersion").GetString());
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AssetDirectory, "report.v2.schema.json")));
        Assert.Contains(
            schema.RootElement.GetProperty("properties").GetProperty("manifestSchemaVersion")
                .GetProperty("enum").EnumerateArray(),
            value => value.ValueKind == JsonValueKind.String && value.GetString() == "2.0");
    }

    /// <summary>A v2 case cannot claim matched inputs when either artifact digest is unknown.</summary>
    [Theory]
    [InlineData("inputArtifactSha256")]
    [InlineData("profileBundleSha256")]
    public void Run_VersionTwoUnknownMatchedDigest_IsInvalid(string property)
    {
        BenchmarkRunResult result = RunMatchedFixture(first => first[property] = null);

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Report.InvalidIssues,
            issue => issue.Code == "case.matched_sha_required" && issue.Path.EndsWith("." + property, StringComparison.Ordinal));
    }

    /// <summary>Invalid v2 availability remains representable in the emitted report contract.</summary>
    [Fact]
    public void Run_VersionTwoInvalidAvailability_EmitsInvalidCase()
    {
        BenchmarkRunResult result = RunMatchedFixture(first => first["availability"] = "unknown");

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Report.Cases,
            item => item.CaseId == "body4-pla-standard-q1" && item.Availability == "invalid");
    }

    /// <summary>Rejects a v2 manifest presented under the historical v1 schema pin.</summary>
    [Fact]
    public void Run_VersionTwoWithVersionOneSchema_IsInvalid()
    {
        BenchmarkRunResult result = RunMatchedFixture(schemaPath: SchemaPath);

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == "schema.manifest_version_mismatch");
    }

    private static BenchmarkRunResult RunMatchedFixture(Action<JsonObject>? mutateFirst = null, string? schemaPath = null)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
        manifest["schemaVersion"] = "2.0";
        JsonArray cases = manifest["cases"]!.AsArray();
        foreach (JsonNode? node in cases)
        {
            JsonObject item = node!.AsObject();
            item["orientationPolicy"] = "fixed";
            item["inputArtifactSha256"] = item["modelSha256"]!.DeepClone();
            item["profileBundleSha256"] = item["profileSha256"]!.DeepClone();
            item["referenceTransform4x4"] = item["transform4x4"]!.DeepClone();
            item["metricDefinitions"] = new JsonObject
            {
                ["time"] = "Bambu total_predication seconds",
                ["material"] = "Bambu total_used_g grams",
                ["support"] = "feature-tagged positive extrusion with pinned filament geometry",
            };
        }
        mutateFirst?.Invoke(cases[0]!.AsObject());
        string temporary = Path.Combine(Path.GetTempPath(), $"matched-benchmark-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(temporary, manifest.ToJsonString());
            return AdditiveBenchmarkRunner.Run(temporary, schemaPath ?? MatchedSchemaPath);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
    /// <summary>Only the documented search policy bypasses fixed-pose comparison.</summary>
    [Theory]
    [InlineData("search")]
    [InlineData("SEARCH")]
    [InlineData("SeArCh")]
    public void ReferenceComparability_SearchPolicy_IsComparable(string policy)
    {
        double[] requested = Enumerable.Repeat(0d, 16).ToArray();
        double[] observed = Enumerable.Repeat(1d, 16).ToArray();

        BenchmarkComparisonResult result = BenchmarkReferenceValidator.Validate(requested, observed, policy);

        Assert.True(result.IsComparable);
        Assert.Null(result.ReasonCode);
    }

    /// <summary>Fixed-pose comparison remains case-insensitive and rejects mismatches.</summary>
    [Theory]
    [InlineData("fixed")]
    [InlineData("FIXED")]
    public void ReferenceComparability_FixedPolicy_RejectsMismatchRegardlessOfCase(string policy)
    {
        double[] requested = Enumerable.Repeat(0d, 16).ToArray();
        double[] observed = Enumerable.Repeat(1d, 16).ToArray();

        BenchmarkComparisonResult result = BenchmarkReferenceValidator.Validate(requested, observed, policy);

        Assert.False(result.IsComparable);
        Assert.Equal("reference_pose_mismatch", result.ReasonCode);
    }

    /// <summary>Unknown and missing policies cannot certify reference inputs.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("searching")]
    public void ReferenceComparability_InvalidPolicy_IsNotComparable(string? policy)
    {
        double[] transform = Enumerable.Repeat(0d, 16).ToArray();

        BenchmarkComparisonResult result = BenchmarkReferenceValidator.Validate(transform, transform, policy!);

        Assert.False(result.IsComparable);
        Assert.Equal("reference_orientation_policy_invalid", result.ReasonCode);
    }

    /// <summary>Invalid policy reports identify the policy, not a false pose mismatch.</summary>
    [Fact]
    public void Run_VersionTwoUnknownOrientationPolicy_IsInvalid()
    {
        BenchmarkRunResult result = RunMatchedFixture(first => first["orientationPolicy"] = "unknown");

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == "case.orientation_policy");
        Assert.Contains(result.Report.InvalidIssues,
            issue => issue.Code == "reference_orientation_policy_invalid"
                && issue.Path == "$.cases[0].orientationPolicy"
                && issue.Message == "Orientation policy must be fixed or search.");
    }

    /// <summary>Manifest policy values remain case-sensitive even though the helper accepts case variants.</summary>
    [Fact]
    public void Run_VersionTwoUppercaseOrientationPolicy_IsInvalid()
    {
        BenchmarkRunResult result = RunMatchedFixture(first => first["orientationPolicy"] = "SEARCH");

        Assert.Equal("invalid", result.Status);
        Assert.Contains(result.Report.InvalidIssues,
            issue => issue.Code == "case.orientation_policy" && issue.Path == "$.cases[0].orientationPolicy");
        Assert.DoesNotContain(result.Report.InvalidIssues, issue => issue.Code == "reference_orientation_policy_invalid");
    }

}
