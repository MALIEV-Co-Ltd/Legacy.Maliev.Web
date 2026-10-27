// <copyright file="FdmSimulationModelPathsTests.cs" company="Maliev Company Limited">
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

/// <summary>Freezes disjoint wall, skin, infill, gap, and bridge path ownership.</summary>
public sealed class FdmSimulationModelPathsTests
{
    /// <summary>Stops perimeter offsets when a thin feature cannot contain requested loops.</summary>
    [Fact]
    public void Build_ThinWall_DoesNotInventSixFullLoops()
    {
        ModelPathPlan plan = ModelPathPlanner.Build([Layer(Rectangle(0, 0, 1, 20), 0.1)], Profile(walls: 6));

        int wallPaths = plan.Paths.Count(path => path.Role is ExtrusionRole.OuterWall or ExtrusionRole.InnerWall);
        Assert.InRange(wallPaths, 1, 2);
        Assert.Contains(plan.Paths, path => path.Role == ExtrusionRole.GapFill);
    }

    /// <summary>Keeps visible and internal perimeters in separate ledger roles.</summary>
    [Fact]
    public void Build_Walls_KeepOuterAndInnerRolesDistinct()
    {
        ModelPathPlan plan = ModelPathPlanner.Build([Layer(Rectangle(0, 0, 20, 20), 0.1)], Profile(walls: 3));

        Assert.Contains(plan.Paths, path => path.Role == ExtrusionRole.OuterWall);
        Assert.Contains(plan.Paths, path => path.Role == ExtrusionRole.InnerWall);
    }

    /// <summary>Unions overlapping top and bottom skins instead of depositing the slab twice.</summary>
    [Fact]
    public void Build_ThinSlab_DoesNotDoubleCountTopAndBottomSkin()
    {
        ModelPathPlan plan = ModelPathPlanner.Build([Layer(Rectangle(0, 0, 20, 20), 0.1)], Profile(topLayers: 4, bottomLayers: 4));

        Assert.DoesNotContain(plan.Paths, path => path.Role == ExtrusionRole.TopSkin);
        Assert.Contains(plan.Paths, path => path.Role == ExtrusionRole.BottomSkin);
        Assert.True(DepositedVolume(plan.Paths) <= 400 * 0.2 * 1.05);
    }

    /// <summary>Clips sparse infill around hollow cavities.</summary>
    [Fact]
    public void Build_HollowPart_NeverDrawsSparseInfillThroughHole()
    {
        PolygonRing[] rings = [Rectangle(0, 0, 40, 40), Rectangle(10, 10, 30, 30, isHole: true)];
        LayerContours[] layers = Enumerable.Range(0, 10).Select(index => Layer(rings, 0.1 + (index * 0.2))).ToArray();

        ModelPathPlan plan = ModelPathPlanner.Build(layers, Profile(topLayers: 1, bottomLayers: 1));

        Assert.Contains(plan.Paths, path => path.Role == ExtrusionRole.SparseInfill);
        Assert.All(
            plan.Paths.Where(path => path.Role == ExtrusionRole.SparseInfill).SelectMany(SegmentMidpoints),
            point => Assert.False(point.X > 10 && point.X < 30 && point.Y > 10 && point.Y < 30));
    }

    /// <summary>Propagates an intermediate exposed ledge through physical top-skin depth.</summary>
    [Fact]
    public void Build_SteppedPart_PropagatesTopSkinBelowLedge()
    {
        LayerContours[] layers =
        [
            Layer(Rectangle(0, 0, 20, 20), 0.1),
            Layer(Rectangle(0, 0, 20, 20), 0.3),
            Layer(Rectangle(0, 0, 10, 20), 0.5),
            Layer(Rectangle(0, 0, 10, 20), 0.7),
            Layer(Rectangle(0, 0, 10, 20), 0.9),
        ];

        ModelPathPlan plan = ModelPathPlanner.Build(layers, Profile(topLayers: 2, bottomLayers: 1));

        Assert.Contains(
            plan.Paths.Where(path => path.Role == ExtrusionRole.TopSkin && Math.Abs(path.Points[0].Z - 0.3) < 0.001),
            path => SegmentMidpoints(path).Any(point => point.X > 10));
    }

    /// <summary>Splits unsupported wall spans into slowed segments without a second volume role.</summary>
    [Fact]
    public void Build_OverhangWall_SplitsSpeedWithoutDuplicatingRole()
    {
        LayerContours lower = Layer(Rectangle(0, 0, 10, 10), 0.1);
        LayerContours upper = Layer(Rectangle(0, 0, 20, 10), 0.3);

        ModelPathPlan plan = ModelPathPlanner.Build([lower, upper], Profile());
        ExtrusionPath[] upperWalls = plan.Paths
            .Where(path => path.Role == ExtrusionRole.OuterWall && Math.Abs(path.Points[0].Z - 0.3) < 0.001)
            .ToArray();

        Assert.Contains(upperWalls, path => path.SpeedMultiplier == 1);
        Assert.Contains(upperWalls, path => path.SpeedMultiplier == 0.5);
        Assert.DoesNotContain(upperWalls, path => path.Role != ExtrusionRole.OuterWall);
    }

    /// <summary>Matches requested rectilinear deposited fraction on a unit-cell-compatible region.</summary>
    [Theory]
    [InlineData("rectilinear")]
    [InlineData("grid")]
    public void Build_LinearInfill_MatchesRequestedFraction(string pattern)
    {
        IReadOnlyList<ExtrusionPath> paths = InfillPathPlanner.Build(
            [Rectangle(0, 0, 100, 100)], 0.1, 0.2, 0.5, 0.20, pattern, 0, "TEST", ExtrusionRole.SparseInfill);

        double fraction = DepositedVolume(paths) / (100 * 100 * 0.2);
        Assert.InRange(fraction, 0.19, 0.21);
    }

    /// <summary>Calibrates implicit gyroid curves to density and changes phase deterministically.</summary>
    [Fact]
    public void Build_Gyroid_MatchesDensityAndIsRepeatableAcrossPhase()
    {
        IReadOnlyList<ExtrusionPath> first = InfillPathPlanner.Build(
            [Rectangle(0, 0, 40, 40)], 0.1, 0.2, 0.45, 0.15, "gyroid", 3, "TEST", ExtrusionRole.SparseInfill);
        IReadOnlyList<ExtrusionPath> repeated = InfillPathPlanner.Build(
            [Rectangle(0, 0, 40, 40)], 0.1, 0.2, 0.45, 0.15, "gyroid", 3, "TEST", ExtrusionRole.SparseInfill);
        IReadOnlyList<ExtrusionPath> nextPhase = InfillPathPlanner.Build(
            [Rectangle(0, 0, 40, 40)], 0.3, 0.2, 0.45, 0.15, "gyroid", 4, "TEST", ExtrusionRole.SparseInfill);

        double fraction = DepositedVolume(first) / (40 * 40 * 0.2);
        Assert.InRange(fraction, 0.14, 0.16);
        Assert.Equal(PathSignature(first), PathSignature(repeated));
        Assert.NotEqual(PathSignature(first), PathSignature(nextPhase));
    }

    /// <summary>Accepts a bounded two-anchor bridge while rejecting a one-ended cantilever.</summary>
    [Fact]
    public void Build_Bridges_RequireTwoSupportedAnchors()
    {
        LayerContours supports = Layer([Rectangle(0, 0, 5, 10), Rectangle(15, 0, 20, 10)], 0.1);
        LayerContours bridge = Layer(Rectangle(0, 0, 20, 10), 0.3);
        ModelPathPlan twoAnchors = ModelPathPlanner.Build([supports, bridge], Profile());

        LayerContours oneSupport = Layer(Rectangle(0, 0, 5, 10), 0.1);
        ModelPathPlan cantilever = ModelPathPlanner.Build([oneSupport, bridge], Profile());

        Assert.NotEmpty(twoAnchors.BridgeMasks);
        Assert.Empty(twoAnchors.SupportDemandMasks);
        Assert.Empty(cantilever.BridgeMasks);
        Assert.NotEmpty(cantilever.SupportDemandMasks);
    }

    private static string PathSignature(IReadOnlyList<ExtrusionPath> paths) => string.Join(
        '|',
        paths.Select(path => string.Join(';', path.Points.Select(point => $"{point.X:F3},{point.Y:F3},{point.Z:F3}"))));

    private static IEnumerable<Vector3> SegmentMidpoints(ExtrusionPath path)
    {
        for (int index = 1; index < path.Points.Count; index++)
        {
            yield return (path.Points[index - 1] + path.Points[index]) / 2;
        }
    }

    private static double DepositedVolume(IEnumerable<ExtrusionPath> paths) => paths.Sum(path =>
        Enumerable.Range(1, path.Points.Count - 1).Sum(index => Vector3.Distance(path.Points[index - 1], path.Points[index]))
        * path.WidthMm
        * path.HeightMm);

    private static LayerContours Layer(PolygonRing ring, double z) => Layer([ring], z);

    private static LayerContours Layer(IReadOnlyList<PolygonRing> rings, double z) => new(z, 0.2, rings);

    private static PolygonRing Rectangle(float x1, float y1, float x2, float y2, bool isHole = false) => new(
        [new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)],
        isHole);

    private static ResolvedSimulationProfile Profile(
        int walls = 2,
        int topLayers = 1,
        int bottomLayers = 1) => ResolvedSimulationProfile.Resolve(
        BuildPreference.Standard,
        new Dictionary<string, string>
        {
            ["machine_id"] = "TEST-MACHINE",
            ["supported_material_ids"] = "TEST",
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
        new Dictionary<string, string>
        {
            ["nozzle_diameter"] = "0.4",
            ["layer_height"] = "0.2",
            ["wall_loops"] = walls.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            ["top_shell_layers"] = topLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bottom_shell_layers"] = bottomLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            ["brim_width"] = "0",
        },
        new Dictionary<string, string>
        {
            ["material_id"] = "TEST",
            ["filament_density"] = "1.0",
            ["filament_max_volumetric_speed"] = "12",
            ["support_material_id"] = "model",
            ["support_material_density"] = "1.0",
            ["support_interface_material_id"] = "model",
            ["support_interface_material_density"] = "1.0",
            ["support_max_volumetric_speed"] = "12",
            ["support_interface_max_volumetric_speed"] = "8",
        },
        new string('A', 64));
}
