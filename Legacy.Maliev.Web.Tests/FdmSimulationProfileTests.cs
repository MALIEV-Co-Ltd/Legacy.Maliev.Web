// <copyright file="FdmSimulationProfileTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests;

using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

/// <summary>Freezes normalized manufacturing settings and physical-ledger ownership.</summary>
public sealed class FdmSimulationProfileTests
{
    /// <summary>Resolves the three owner-approved build choices from inherited values.</summary>
    [Theory]
    [InlineData(BuildPreference.Standard, 0.20, 2)]
    [InlineData(BuildPreference.Quality, 0.12, 3)]
    [InlineData(BuildPreference.Strength, 0.20, 6)]
    public void ResolveBuildProfile_UsesResolvedLayerHeightAndWalls(
        BuildPreference preference,
        double expectedLayerHeight,
        int expectedWalls)
    {
        ResolvedSimulationProfile result = CreateProfile(
            preference,
            new Dictionary<string, string>
            {
                ["layer_height"] = expectedLayerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["wall_loops"] = expectedWalls.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["inner_wall_speed"] = "200",
                ["outer_wall_speed"] = "50%",
                ["sparse_infill_density"] = "15%",
            });

        Assert.Equal(expectedLayerHeight, result.LayerHeightMm, 6);
        Assert.Equal(expectedWalls, result.WallCount);
        Assert.Equal(100, result.SpeedMmPerSecond[ExtrusionRole.OuterWall], 6);
        Assert.Equal(0.15, result.SparseInfillFraction, 6);
    }

    /// <summary>Treats zero flow as unlimited instead of a zero-speed print.</summary>
    [Fact]
    public void ResolveBuildProfile_ZeroVolumetricFlow_IsUnlimited()
    {
        ResolvedSimulationProfile result = CreateProfile(
            BuildPreference.Standard,
            new Dictionary<string, string>
            {
                ["layer_height"] = "0.2",
                ["wall_loops"] = "2",
                ["inner_wall_speed"] = "200",
                ["outer_wall_speed"] = "50%",
                ["sparse_infill_density"] = "15%",
                ["filament_max_volumetric_speed"] = "0",
            });

        Assert.Null(result.MaximumVolumetricFlowMm3PerSecond);
    }

    /// <summary>Fails closed when a process nozzle does not match its machine.</summary>
    [Fact]
    public void ResolveBuildProfile_IncompatibleNozzle_IsRejected()
    {
        SimulationProfileException exception = Assert.Throws<SimulationProfileException>(() =>
            CreateProfile(
                BuildPreference.Standard,
                new Dictionary<string, string>
                {
                    ["layer_height"] = "0.2",
                    ["wall_loops"] = "2",
                    ["nozzle_diameter"] = "0.6",
                    ["inner_wall_speed"] = "200",
                    ["outer_wall_speed"] = "50%",
                    ["sparse_infill_density"] = "15%",
                }));

        Assert.Equal("profile_nozzle_incompatible", exception.ReasonCode);
    }

    /// <summary>Changes the resolved identity when an inherited speed changes.</summary>
    [Fact]
    public void ResolveBuildProfile_ResolvedHashIncludesInheritedSpeed()
    {
        ResolvedSimulationProfile first = CreateProfile(BuildPreference.Standard, Settings("200"));
        ResolvedSimulationProfile second = CreateProfile(BuildPreference.Standard, Settings("180"));

        Assert.Equal(first.SourceArtifactSha256, second.SourceArtifactSha256);
        Assert.NotEqual(first.ResolvedProfileSha256, second.ResolvedProfileSha256);
    }

    /// <summary>Keeps support body/interface/sheath ownership separate from other deposition.</summary>
    [Fact]
    public void SimulationResult_SupportAndTotalVolumesReconcile()
    {
        SimulationResult result = SimulationResult.Create(
            "analysis-v1",
            "profile-hash",
            [
                new RoleDeposition(ExtrusionRole.OuterWall, "ASA", 1200, 12),
                new RoleDeposition(ExtrusionRole.SupportBody, "ASA", 1500, 15),
                new RoleDeposition(ExtrusionRole.SupportInterface, "ASA", 300, 4),
                new RoleDeposition(ExtrusionRole.SupportSheath, "ASA", 200, 2),
                new RoleDeposition(ExtrusionRole.Adhesion, "ASA", 100, 1),
                new RoleDeposition(ExtrusionRole.Purge, "ASA", 50, 0),
            ]);

        Assert.Equal(3350, result.TotalDepositedMm3, 6);
        Assert.Equal(2000, result.SupportDepositedMm3, 6);
        Assert.Equal(100, result.AdhesionDepositedMm3, 6);
        Assert.Equal(50, result.PurgeDepositedMm3, 6);
    }

    /// <summary>Freezes unit-bearing physical result fields without commercial money.</summary>
    [Fact]
    public void SimulationResultSchema_DeclaresDisjointPhysicalVolumes()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "AdditiveBenchmark",
            "simulation-result.v1.schema.json");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement properties = schema.RootElement.GetProperty("properties");

        Assert.True(properties.TryGetProperty("totalDepositedMm3", out _));
        Assert.True(properties.TryGetProperty("supportDepositedMm3", out _));
        Assert.True(properties.TryGetProperty("adhesionDepositedMm3", out _));
        Assert.True(properties.TryGetProperty("purgeDepositedMm3", out _));
        Assert.False(properties.TryGetProperty("price", out _));
    }

    private static Dictionary<string, string> Settings(string innerSpeed) => new()
    {
        ["layer_height"] = "0.2",
        ["wall_loops"] = "2",
        ["inner_wall_speed"] = innerSpeed,
        ["outer_wall_speed"] = "50%",
        ["sparse_infill_density"] = "15%",
    };

    private static ResolvedSimulationProfile CreateProfile(
        BuildPreference preference,
        IReadOnlyDictionary<string, string> process)
    {
        // Complete synthetic settings satisfy the current parser without consulting
        // or changing the trusted runtime catalog and its material eligibility.
        var processSettings = new Dictionary<string, string>(process, StringComparer.Ordinal);
        processSettings.TryAdd("nozzle_diameter", "0.4");
        processSettings.TryAdd("layer_height", "0.2");
        processSettings.TryAdd("wall_loops", "2");
        processSettings.TryAdd("inner_wall_speed", "200");
        processSettings.TryAdd("outer_wall_speed", "50%");
        processSettings.TryAdd("sparse_infill_density", "15%");
        processSettings.TryAdd("sparse_infill_pattern", "rectilinear");
        processSettings.TryAdd("outer_wall_line_width", "0.42");
        processSettings.TryAdd("inner_wall_line_width", "0.45");
        processSettings.TryAdd("sparse_infill_line_width", "0.45");
        processSettings.TryAdd("solid_infill_line_width", "0.42");
        processSettings.TryAdd("gap_fill_min_width", "0.2");
        processSettings.TryAdd("gap_fill_max_width", "0.8");
        processSettings.TryAdd("top_shell_layers", "4");
        processSettings.TryAdd("bottom_shell_layers", "4");
        processSettings.TryAdd("bridge_max_span", "25");
        processSettings.TryAdd("bridge_anchor_length", "1.2");
        processSettings.TryAdd("overhang_speed_multiplier", "50%");
        processSettings.TryAdd("support_enabled", "true");
        processSettings.TryAdd("support_mode", "normal");
        processSettings.TryAdd("support_build_plate_only", "true");
        processSettings.TryAdd("support_overhang_angle", "30");
        processSettings.TryAdd("support_top_z_gap", "0.2");
        processSettings.TryAdd("support_bottom_z_gap", "0.2");
        processSettings.TryAdd("support_xy_clearance", "0.35");
        processSettings.TryAdd("support_body_density", "15%");
        processSettings.TryAdd("support_interface_density", "80%");
        processSettings.TryAdd("support_interface_layers", "2");
        processSettings.TryAdd("support_body_line_width", "0.45");
        processSettings.TryAdd("support_interface_line_width", "0.42");
        processSettings.TryAdd("support_expansion", "0");
        processSettings.TryAdd("tree_tip_spacing", "3");
        processSettings.TryAdd("tree_max_branch_angle", "35");
        processSettings.TryAdd("tree_min_radius", "2");
        processSettings.TryAdd("tree_max_radius", "4");
        processSettings.TryAdd("tree_radius_per_tip", "0.2");
        processSettings.TryAdd("tree_wall_count", "2");
        processSettings.TryAdd("tree_hollow_density", "0%");
        processSettings.TryAdd("gap_fill_speed", "150");
        processSettings.TryAdd("top_surface_speed", "100");
        processSettings.TryAdd("bottom_surface_speed", "100");
        processSettings.TryAdd("internal_solid_infill_speed", "150");
        processSettings.TryAdd("sparse_infill_speed", "200");
        processSettings.TryAdd("bridge_speed", "50");
        processSettings.TryAdd("support_speed", "150");
        processSettings.TryAdd("support_interface_speed", "80");
        processSettings.TryAdd("first_layer_speed", "50");
        processSettings.TryAdd("travel_speed", "300");
        processSettings.TryAdd("first_layer_speed_multiplier", "50%");
        processSettings.TryAdd("minimum_layer_time", "5");
        processSettings.TryAdd("retract_length", "0.8");
        processSettings.TryAdd("retract_speed", "40");
        processSettings.TryAdd("setup_time", "30");
        processSettings.TryAdd("nozzle_heat_time", "60");
        processSettings.TryAdd("bed_heat_time", "90");
        processSettings.TryAdd("tool_change_time", "12");
        processSettings.TryAdd("brim_width", "0");

        return ResolvedSimulationProfile.Resolve(
            preference,
            new Dictionary<string, string>
            {
                ["machine_id"] = "SYNTHETIC-PROFILE-CONTRACT",
                ["supported_material_ids"] = "ASA",
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
            processSettings,
            new Dictionary<string, string>
            {
                ["material_id"] = "ASA",
                ["filament_density"] = "1.07",
                ["filament_max_volumetric_speed"] = processSettings.GetValueOrDefault("filament_max_volumetric_speed", "12"),
                ["support_material_id"] = "model",
                ["support_material_density"] = "1.07",
                ["support_interface_material_id"] = "model",
                ["support_interface_material_density"] = "1.07",
                ["support_max_volumetric_speed"] = "12",
                ["support_interface_max_volumetric_speed"] = "8",
            },
            sourceArtifactSha256: new string('A', 64));
    }
}
