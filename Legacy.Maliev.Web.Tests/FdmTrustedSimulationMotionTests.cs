// <copyright file="FdmSimulationMotionTests.cs" company="Maliev Company Limited">
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

/// <summary>Freezes acceleration, flow, travel, cooling, and setup-time accounting.</summary>
public sealed class FdmSimulationMotionTests
{
    /// <summary>Pins triangular and trapezoidal zero-to-zero motion fixtures.</summary>
    [Theory]
    [InlineData(100, 2.5)]
    [InlineData(1, 0.2)]
    public void CalculateMoveSeconds_MatchesAnalyticFixture(double length, double expected)
    {
        Assert.Equal(expected, FeatureMotionEstimator.CalculateMoveSeconds(length, 0, 0, 100, 50), 6);
    }

    /// <summary>Does not stop at tessellation vertices along one straight extrusion run.</summary>
    [Fact]
    public void Estimate_StraightJoinedSegments_UseOneMotionRun()
    {
        ExtrusionPath path = Path(
            ExtrusionRole.OuterWall,
            "MODEL",
            [new(0, 0, 0.1f), new(50, 0, 0.1f), new(100, 0, 0.1f)]);

        MotionLedger result = FeatureMotionEstimator.Estimate([path], Profile(acceleration: 100, outerSpeed: 50));

        Assert.Equal(2.5, result.RoleSeconds[ExtrusionRole.OuterWall], 6);
    }

    /// <summary>Applies a lower interface-material flow cap only to eligible support paths.</summary>
    [Fact]
    public void Estimate_InterfaceFlowLimit_DoesNotThrottleModelWall()
    {
        ExtrusionPath wall = Path(ExtrusionRole.OuterWall, "MODEL", [new(0, 0, 0.1f), new(100, 0, 0.1f)]);
        ExtrusionPath support = Path(ExtrusionRole.SupportInterface, "INTERFACE", [new(0, 2, 0.1f), new(100, 2, 0.1f)]);

        MotionLedger result = FeatureMotionEstimator.Estimate([wall, support], Profile(interfaceFlow: 0.1));

        Assert.True(result.RoleSeconds[ExtrusionRole.SupportInterface] > result.RoleSeconds[ExtrusionRole.OuterWall] * 10);
    }

    /// <summary>Charges parallel heating by critical path and plate setup exactly once.</summary>
    [Fact]
    public void Estimate_ParallelHeatingAndSetup_AreNotDoubleCounted()
    {
        MotionLedger result = FeatureMotionEstimator.Estimate(
            [Path(ExtrusionRole.OuterWall, "MODEL", [new(0, 0, 0.1f), new(10, 0, 0.1f)])],
            Profile(setup: 30, nozzleHeat: 60, bedHeat: 90));

        Assert.Equal(120, result.PreparationSeconds, 6);
    }

    /// <summary>Accounts for travel, retract recovery, Z movement, and minimum-layer time.</summary>
    [Fact]
    public void Estimate_DisconnectedPaths_AccountForTravelRetractAndCooling()
    {
        ExtrusionPath first = Path(ExtrusionRole.OuterWall, "MODEL", [new(0, 0, 0.1f), new(10, 0, 0.1f)]);
        ExtrusionPath second = Path(ExtrusionRole.OuterWall, "MODEL", [new(20, 0, 0.3f), new(30, 0, 0.3f)]);

        MotionLedger result = FeatureMotionEstimator.Estimate([first, second], Profile(minimumLayerTime: 5));

        Assert.True(result.TravelSeconds > 0);
        Assert.True(result.RoleSeconds[ExtrusionRole.OuterWall] > 0);
        Assert.True(result.TotalSeconds - result.PreparationSeconds >= 10);
    }

    /// <summary>Slows extrusion on a short layer before assigning any unavoidable cooling dwell.</summary>
    [Fact]
    public void Estimate_CoolingLimitedLayer_SlowsExtrusionBeforeDwell()
    {
        ExtrusionPath wall = Path(
            ExtrusionRole.OuterWall,
            "MODEL",
            [new(0, 0, 0.1f), new(100, 0, 0.1f)]);

        MotionLedger unrestricted = FeatureMotionEstimator.Estimate(
            [wall],
            Profile(outerSpeed: 50, minimumLayerTime: 0));
        MotionLedger coolingLimited = FeatureMotionEstimator.Estimate(
            [wall],
            Profile(outerSpeed: 50, minimumLayerTime: 5));

        Assert.True(
            coolingLimited.RoleSeconds[ExtrusionRole.OuterWall]
            > unrestricted.RoleSeconds[ExtrusionRole.OuterWall]);
        Assert.Equal(0, coolingLimited.CoolingSeconds, 3);
        Assert.Equal(5, coolingLimited.TotalSeconds - coolingLimited.PreparationSeconds, 3);
    }

    /// <summary>Applies resolved filament cooling limits to otherwise identical path motion.</summary>
    [Fact]
    public void Estimate_RuntimeFilamentProfiles_ProduceDifferentShortLayerTimes()
    {
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();
        Assert.True(catalog.TryResolveTrustedProfile("PLA", BuildPreference.Standard, out ResolvedSimulationProfile? pla));
        Assert.True(catalog.TryResolveTrustedProfile("PETG", BuildPreference.Standard, out ResolvedSimulationProfile? petg));
        ExtrusionPath wall = Path(
            ExtrusionRole.OuterWall,
            "MODEL",
            [new(0, 0, 0.1f), new(100, 0, 0.1f)]);

        MotionLedger plaMotion = FeatureMotionEstimator.Estimate([wall], pla!);
        MotionLedger petgMotion = FeatureMotionEstimator.Estimate([wall], petg!);

        Assert.Equal(4, plaMotion.TotalSeconds - plaMotion.PreparationSeconds, 3);
        Assert.Equal(12, petgMotion.TotalSeconds - petgMotion.PreparationSeconds, 3);
        Assert.True(petgMotion.RoleSeconds[ExtrusionRole.OuterWall]
            > plaMotion.RoleSeconds[ExtrusionRole.OuterWall]);
    }

    /// <summary>Changing outer-wall speed changes time without changing deposited volume.</summary>
    [Fact]
    public void Estimate_OuterWallSpeed_ChangesTimeNotMaterial()
    {
        ExtrusionPath wall = Path(ExtrusionRole.OuterWall, "MODEL", [new(0, 0, 0.1f), new(100, 0, 0.1f)]);

        MotionLedger slow = FeatureMotionEstimator.Estimate([wall], Profile(outerSpeed: 25));
        MotionLedger fast = FeatureMotionEstimator.Estimate([wall], Profile(outerSpeed: 100));

        Assert.True(slow.RoleSeconds[ExtrusionRole.OuterWall] > fast.RoleSeconds[ExtrusionRole.OuterWall]);
        Assert.Equal(100 * wall.WidthMm * wall.HeightMm, DepositedVolume(wall), 6);
    }

    /// <summary>Ignores zero-length moves and rejects nonfinite geometry instead of returning NaN.</summary>
    [Fact]
    public void Estimate_DegenerateAndNonfinitePaths_FailSafely()
    {
        ExtrusionPath zero = Path(ExtrusionRole.OuterWall, "MODEL", [new(0, 0, 0.1f), new(0, 0, 0.1f)]);
        MotionLedger result = FeatureMotionEstimator.Estimate([zero], Profile());
        Assert.Equal(0, result.RoleSeconds[ExtrusionRole.OuterWall]);

        ExtrusionPath invalid = Path(ExtrusionRole.OuterWall, "MODEL", [new(float.NaN, 0, 0.1f), new(1, 0, 0.1f)]);
        Assert.Throws<SimulationGeometryException>(() => FeatureMotionEstimator.Estimate([invalid], Profile()));
    }

    /// <summary>Reconciles the fixed-pose engine's role volumes and times exactly once.</summary>
    [Fact]
    public void Engine_Cube_ReconcilesPhysicalLedgers()
    {
        NormalizedMesh mesh = MeshNormalizer.Normalize(
            new MeshInput(Cube(4), 1, Matrix4x4.Identity),
            new GeometryTolerance(0.001, 0.02));

        SimulationResult result = FdmSimulationEngine.Estimate(new SimulationRequest(
            mesh,
            new Pose("fixed", Matrix4x4.Identity),
            Profile(),
            1,
            new AnalysisBudget(1000, 1000, 1_000_000, default)));

        Assert.True(result.TotalDepositedMm3 > 0);
        Assert.Equal(result.TotalDepositedMm3, result.Roles.Sum(role => role.DepositedMm3), 6);
        Assert.Equal(result.Roles.Sum(role => role.Seconds), result.Motion.RoleSeconds.Values.Sum(), 6);
        Assert.DoesNotContain("support_unsatisfied_demand", result.Diagnostics);
    }

    private static double DepositedVolume(ExtrusionPath path) => Vector3.Distance(path.Points[0], path.Points[1])
        * path.WidthMm
        * path.HeightMm;

    private static ExtrusionPath Path(ExtrusionRole role, string material, IReadOnlyList<Vector3> points) => new(
        points,
        1,
        0.2,
        role,
        material);

    private static IReadOnlyList<MeshTriangle> Cube(float size)
    {
        Vector3 p000 = new(0, 0, 0); Vector3 p100 = new(size, 0, 0);
        Vector3 p110 = new(size, size, 0); Vector3 p010 = new(0, size, 0);
        Vector3 p001 = new(0, 0, size); Vector3 p101 = new(size, 0, size);
        Vector3 p111 = new(size, size, size); Vector3 p011 = new(0, size, size);
        return
        [
            new(p000, p110, p100), new(p000, p010, p110), new(p001, p101, p111), new(p001, p111, p011),
            new(p000, p100, p101), new(p000, p101, p001), new(p010, p011, p111), new(p010, p111, p110),
            new(p000, p001, p011), new(p000, p011, p010), new(p100, p110, p111), new(p100, p111, p101),
        ];
    }

    private static ResolvedSimulationProfile Profile(
        double acceleration = 100,
        double outerSpeed = 50,
        double? interfaceFlow = 8,
        double minimumLayerTime = 0,
        double setup = 0,
        double nozzleHeat = 0,
        double bedHeat = 0) => ResolvedSimulationProfile.Resolve(
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
            ["printing_acceleration"] = acceleration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["travel_acceleration"] = "500",
        },
        Process(outerSpeed, minimumLayerTime, setup, nozzleHeat, bedHeat),
        new Dictionary<string, string>
        {
            ["material_id"] = "MODEL",
            ["filament_density"] = "1",
            ["filament_max_volumetric_speed"] = "0",
            ["minimum_layer_time"] = minimumLayerTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["minimum_cooling_speed"] = "1",
            ["support_material_id"] = "SUPPORT",
            ["support_material_density"] = "1",
            ["support_interface_material_id"] = "INTERFACE",
            ["support_interface_material_density"] = "1",
            ["support_max_volumetric_speed"] = "8",
            ["support_interface_max_volumetric_speed"] = (interfaceFlow ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
        new string('A', 64));

    private static IReadOnlyDictionary<string, string> Process(
        double outerSpeed,
        double minimumLayerTime,
        double setup,
        double nozzleHeat,
        double bedHeat) => new Dictionary<string, string>
        {
            ["nozzle_diameter"] = "0.4",
            ["layer_height"] = "0.2",
            ["wall_loops"] = "2",
            ["inner_wall_speed"] = "200",
            ["outer_wall_speed"] = outerSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            ["first_layer_speed_multiplier"] = "100%",
            ["minimum_layer_time"] = minimumLayerTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["retract_length"] = "0.8",
            ["retract_speed"] = "40",
            ["setup_time"] = setup.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["nozzle_heat_time"] = nozzleHeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bed_heat_time"] = bedHeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["tool_change_time"] = "12",
            ["brim_width"] = "0",
        };
}
