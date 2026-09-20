using Maliev.AdditiveBenchmark;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>
/// Covers fail-closed resolution of Bambu Studio's inherited JSON presets.
/// </summary>
public sealed class BambuStudioProfileResolverTests
{
    /// <summary>
    /// Confirms inherited and included values are materialized while leaf values retain priority.
    /// </summary>
    [Fact]
    public void Resolve_InheritedAndIncludedPreset_MaterializesCompleteSettings()
    {
        using TemporaryProfileDirectory profiles = new();
        profiles.Write("base.json", """
            {"type":"filament","name":"base","from":"system","filament_density":["1.02"],"filament_type":["ASA"],"speed":["10"]}
            """);
        profiles.Write("template.json", """
            {"name":"template","machine_start_gcode":"G28","speed":["20"]}
            """);
        string leafPath = profiles.Write("leaf.json", """
            {"type":"filament","name":"PolyLite ASA","from":"system","inherits":"base","include":["template"],"speed":["30"]}
            """);

        ResolvedBambuProfile result = BambuStudioProfileResolver.Resolve(leafPath, [profiles.Path]);

        Assert.Equal(["base", "template", "PolyLite ASA"], result.SourceProfileNames);
        Assert.Equal("ASA", result.Settings["filament_type"]![0]!.GetValue<string>());
        Assert.Equal("1.02", result.Settings["filament_density"]![0]!.GetValue<string>());
        Assert.Equal("G28", result.Settings["machine_start_gcode"]!.GetValue<string>());
        Assert.Equal("30", result.Settings["speed"]![0]!.GetValue<string>());
        Assert.False(result.Settings.ContainsKey("include"));
        Assert.Matches("^[A-F0-9]{64}$", result.Sha256);
    }

    /// <summary>
    /// Confirms an unresolved inheritance link blocks slicing instead of falling back to defaults.
    /// </summary>
    [Fact]
    public void Resolve_MissingInheritedPreset_ThrowsActionableError()
    {
        using TemporaryProfileDirectory profiles = new();
        string leafPath = profiles.Write("leaf.json", """
            {"type":"process","name":"quality","from":"system","inherits":"missing-base"}
            """);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => BambuStudioProfileResolver.Resolve(leafPath, [profiles.Path]));

        Assert.Contains("missing-base", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_IdenticalDuplicateNamedPreset_IsAccepted()
    {
        using TemporaryProfileDirectory profiles = new();
        string duplicate = """
            {"type":"filament","name":"base","from":"system","filament_type":["PA"]}
            """;
        profiles.Write("base-a.json", duplicate);
        profiles.Write("base-b.json", duplicate);
        string leafPath = profiles.Write("leaf.json", """
            {"type":"filament","name":"eSUN PA","from":"user","inherits":"base"}
            """);

        ResolvedBambuProfile result = BambuStudioProfileResolver.Resolve(leafPath, [profiles.Path]);

        Assert.Equal("PA", result.Settings["filament_type"]![0]!.GetValue<string>());
    }

    /// <summary>
    /// Confirms the command line uses the installed Windows CLI's verified STL argument shape.
    /// </summary>
    [Fact]
    public void BuildArguments_StlJob_UsesExplicitOrientationBedAndAllPlateSlice()
    {
        BambuStudioSliceRequest request = new(
            ModelPath: @"Z:\280te.stl",
            MachineProfilePath: @"C:\profiles\machine.json",
            ProcessProfilePath: @"C:\profiles\process.json",
            FilamentProfilePath: @"C:\profiles\filament.json",
            OutputDirectory: @"C:\jobs\quote-1",
            OutputFileName: "result.3mf",
            BedType: "High Temp Plate");

        IReadOnlyList<string> arguments = BambuStudioCli.BuildArguments(request);
        List<string> argumentList = arguments.ToList();

        Assert.Contains("--orient", arguments);
        Assert.Equal("1", arguments[argumentList.IndexOf("--orient") + 1]);
        Assert.Contains("--curr-bed-type", arguments);
        Assert.Equal("High Temp Plate", arguments[argumentList.IndexOf("--curr-bed-type") + 1]);
        Assert.Contains("--slice", arguments);
        Assert.Equal("0", arguments[argumentList.IndexOf("--slice") + 1]);
        Assert.Equal("result.3mf", arguments[argumentList.IndexOf("--export-3mf") + 1]);
        Assert.DoesNotContain(@"C:\jobs\quote-1\result.3mf", arguments);
    }

    /// <summary>
    /// Confirms operators can create a reviewed complete preset without editing vendor files.
    /// </summary>
    [Fact]
    public void Cli_ResolveProfile_WritesCompletePresetAndDigest()
    {
        using TemporaryProfileDirectory profiles = new();
        profiles.Write("base.json", """
            {"type":"process","name":"base","from":"system","layer_height":"0.12"}
            """);
        string leafPath = profiles.Write("leaf.json", """
            {"type":"process","name":"quality","from":"system","inherits":"base"}
            """);
        string outputPath = System.IO.Path.Combine(profiles.Path, "resolved", "quality.json");

        int exitCode = AdditiveBenchmarkCli.Run(
        [
            "resolve-profile",
            "--profile", leafPath,
            "--search-root", profiles.Path,
            "--output", outputPath,
        ]);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        using JsonDocument resolved = JsonDocument.Parse(File.ReadAllText(outputPath));
        Assert.Equal("0.12", resolved.RootElement.GetProperty("layer_height").GetString());
    }

    /// <summary>
    /// Confirms a zero exit/result code without a sliced plate is rejected as false success.
    /// </summary>
    [Fact]
    public void ParseResult_SuccessWithoutSlicedPlate_ThrowsInsteadOfPublishingZeros()
    {
        const string resultJson = """
            {"return_code":0,"error_string":"Success.","plate_index":2,"layer_height":0,"wall_loops":0}
            """;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => BambuStudioResultParser.Parse(resultJson));

        Assert.Contains("sliced_plates", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Confirms authoritative material and machine-time fields are retained separately.
    /// </summary>
    [Fact]
    public void ParseResult_ValidSlice_ReturnsModelTotalAndMaterialMetrics()
    {
        const string resultJson = """
            {
              "return_code":0,
              "error_string":"Success.",
              "layer_height":0.12,
              "sparse_infill_density":15,
              "wall_loops":2,
              "sliced_plates":[{
                "id":1,
                "main_predication":2700.9624,
                "total_predication":3281.0957,
                "filaments":[{"id":1,"filament_id":"GFB61","main_used_g":9.7888,"total_used_g":9.7889}]
              }]
            }
            """;

        BambuStudioSliceMetrics metrics = BambuStudioResultParser.Parse(resultJson);

        Assert.Equal(2700.9624, metrics.ModelSeconds, precision: 4);
        Assert.Equal(3281.0957, metrics.TotalSeconds, precision: 4);
        Assert.Equal(580.1333, metrics.PreparationSeconds, precision: 4);
        Assert.Equal(9.7889, metrics.TotalMaterialGrams, precision: 4);
        Assert.Equal(0.12, metrics.LayerHeightMillimetres, precision: 2);
        Assert.Equal(15, metrics.SparseInfillPercent);
        Assert.Equal(2, metrics.WallLoops);
    }

    private sealed class TemporaryProfileDirectory : IDisposable
    {
        public TemporaryProfileDirectory()
        {
            this.Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"bambu-profile-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.Path);
        }

        public string Path { get; }

        public string Write(string fileName, string content)
        {
            string path = System.IO.Path.Combine(this.Path, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(this.Path, recursive: true);
        }
    }
}
