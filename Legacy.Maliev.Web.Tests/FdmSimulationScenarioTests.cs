// <copyright file="FdmSimulationScenarioTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests;

using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

/// <summary>Freezes deterministic orientation, fit, packing, and partition scenarios.</summary>
public sealed class FdmSimulationScenarioTests
{
    /// <summary>Generates bounded deterministic candidates and bypasses search for a functional lock.</summary>
    [Fact]
    public void GenerateCandidates_IsDeterministicAndHonorsFixedPose()
    {
        NormalizedMesh mesh = Mesh(10, 8, 6);

        string[] first = OrientationPlanner.GenerateCandidates(mesh).Select(pose => pose.Id).ToArray();
        string[] second = OrientationPlanner.GenerateCandidates(mesh).Select(pose => pose.Id).ToArray();
        var locked = new Pose("functional-lock", Matrix4x4.CreateRotationZ(0.123f));

        Assert.Equal(first, second);
        Assert.InRange(first.Length, 1, 32);
        Assert.Equal("functional-lock", Assert.Single(OrientationPlanner.GenerateCandidates(mesh, locked)).Id);
    }

    /// <summary>Selects a diagonal pose when orthogonal XY poses exceed the usable plate and height blocks vertical poses.</summary>
    [Fact]
    public void Evaluate_PartFitsOnlyDiagonally_SelectsFeasiblePose()
    {
        MachineScenario result = MachineScenarioPlanner.Evaluate(
            Mesh(20, 2, 2),
            Profile(16, 16, 5),
            1,
            Budget());

        Assert.NotNull(result.SelectedPose);
        Assert.Contains("45", result.SelectedPose.Id, StringComparison.Ordinal);
        Assert.Single(result.Plates);
    }

    /// <summary>Rejects a CAD envelope whose brim exceeds the usable plate.</summary>
    [Fact]
    public void Evaluate_BrimExceedsPlate_RequiresMachineOrPartition()
    {
        MachineScenario result = MachineScenarioPlanner.Evaluate(
            Mesh(15.8f, 15.8f, 2),
            Profile(16, 16, 5, brimWidth: 1),
            1,
            Budget());

        Assert.Null(result.SelectedPose);
        Assert.Contains("requires_machine_or_partition", result.Diagnostics);
    }

    /// <summary>Honors excluded bed zones during packing, not just the rectangular machine bounds.</summary>
    [Fact]
    public void Evaluate_ExcludedZone_PreventsFalseFit()
    {
        MachineScenario result = MachineScenarioPlanner.Evaluate(
            Mesh(20, 16, 2),
            Profile(20, 20, 5, excludedZones: "0,0,20,5"),
            1,
            Budget());

        Assert.Null(result.SelectedPose);
        Assert.Contains(result.Orientations.SelectMany(item => item.ReasonCodes), reason => reason == "machine_excluded_zone_collision");
    }

    /// <summary>Never scales a 500 or 1,500 mm part into an X1C quote.</summary>
    [Theory]
    [InlineData(500)]
    [InlineData(1500)]
    public void Evaluate_OversizePart_ProducesReviewedPartition(double size)
    {
        MachineScenario result = MachineScenarioPlanner.Evaluate(
            Mesh((float)size, 100, 100),
            Profile(256, 256, 256),
            1,
            Budget(maximumLayers: 20_000));

        Assert.Null(result.SelectedPose);
        Assert.NotNull(result.Partition);
        Assert.Contains("requires_machine_or_partition", result.Diagnostics);
        Assert.Contains("assembly_review_required", result.Partition.ReasonCodes);
    }

    /// <summary>Crosses plate capacity deterministically for quantities 1, 2, and 10.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(10, 3)]
    public void Evaluate_Quantity_PacksAcrossExpectedPlateCount(int quantity, int expectedPlates)
    {
        MachineScenario result = MachineScenarioPlanner.Evaluate(
            Mesh(10, 10, 2),
            Profile(25, 25, 10),
            quantity,
            Budget());

        Assert.Equal(expectedPlates, result.Plates.Count);
        Assert.Equal(quantity, result.Plates.Sum(plate => plate.Quantity));
        Assert.All(result.Plates, plate => Assert.True(plate.Result.Motion.PreparationSeconds > 0));
    }

    private static AnalysisBudget Budget(int maximumLayers = 5000) => new(1000, maximumLayers, 2_000_000, default);

    private static NormalizedMesh Mesh(float width, float depth, float height) => MeshNormalizer.Normalize(
        new MeshInput(Box(width, depth, height), 1, Matrix4x4.Identity),
        new GeometryTolerance(0.001, 0.02));

    private static IReadOnlyList<MeshTriangle> Box(float x, float y, float z)
    {
        Vector3 p000 = new(0, 0, 0); Vector3 p100 = new(x, 0, 0); Vector3 p110 = new(x, y, 0); Vector3 p010 = new(0, y, 0);
        Vector3 p001 = new(0, 0, z); Vector3 p101 = new(x, 0, z); Vector3 p111 = new(x, y, z); Vector3 p011 = new(0, y, z);
        return
        [
            new(p000, p110, p100), new(p000, p010, p110), new(p001, p101, p111), new(p001, p111, p011),
            new(p000, p100, p101), new(p000, p101, p001), new(p010, p011, p111), new(p010, p111, p110),
            new(p000, p001, p011), new(p000, p011, p010), new(p100, p110, p111), new(p100, p111, p101),
        ];
    }

    private static ResolvedSimulationProfile Profile(
        double width,
        double depth,
        double height,
        double brimWidth = 0,
        string excludedZones = "none") => ResolvedSimulationProfile.Resolve(
        BuildPreference.Standard,
        new Dictionary<string, string>
        {
            ["machine_id"] = "TEST-MACHINE",
            ["supported_material_ids"] = "MODEL",
            ["excluded_zones"] = excludedZones,
            ["nozzle_diameter"] = "0.4",
            ["printable_width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["printable_depth"] = depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["printable_height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maximum_xy_speed"] = "500",
            ["maximum_z_speed"] = "20",
            ["printing_acceleration"] = "10000",
            ["travel_acceleration"] = "12000",
        },
        Process(brimWidth),
        new Dictionary<string, string>
        {
            ["material_id"] = "MODEL",
            ["filament_density"] = "1",
            ["filament_max_volumetric_speed"] = "12",
            ["support_material_id"] = "model",
            ["support_material_density"] = "1",
            ["support_interface_material_id"] = "model",
            ["support_interface_material_density"] = "1",
            ["support_max_volumetric_speed"] = "12",
            ["support_interface_max_volumetric_speed"] = "8",
        },
        new string('A', 64));

    private static IReadOnlyDictionary<string, string> Process(double brimWidth) => new Dictionary<string, string>
    {
        ["nozzle_diameter"] = "0.4",
        ["layer_height"] = "0.2",
        ["wall_loops"] = "2",
        ["inner_wall_speed"] = "200",
        ["outer_wall_speed"] = "50%",
        ["sparse_infill_density"] = "15%",
        ["sparse_infill_pattern"] = "rectilinear",
        ["outer_wall_line_width"] = "0.42",
        ["inner_wall_line_width"] = "0.45",
        ["sparse_infill_line_width"] = "0.45",
        ["solid_infill_line_width"] = "0.42",
        ["gap_fill_min_width"] = "0.2",
        ["gap_fill_max_width"] = "0.8",
        ["top_shell_layers"] = "1",
        ["bottom_shell_layers"] = "1",
        ["bridge_max_span"] = "25",
        ["bridge_anchor_length"] = "1.2",
        ["overhang_speed_multiplier"] = "50%",
        ["support_enabled"] = "true",
        ["support_mode"] = "normal",
        ["support_build_plate_only"] = "true",
        ["support_overhang_angle"] = "30",
        ["support_top_z_gap"] = "0.2",
        ["support_bottom_z_gap"] = "0.2",
        ["support_xy_clearance"] = "0.35",
        ["support_body_density"] = "15%",
        ["support_interface_density"] = "80%",
        ["support_interface_layers"] = "2",
        ["support_body_line_width"] = "0.45",
        ["support_interface_line_width"] = "0.42",
        ["support_expansion"] = "0",
        ["tree_tip_spacing"] = "3",
        ["tree_max_branch_angle"] = "35",
        ["tree_min_radius"] = "2",
        ["tree_max_radius"] = "4",
        ["tree_radius_per_tip"] = "0.2",
        ["tree_wall_count"] = "2",
        ["tree_hollow_density"] = "0%",
        ["gap_fill_speed"] = "150",
        ["top_surface_speed"] = "100",
        ["bottom_surface_speed"] = "100",
        ["internal_solid_infill_speed"] = "150",
        ["sparse_infill_speed"] = "200",
        ["bridge_speed"] = "50",
        ["support_speed"] = "150",
        ["support_interface_speed"] = "80",
        ["first_layer_speed"] = "50",
        ["travel_speed"] = "300",
        ["first_layer_speed_multiplier"] = "50%",
        ["minimum_layer_time"] = "5",
        ["retract_length"] = "0.8",
        ["retract_speed"] = "40",
        ["setup_time"] = "30",
        ["nozzle_heat_time"] = "60",
        ["bed_heat_time"] = "90",
        ["tool_change_time"] = "12",
        ["brim_width"] = brimWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
