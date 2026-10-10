using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maliev.AdditiveBenchmark;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises malformed JSON object boundaries through the actual runner and CLI.</summary>
public sealed class AdditiveBenchmarkObjectShapeTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "TestAssets", "AdditiveBenchmark");

    /// <summary>Supplies all five non-object JSON kinds at each newly guarded boundary.</summary>
    public static IEnumerable<object[]> InvalidObjectCases()
    {
        string[] replacements = ["null", "\"scalar\"", "42", "true", "[]"];
        foreach (var replacement in replacements)
        {
            yield return ["schemaRoot", replacement, "schema.root_type", "$schema"];
            yield return ["schemaProperties", replacement, "schema.version_contract_missing", "$.properties.schemaVersion.const"];
            yield return ["schemaVersion", replacement, "schema.version_contract_missing", "$.properties.schemaVersion.const"];
            yield return ["provenance", replacement, "case.provenance_record_type", "$.cases[0].provenance[0]"];
            yield return ["blocker", replacement, "blocker.type", "$.evidenceBlockers[0]"];
        }
    }

    /// <summary>Catches non-object typed-access exceptions without relying on a generic exception catch.</summary>
    [Theory]
    [MemberData(nameof(InvalidObjectCases))]
    public void RunAndCli_NonObjectBoundary_ProduceStructuredInvalid(string boundary, string replacement, string reason, string path)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Assets, "manifest.v1.json")))!;
        JsonNode? schema = JsonNode.Parse(File.ReadAllText(Path.Combine(Assets, "manifest.v1.schema.json")))!;
        switch (boundary)
        {
            case "schemaRoot": schema = JsonNode.Parse(replacement); break;
            case "schemaProperties": schema!["properties"] = JsonNode.Parse(replacement); break;
            case "schemaVersion": schema!["properties"]!["schemaVersion"] = JsonNode.Parse(replacement); break;
            case "provenance": manifest["cases"]![0]!["provenance"]![0] = JsonNode.Parse(replacement); break;
            case "blocker": manifest["evidenceBlockers"]![0] = JsonNode.Parse(replacement); break;
            default: throw new ArgumentException("Unknown test boundary.", nameof(boundary));
        }
        AssertInvalid(manifest, schema, reason, path);
    }

    /// <summary>Retains invalid-report behavior for absent schema-object paths as well as wrong kinds.</summary>
    [Theory]
    [InlineData("properties")]
    [InlineData("schemaVersion")]
    public void RunAndCli_MissingNestedSchemaObject_ProduceStructuredInvalid(string field)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Assets, "manifest.v1.json")))!;
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(Assets, "manifest.v1.schema.json")))!;
        if (field == "properties") schema.AsObject().Remove(field);
        else schema["properties"]!.AsObject().Remove(field);
        AssertInvalid(manifest, schema, "schema.version_contract_missing", "$.properties.schemaVersion.const");
    }

    private static void AssertInvalid(JsonNode manifest, JsonNode? schema, string reason, string path)
    {
        var directory = Path.Combine(Path.GetTempPath(), "web-additive-object-shapes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            var schemaPath = Path.Combine(directory, "schema.json");
            var reportPath = Path.Combine(directory, "report.json");
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            File.WriteAllText(schemaPath, schema?.ToJsonString() ?? "null");
            var result = AdditiveBenchmarkRunner.Run(manifestPath, schemaPath);
            Assert.Equal("invalid", result.Status);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(result.Report.InvalidIssues, issue => issue.Code == reason && issue.Path == path);
            Assert.Equal(1, AdditiveBenchmarkCli.Run(["--manifest", manifestPath, "--schema", schemaPath, "--report", reportPath]));
            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.Equal("invalid", report.RootElement.GetProperty("status").GetString());
            Assert.Equal("1.0", report.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Contains(report.RootElement.GetProperty("invalidIssues").EnumerateArray(), issue =>
                issue.GetProperty("code").GetString() == reason && issue.GetProperty("path").GetString() == path);
        }
        finally { Directory.Delete(directory, true); }
    }
}
