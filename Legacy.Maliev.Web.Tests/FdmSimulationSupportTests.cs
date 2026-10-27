// <copyright file="FdmSimulationSupportTests.cs" company="Maliev Company Limited">
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

/// <summary>Freezes unioned, connected, and auditable normal-support deposition.</summary>
public sealed class FdmSimulationSupportTests
{
    /// <summary>Produces no support for vertically aligned model layers.</summary>
    [Fact]
    public void Build_VerticalCube_ProducesNoSupport()
    {
        LayerContours[] layers = Enumerable.Range(0, 5).Select(index => Layer([Rectangle(0, 0, 10, 10)], index)).ToArray();
        ResolvedSimulationProfile profile = Profile();
        ModelPathPlan model = ModelPathPlanner.Build(layers, profile);

        SupportPlan result = SupportPlanner.Build(layers, model, profile);

        Assert.Empty(result.Paths);
        Assert.Empty(result.Regions);
        Assert.Empty(result.UnsatisfiedDemand);
    }

    /// <summary>Creates positive support body/interface deposition for a one-ended cantilever.</summary>
    [Fact]
    public void Build_Cantilever_ProducesAuditableSupport()
    {
        LayerContours[] layers =
        [
            Layer([Rectangle(0, 0, 10, 10)], 0),
            Layer([Rectangle(0, 0, 20, 10)], 1),
            Layer([Rectangle(0, 0, 20, 10)], 2),
            Layer([Rectangle(0, 0, 20, 10)], 3),
        ];
        ResolvedSimulationProfile profile = Profile();
        ModelPathPlan model = ModelPathPlanner.Build(layers, profile);

        SupportPlan result = SupportPlanner.Build(layers, model, profile);

        Assert.NotEmpty(result.Paths);
        Assert.Contains(result.Paths, path => path.Role == ExtrusionRole.SupportInterface);
        Assert.True(result.Regions.Sum(region => region.InterfaceDepositedMm3 + region.BodyDepositedMm3) > 0);
    }

    /// <summary>Unions overlapping roof projections so shared support is not billed twice.</summary>
    [Fact]
    public void Build_OverlappingRoofs_UnionsSharedTower()
    {
        LayerContours[] layers = Enumerable.Range(0, 6).Select(index => Layer([], index)).ToArray();
        var model = new ModelPathPlan(
            [],
            [],
            [new LayerMask(layers[^1].ZMm, [Rectangle(0, 0, 10, 10)]), new LayerMask(layers[^1].ZMm, [Rectangle(5, 0, 15, 10)])],
            []);

        SupportPlan result = SupportPlanner.Build(layers, model, Profile());

        double envelope = Assert.Single(result.Regions).EnvelopeMm3;
        Assert.Equal(15 * 10 * 0.2 * 5, envelope, 3);
    }

    /// <summary>Retains unsatisfied demand when support is disabled.</summary>
    [Fact]
    public void Build_SupportOff_ReportsDemandInsteadOfZeroSuccess()
    {
        LayerContours[] layers = Enumerable.Range(0, 3).Select(index => Layer([], index)).ToArray();
        var model = new ModelPathPlan([], [], [new LayerMask(layers[^1].ZMm, [Rectangle(0, 0, 10, 10)])], []);

        SupportPlan result = SupportPlanner.Build(layers, model, Profile(supportEnabled: false));

        Assert.Empty(result.Paths);
        Assert.NotEmpty(result.UnsatisfiedDemand);
        Assert.Contains("support_disabled_with_demand", result.Diagnostics);
    }

    /// <summary>Flags a support column blocked from reaching the build plate by model collision.</summary>
    [Fact]
    public void Build_InternalCeiling_ReportsAccessibilityFailure()
    {
        LayerContours[] layers =
        [
            Layer([Rectangle(0, 0, 20, 20)], 0),
            Layer([], 1),
            Layer([], 2),
            Layer([], 3),
        ];
        var model = new ModelPathPlan([], [], [new LayerMask(layers[^1].ZMm, [Rectangle(5, 5, 15, 15)])], []);

        SupportPlan result = SupportPlanner.Build(layers, model, Profile());

        Assert.NotEmpty(result.UnsatisfiedDemand);
        Assert.Contains("support_accessibility_unresolved", result.Diagnostics);
    }

    /// <summary>Pins independent body/interface density mass arithmetic.</summary>
    [Fact]
    public void CalculateMass_UsesDepositedVolumesAndSeparateDensities()
    {
        Assert.Equal(2.232, SupportPlanner.CalculateMassGrams(1500, 1.24, 300, 1.24), 6);
        Assert.Equal(2.1, SupportPlanner.CalculateMassGrams(1500, 1.2, 300, 1.0), 6);
    }

    /// <summary>Merges nearby descending tips deterministically while retaining provisional status.</summary>
    [Fact]
    public void BuildTree_NearbyTips_MergeIntoSharedTrunk()
    {
        LayerContours[] layers = Enumerable.Range(0, 12).Select(index => Layer([], index)).ToArray();
        var model = new ModelPathPlan(
            [],
            [],
            [new LayerMask(layers[^1].ZMm, [Rectangle(0, 0, 1, 1), Rectangle(2, 0, 3, 1)])],
            []);

        SupportPlan first = TreeSupportPlanner.Build(EmptyMesh(), layers, model, Profile(supportMode: "tree"));
        SupportPlan repeated = TreeSupportPlanner.Build(EmptyMesh(), layers, model, Profile(supportMode: "tree"));

        Assert.NotEmpty(first.Paths);
        Assert.Contains("tree_support_provisional", first.Diagnostics);
        Assert.Equal(TreeSignature(first), TreeSignature(repeated));
        Assert.Contains(first.Paths, path => path.WidthMm > 2 * Profile(supportMode: "tree").Support.Tree.MinimumRadiusMm);
    }

    /// <summary>Leaves branch-angle-limited tips independent rather than forcing an invalid merge.</summary>
    [Fact]
    public void BuildTree_AngleLimit_IsReported()
    {
        LayerContours[] layers = Enumerable.Range(0, 6).Select(index => Layer([], index)).ToArray();
        var model = new ModelPathPlan(
            [],
            [],
            [new LayerMask(layers[^1].ZMm, [Rectangle(0, 0, 1, 1), Rectangle(8, 0, 9, 1)])],
            []);

        SupportPlan result = TreeSupportPlanner.Build(EmptyMesh(), layers, model, Profile(supportMode: "tree", maximumTreeAngle: 5));

        Assert.Contains("tree_branch_angle_limited", result.Diagnostics);
    }

    /// <summary>Reports tips whose branches collide before reaching an allowed foundation.</summary>
    [Fact]
    public void BuildTree_ModelCollision_ReportsUnreachableTip()
    {
        LayerContours[] layers =
        [
            Layer([Rectangle(0, 0, 20, 20)], 0),
            Layer([], 1),
            Layer([], 2),
            Layer([], 3),
        ];
        var model = new ModelPathPlan([], [], [new LayerMask(layers[^1].ZMm, [Rectangle(5, 5, 6, 6)])], []);

        SupportPlan result = TreeSupportPlanner.Build(EmptyMesh(), layers, model, Profile(supportMode: "tree"));

        Assert.NotEmpty(result.UnsatisfiedDemand);
        Assert.Contains("tree_tip_unreachable", result.Diagnostics);
    }

    /// <summary>Uses a hollow sheath policy rather than pricing every tree trunk as a solid cylinder.</summary>
    [Fact]
    public void BuildTree_HollowTrunk_DepositsLessThanSolidTrunk()
    {
        LayerContours[] layers = Enumerable.Range(0, 10).Select(index => Layer([], index)).ToArray();
        var model = new ModelPathPlan([], [], [new LayerMask(layers[^1].ZMm, [Rectangle(0, 0, 1, 1)])], []);

        SupportPlan hollow = TreeSupportPlanner.Build(EmptyMesh(), layers, model, Profile(supportMode: "tree", treeInteriorDensity: 0));
        SupportPlan solid = TreeSupportPlanner.Build(EmptyMesh(), layers, model, Profile(supportMode: "tree", treeInteriorDensity: 1));

        Assert.True(DepositedVolume(hollow.Paths) < DepositedVolume(solid.Paths));
    }

    private static string TreeSignature(SupportPlan plan) => string.Join(
        '|',
        plan.Paths.Select(path => $"{path.Role}:{path.WidthMm:F4}:{string.Join(';', path.Points.Select(point => $"{point.X:F3},{point.Y:F3},{point.Z:F3}"))}"));

    private static double DepositedVolume(IEnumerable<ExtrusionPath> paths) => paths.Sum(path =>
        Enumerable.Range(1, path.Points.Count - 1).Sum(index => Vector3.Distance(path.Points[index - 1], path.Points[index]))
        * path.WidthMm
        * path.HeightMm);

    private static NormalizedMesh EmptyMesh() => new([], new string('0', 64), []);

    private static LayerContours Layer(IReadOnlyList<PolygonRing> rings, int index) => new(
        0.1 + (index * 0.2),
        0.2,
        rings);

    private static PolygonRing Rectangle(float x1, float y1, float x2, float y2) => new(
        [new Vector2(x1, y1), new Vector2(x2, y1), new Vector2(x2, y2), new Vector2(x1, y2)],
        false);

    private static ResolvedSimulationProfile Profile(
        bool supportEnabled = true,
        string supportMode = "normal",
        double treeInteriorDensity = 0,
        double maximumTreeAngle = 35) => ResolvedSimulationProfile.Resolve(
        BuildPreference.Standard,
        new Dictionary<string, string>
        {
            ["machine_id"] = "TEST-MACHINE",
            ["supported_material_ids"] = "MODEL",
            ["excluded_zones"] = "none",
            ["nozzle_diameter"] = "0.4",
            ["printable_width"] = "256",
            ["printable_depth"] = "256",
            ["printable_height"] = "256",
            ["maximum_xy_speed"] = "500",
            ["maximum_z_speed"] = "20",
            ["printing_acceleration"] = "10000",
            ["travel_acceleration"] = "12000",
        },
        Process(supportEnabled, supportMode, treeInteriorDensity, maximumTreeAngle),
        new Dictionary<string, string>
        {
            ["material_id"] = "MODEL",
            ["filament_density"] = "1.0",
            ["filament_max_volumetric_speed"] = "12",
            ["support_material_id"] = "SUPPORT",
            ["support_material_density"] = "1.2",
            ["support_interface_material_id"] = "INTERFACE",
            ["support_interface_material_density"] = "1.0",
            ["support_max_volumetric_speed"] = "12",
            ["support_interface_max_volumetric_speed"] = "8",
        },
        new string('A', 64));

    private static IReadOnlyDictionary<string, string> Process(
        bool supportEnabled,
        string supportMode,
        double treeInteriorDensity,
        double maximumTreeAngle) => new Dictionary<string, string>
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
            ["bridge_max_span"] = "8",
            ["bridge_anchor_length"] = "1.2",
            ["overhang_speed_multiplier"] = "50%",
            ["support_enabled"] = supportEnabled.ToString(),
            ["support_mode"] = supportMode,
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
            ["tree_max_branch_angle"] = maximumTreeAngle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["tree_min_radius"] = "2",
            ["tree_max_radius"] = "4",
            ["tree_radius_per_tip"] = "0.2",
            ["tree_wall_count"] = "2",
            ["tree_hollow_density"] = treeInteriorDensity.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            ["brim_width"] = "0",
        };
}
