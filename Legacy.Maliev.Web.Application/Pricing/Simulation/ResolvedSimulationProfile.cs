// <copyright file="ResolvedSimulationProfile.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>Immutable normalized machine, process, and filament settings.</summary>
public sealed class ResolvedSimulationProfile
{
    private ResolvedSimulationProfile(
        BuildPreference preference,
        double layerHeightMm,
        int wallCount,
        double sparseInfillFraction,
        string sparseInfillPattern,
        double outerWallLineWidthMm,
        double innerWallLineWidthMm,
        double sparseInfillLineWidthMm,
        double solidInfillLineWidthMm,
        double gapFillMinimumWidthMm,
        double gapFillMaximumWidthMm,
        int topShellLayers,
        int bottomShellLayers,
        double bridgeMaximumSpanMm,
        double bridgeAnchorLengthMm,
        double overhangSpeedMultiplier,
        ResolvedSupportProfile support,
        ResolvedMotionProfile motion,
        IReadOnlyDictionary<ExtrusionRole, double> speedMmPerSecond,
        double? maximumVolumetricFlowMm3PerSecond,
        double nozzleDiameterMm,
        double materialDensityGramsPerCm3,
        string materialId,
        string sourceArtifactSha256,
        string resolvedProfileSha256,
        double printableWidthMm,
        double printableDepthMm,
        double printableHeightMm)
    {
        this.Preference = preference;
        this.LayerHeightMm = layerHeightMm;
        this.WallCount = wallCount;
        this.SparseInfillFraction = sparseInfillFraction;
        this.SparseInfillPattern = sparseInfillPattern;
        this.OuterWallLineWidthMm = outerWallLineWidthMm;
        this.InnerWallLineWidthMm = innerWallLineWidthMm;
        this.SparseInfillLineWidthMm = sparseInfillLineWidthMm;
        this.SolidInfillLineWidthMm = solidInfillLineWidthMm;
        this.GapFillMinimumWidthMm = gapFillMinimumWidthMm;
        this.GapFillMaximumWidthMm = gapFillMaximumWidthMm;
        this.TopShellLayers = topShellLayers;
        this.BottomShellLayers = bottomShellLayers;
        this.BridgeMaximumSpanMm = bridgeMaximumSpanMm;
        this.BridgeAnchorLengthMm = bridgeAnchorLengthMm;
        this.OverhangSpeedMultiplier = overhangSpeedMultiplier;
        this.Support = support;
        this.Motion = motion;
        this.SpeedMmPerSecond = speedMmPerSecond;
        this.MaximumVolumetricFlowMm3PerSecond = maximumVolumetricFlowMm3PerSecond;
        this.NozzleDiameterMm = nozzleDiameterMm;
        this.MaterialDensityGramsPerCm3 = materialDensityGramsPerCm3;
        this.MaterialId = materialId;
        this.SourceArtifactSha256 = sourceArtifactSha256;
        this.ResolvedProfileSha256 = resolvedProfileSha256;
        this.PrintableWidthMm = printableWidthMm;
        this.PrintableDepthMm = printableDepthMm;
        this.PrintableHeightMm = printableHeightMm;
        this.MachineId = string.Empty;
        this.BrimWidthMm = 0;
        this.ExcludedZones = [];
    }

    /// <summary>Gets the customer build choice.</summary>
    public BuildPreference Preference { get; }

    /// <summary>Gets physical layer height.</summary>
    public double LayerHeightMm { get; }

    /// <summary>Gets requested perimeter count.</summary>
    public int WallCount { get; }

    /// <summary>Gets sparse infill as a fraction from zero to one.</summary>
    public double SparseInfillFraction { get; }

    /// <summary>Gets the normalized sparse infill pattern.</summary>
    public string SparseInfillPattern { get; }

    /// <summary>Gets visible perimeter extrusion width.</summary>
    public double OuterWallLineWidthMm { get; }

    /// <summary>Gets internal perimeter extrusion width.</summary>
    public double InnerWallLineWidthMm { get; }

    /// <summary>Gets sparse infill extrusion width.</summary>
    public double SparseInfillLineWidthMm { get; }

    /// <summary>Gets top and bottom solid extrusion width.</summary>
    public double SolidInfillLineWidthMm { get; }

    /// <summary>Gets the minimum printable variable-width gap lane.</summary>
    public double GapFillMinimumWidthMm { get; }

    /// <summary>Gets the maximum variable-width gap lane.</summary>
    public double GapFillMaximumWidthMm { get; }

    /// <summary>Gets requested top skin depth in physical layers.</summary>
    public int TopShellLayers { get; }

    /// <summary>Gets requested bottom skin depth in physical layers.</summary>
    public int BottomShellLayers { get; }

    /// <summary>Gets the maximum eligible two-anchor bridge span.</summary>
    public double BridgeMaximumSpanMm { get; }

    /// <summary>Gets the minimum supported anchor length at each bridge end.</summary>
    public double BridgeAnchorLengthMm { get; }

    /// <summary>Gets the local speed multiplier for unsupported wall segments.</summary>
    public double OverhangSpeedMultiplier { get; }

    /// <summary>Gets resolved support material and geometry settings.</summary>
    public ResolvedSupportProfile Support { get; }

    /// <summary>Gets resolved kinematic, travel, cooling, and setup settings.</summary>
    public ResolvedMotionProfile Motion { get; }

    /// <summary>Gets resolved role speeds.</summary>
    public IReadOnlyDictionary<ExtrusionRole, double> SpeedMmPerSecond { get; }

    /// <summary>Gets flow cap, or null when explicitly unlimited.</summary>
    public double? MaximumVolumetricFlowMm3PerSecond { get; }

    /// <summary>Gets nozzle diameter.</summary>
    public double NozzleDiameterMm { get; }

    /// <summary>Gets filament density.</summary>
    public double MaterialDensityGramsPerCm3 { get; }

    /// <summary>Gets exact or explicitly generic material profile ID.</summary>
    public string MaterialId { get; }

    /// <summary>Gets hash of the source artifact before inheritance resolution.</summary>
    public string SourceArtifactSha256 { get; }

    /// <summary>Gets hash of normalized effective settings.</summary>
    public string ResolvedProfileSha256 { get; }

    /// <summary>Gets usable machine width.</summary>
    public double PrintableWidthMm { get; }

    /// <summary>Gets usable machine depth.</summary>
    public double PrintableDepthMm { get; }

    /// <summary>Gets usable machine height.</summary>
    public double PrintableHeightMm { get; }

    /// <summary>Gets the resolved machine identity.</summary>
    public string MachineId { get; private init; }

    /// <summary>Gets model-side brim expansion around each packed footprint.</summary>
    public double BrimWidthMm { get; private init; }

    /// <summary>Gets unusable rectangular bed zones.</summary>
    public IReadOnlyList<MachineExclusionZone> ExcludedZones { get; private init; }

    /// <summary>Resolves effective machine/process/filament settings into physical units.</summary>
    public static ResolvedSimulationProfile Resolve(
        BuildPreference preference,
        IReadOnlyDictionary<string, string> machine,
        IReadOnlyDictionary<string, string> process,
        IReadOnlyDictionary<string, string> filament,
        string sourceArtifactSha256)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(filament);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArtifactSha256);

        double machineNozzle = Positive(machine, "nozzle_diameter");
        double processNozzle = Positive(process, "nozzle_diameter");
        if (Math.Abs(machineNozzle - processNozzle) > 0.0001)
        {
            throw new SimulationProfileException(
                "profile_nozzle_incompatible",
                "Process nozzle diameter does not match the machine profile.");
        }

        double innerWall = Positive(process, "inner_wall_speed");
        double outerWall = ResolveSpeed(process, "outer_wall_speed", innerWall);
        double flow = NonNegative(filament, "filament_max_volumetric_speed");
        double density = Positive(filament, "filament_density");
        string material = Required(filament, "material_id");
        var speeds = new Dictionary<ExtrusionRole, double>
        {
            [ExtrusionRole.InnerWall] = innerWall,
            [ExtrusionRole.OuterWall] = outerWall,
            [ExtrusionRole.GapFill] = ResolveSpeed(process, "gap_fill_speed", innerWall),
            [ExtrusionRole.TopSkin] = ResolveSpeed(process, "top_surface_speed", innerWall),
            [ExtrusionRole.BottomSkin] = ResolveSpeed(process, "bottom_surface_speed", innerWall),
            [ExtrusionRole.InternalSolid] = ResolveSpeed(process, "internal_solid_infill_speed", innerWall),
            [ExtrusionRole.SparseInfill] = ResolveSpeed(process, "sparse_infill_speed", innerWall),
            [ExtrusionRole.Bridge] = ResolveSpeed(process, "bridge_speed", innerWall),
            [ExtrusionRole.SupportBody] = ResolveSpeed(process, "support_speed", innerWall),
            [ExtrusionRole.SupportInterface] = ResolveSpeed(process, "support_interface_speed", innerWall),
            [ExtrusionRole.SupportSheath] = ResolveSpeed(process, "support_speed", innerWall),
            [ExtrusionRole.Adhesion] = ResolveSpeed(process, "first_layer_speed", innerWall),
            [ExtrusionRole.Purge] = ResolveSpeed(process, "first_layer_speed", innerWall),
        };

        string canonical = Canonicalize(machine, process, filament);
        string resolvedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        string machineId = Required(machine, "machine_id");
        string[] supportedMaterials = Required(machine, "supported_material_ids")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!supportedMaterials.Contains(material, StringComparer.OrdinalIgnoreCase))
        {
            throw new SimulationProfileException("profile_material_machine_incompatible", "Material is not approved for the selected machine profile.");
        }

        return new ResolvedSimulationProfile(
            preference,
            Positive(process, "layer_height"),
            PositiveInteger(process, "wall_loops"),
            Fraction(process, "sparse_infill_density"),
            Pattern(process, "sparse_infill_pattern"),
            Positive(process, "outer_wall_line_width"),
            Positive(process, "inner_wall_line_width"),
            Positive(process, "sparse_infill_line_width"),
            Positive(process, "solid_infill_line_width"),
            Positive(process, "gap_fill_min_width"),
            Positive(process, "gap_fill_max_width"),
            NonNegativeInteger(process, "top_shell_layers"),
            NonNegativeInteger(process, "bottom_shell_layers"),
            Positive(process, "bridge_max_span"),
            Positive(process, "bridge_anchor_length"),
            PositiveFraction(process, "overhang_speed_multiplier"),
            ResolveSupport(process, filament, material, density),
            ResolveMotion(machine, process, filament),
            speeds,
            flow == 0 ? null : flow,
            machineNozzle,
            density,
            material,
            sourceArtifactSha256,
            resolvedSha,
            Positive(machine, "printable_width"),
            Positive(machine, "printable_depth"),
            Positive(machine, "printable_height"))
        {
            MachineId = machineId,
            BrimWidthMm = NonNegative(process, "brim_width"),
            ExcludedZones = ParseExcludedZones(Required(machine, "excluded_zones")),
        };
    }

    private static double ResolveSpeed(IReadOnlyDictionary<string, string> values, string key, double parent)
    {
        string value = Required(values, key);
        if (value.EndsWith('%'))
        {
            double percent = Parse(value[..^1], key);
            if (percent <= 0)
            {
                throw Invalid(key, "must be positive");
            }

            return parent * percent / 100d;
        }

        double speed = Parse(value, key);
        if (speed <= 0)
        {
            throw Invalid(key, "must be positive");
        }

        return speed;
    }

    private static double Fraction(IReadOnlyDictionary<string, string> values, string key)
    {
        string value = Required(values, key);
        double fraction = value.EndsWith('%') ? Parse(value[..^1], key) / 100d : Parse(value, key);
        if (fraction < 0 || fraction > 1)
        {
            throw Invalid(key, "must be between zero and one");
        }

        return fraction;
    }

    private static int PositiveInteger(IReadOnlyDictionary<string, string> values, string key)
    {
        string value = Required(values, key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) || result <= 0)
        {
            throw Invalid(key, "must be a positive integer");
        }

        return result;
    }

    private static double PositiveFraction(IReadOnlyDictionary<string, string> values, string key)
    {
        double result = Fraction(values, key);
        if (result <= 0)
        {
            throw Invalid(key, "must be greater than zero");
        }

        return result;
    }

    private static int NonNegativeInteger(IReadOnlyDictionary<string, string> values, string key)
    {
        string value = Required(values, key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) || result < 0)
        {
            throw Invalid(key, "must be a non-negative integer");
        }

        return result;
    }

    private static string Pattern(IReadOnlyDictionary<string, string> values, string key)
    {
        string pattern = Required(values, key).ToLowerInvariant();
        if (pattern is not ("rectilinear" or "grid" or "gyroid"))
        {
            throw Invalid(key, "must be rectilinear, grid, or gyroid");
        }

        return pattern;
    }

    private static ResolvedSupportProfile ResolveSupport(
        IReadOnlyDictionary<string, string> process,
        IReadOnlyDictionary<string, string> filament,
        string modelMaterialId,
        double modelDensity)
    {
        string mode = Required(process, "support_mode").ToLowerInvariant();
        if (mode is not ("normal" or "tree"))
        {
            throw Invalid("support_mode", "must be normal or tree");
        }

        string bodyMaterial = Required(filament, "support_material_id");
        string interfaceMaterial = Required(filament, "support_interface_material_id");
        double overhangAngle = Positive(process, "support_overhang_angle");
        if (overhangAngle >= 90)
        {
            throw Invalid("support_overhang_angle", "must be below 90 degrees from horizontal");
        }

        double treeAngle = Positive(process, "tree_max_branch_angle");
        if (treeAngle >= 90)
        {
            throw Invalid("tree_max_branch_angle", "must be below 90 degrees from vertical");
        }

        double treeMinimumRadius = Positive(process, "tree_min_radius");
        double treeMaximumRadius = Positive(process, "tree_max_radius");
        if (treeMinimumRadius > treeMaximumRadius)
        {
            throw Invalid("tree_min_radius", "must not exceed tree_max_radius");
        }

        return new ResolvedSupportProfile(
            Boolean(process, "support_enabled"),
            mode,
            Boolean(process, "support_build_plate_only"),
            overhangAngle,
            NonNegative(process, "support_top_z_gap"),
            NonNegative(process, "support_bottom_z_gap"),
            NonNegative(process, "support_xy_clearance"),
            PositiveFraction(process, "support_body_density"),
            PositiveFraction(process, "support_interface_density"),
            PositiveInteger(process, "support_interface_layers"),
            Positive(process, "support_body_line_width"),
            Positive(process, "support_interface_line_width"),
            bodyMaterial == "model" ? modelMaterialId : bodyMaterial,
            bodyMaterial == "model" ? modelDensity : Positive(filament, "support_material_density"),
            interfaceMaterial == "model" ? modelMaterialId : interfaceMaterial,
            interfaceMaterial == "model" ? modelDensity : Positive(filament, "support_interface_material_density"),
            NonNegative(process, "support_expansion"),
            OptionalFlow(filament, "support_max_volumetric_speed"),
            OptionalFlow(filament, "support_interface_max_volumetric_speed"),
            new ResolvedTreeSupportProfile(
                Positive(process, "tree_tip_spacing"),
                treeAngle,
                treeMinimumRadius,
                treeMaximumRadius,
                Positive(process, "tree_radius_per_tip"),
                PositiveInteger(process, "tree_wall_count"),
                Fraction(process, "tree_hollow_density")));
    }

    private static ResolvedMotionProfile ResolveMotion(
        IReadOnlyDictionary<string, string> machine,
        IReadOnlyDictionary<string, string> process,
        IReadOnlyDictionary<string, string> filament) => new(
            Positive(machine, "maximum_xy_speed"),
            Positive(machine, "maximum_z_speed"),
            Positive(machine, "printing_acceleration"),
            Positive(machine, "travel_acceleration"),
            Positive(process, "travel_speed"),
            PositiveFraction(process, "first_layer_speed_multiplier"),
            CoolingValue(filament, process, "minimum_layer_time"),
            CoolingValue(filament, process, "minimum_cooling_speed"),
            NonNegative(process, "retract_length"),
            Positive(process, "retract_speed"),
            NonNegative(process, "setup_time"),
            NonNegative(process, "nozzle_heat_time"),
            NonNegative(process, "bed_heat_time"),
            NonNegative(process, "tool_change_time"),
            OptionalNonNegative(machine, "machine_prepare_compensation_time"),
            OptionalNonNegative(machine, "machine_load_filament_time"));

    private static double OptionalNonNegative(IReadOnlyDictionary<string, string> values, string key) =>
        values.ContainsKey(key) ? NonNegative(values, key) : 0;

    private static double CoolingValue(
        IReadOnlyDictionary<string, string> filament,
        IReadOnlyDictionary<string, string> process,
        string key)
    {
        if (filament.ContainsKey(key))
        {
            return NonNegative(filament, key);
        }

        return process.ContainsKey(key) ? NonNegative(process, key) : 0;
    }

    private static double? OptionalFlow(IReadOnlyDictionary<string, string> values, string key)
    {
        double value = NonNegative(values, key);
        return value == 0 ? null : value;
    }

    private static bool Boolean(IReadOnlyDictionary<string, string> values, string key)
    {
        string value = Required(values, key);
        if (!bool.TryParse(value, out bool result))
        {
            throw Invalid(key, "must be true or false");
        }

        return result;
    }

    private static IReadOnlyList<MachineExclusionZone> ParseExcludedZones(string value)
    {
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var zones = new List<MachineExclusionZone>();
        foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = item.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length != 4)
            {
                throw Invalid("excluded_zones", "must contain x1,y1,x2,y2 rectangles or 'none'");
            }

            double x1 = Parse(fields[0], "excluded_zones");
            double y1 = Parse(fields[1], "excluded_zones");
            double x2 = Parse(fields[2], "excluded_zones");
            double y2 = Parse(fields[3], "excluded_zones");
            if (x2 <= x1 || y2 <= y1)
            {
                throw Invalid("excluded_zones", "rectangles must have positive area");
            }

            zones.Add(new MachineExclusionZone(x1, y1, x2, y2));
        }

        return zones;
    }

    private static double Positive(IReadOnlyDictionary<string, string> values, string key)
    {
        double result = Parse(Required(values, key), key);
        if (result <= 0)
        {
            throw Invalid(key, "must be positive");
        }

        return result;
    }

    private static double NonNegative(IReadOnlyDictionary<string, string> values, string key)
    {
        double result = Parse(Required(values, key), key);
        if (result < 0)
        {
            throw Invalid(key, "must be non-negative");
        }

        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out string? result) || string.IsNullOrWhiteSpace(result))
        {
            throw new SimulationProfileException("profile_setting_missing", $"Required profile setting '{key}' is missing.");
        }

        return result.Trim();
    }

    private static double Parse(string value, string key)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            || !double.IsFinite(result))
        {
            throw Invalid(key, "must be finite numeric text");
        }

        return result;
    }

    private static SimulationProfileException Invalid(string key, string requirement) =>
        new("profile_setting_invalid", $"Profile setting '{key}' {requirement}.");

    private static string Canonicalize(params IReadOnlyDictionary<string, string>[] groups)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < groups.Length; index++)
        {
            foreach ((string key, string value) in groups[index].OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.Append(index).Append(':').Append(key).Append('=').Append(value.Trim()).Append('\n');
            }
        }

        return builder.ToString();
    }
}

/// <summary>Stable fail-closed profile validation exception.</summary>
public sealed class SimulationProfileException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public SimulationProfileException(string reasonCode, string message)
        : base(message)
    {
        this.ReasonCode = reasonCode;
    }

    /// <summary>Gets the stable reason code.</summary>
    public string ReasonCode { get; }
}

/// <summary>Resolved normal/tree support settings and material provenance.</summary>
public sealed record ResolvedSupportProfile(
    bool Enabled,
    string Mode,
    bool BuildPlateOnly,
    double OverhangAngleFromHorizontalDegrees,
    double TopZGapMm,
    double BottomZGapMm,
    double XYClearanceMm,
    double BodyDensity,
    double InterfaceDensity,
    int InterfaceLayers,
    double BodyLineWidthMm,
    double InterfaceLineWidthMm,
    string BodyMaterialId,
    double BodyMaterialDensityGramsPerCm3,
    string InterfaceMaterialId,
    double InterfaceMaterialDensityGramsPerCm3,
    double ExpansionMm,
    double? BodyMaximumVolumetricFlowMm3PerSecond,
    double? InterfaceMaximumVolumetricFlowMm3PerSecond,
    ResolvedTreeSupportProfile Tree);

/// <summary>Resolved deterministic tree-support graph and trunk settings.</summary>
public sealed record ResolvedTreeSupportProfile(
    double TipSpacingMm,
    double MaximumBranchAngleFromVerticalDegrees,
    double MinimumRadiusMm,
    double MaximumRadiusMm,
    double RadiusPerTipMm,
    int WallCount,
    double HollowInteriorDensity);

/// <summary>Resolved machine motion, travel, cooling, and plate setup settings.</summary>
public sealed record ResolvedMotionProfile(
    double MaximumXYSpeedMmPerSecond,
    double MaximumZSpeedMmPerSecond,
    double PrintingAccelerationMmPerSecondSquared,
    double TravelAccelerationMmPerSecondSquared,
    double TravelSpeedMmPerSecond,
    double FirstLayerSpeedMultiplier,
    double MinimumLayerTimeSeconds,
    double MinimumCoolingSpeedMmPerSecond,
    double RetractLengthMm,
    double RetractSpeedMmPerSecond,
    double SetupSeconds,
    double NozzleHeatSeconds,
    double BedHeatSeconds,
    double ToolChangeSeconds,
    double MachinePrepareCompensationSeconds,
    double MachineLoadFilamentSeconds);

/// <summary>One rectangular unusable area in machine-bed coordinates.</summary>
public sealed record MachineExclusionZone(double MinimumX, double MinimumY, double MaximumX, double MaximumY);
