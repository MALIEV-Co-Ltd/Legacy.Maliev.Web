// <copyright file="SimulationContracts.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

/// <summary>Semantic role of one non-overlapping deposited or motion feature.</summary>
public enum ExtrusionRole
{
    /// <summary>Visible outer perimeter.</summary>
    OuterWall,
    /// <summary>Internal perimeter.</summary>
    InnerWall,
    /// <summary>Variable-width narrow-region fill.</summary>
    GapFill,
    /// <summary>Top solid skin.</summary>
    TopSkin,
    /// <summary>Bottom solid skin.</summary>
    BottomSkin,
    /// <summary>Internal solid fill.</summary>
    InternalSolid,
    /// <summary>Sparse model infill.</summary>
    SparseInfill,
    /// <summary>Anchored bridge extrusion.</summary>
    Bridge,
    /// <summary>Sparse support body.</summary>
    SupportBody,
    /// <summary>Dense support interface.</summary>
    SupportInterface,
    /// <summary>Support perimeter or branch sheath.</summary>
    SupportSheath,
    /// <summary>Skirt, brim, or raft adhesion material.</summary>
    Adhesion,
    /// <summary>Purge or flushing material.</summary>
    Purge,
}

/// <summary>A mesh triangle expressed in millimetres.</summary>
/// <param name="A">First vertex.</param>
/// <param name="B">Second vertex.</param>
/// <param name="C">Third vertex.</param>
public sealed record MeshTriangle(Vector3 A, Vector3 B, Vector3 C);

/// <summary>Raw mesh input with explicit units and component transform.</summary>
/// <param name="Triangles">Indexed or expanded input triangles.</param>
/// <param name="UnitsToMillimetres">Scale applied before analysis.</param>
/// <param name="ComponentTransform">Input assembly transform.</param>
public sealed record MeshInput(
    IReadOnlyList<MeshTriangle> Triangles,
    double UnitsToMillimetres,
    Matrix4x4 ComponentTransform);

/// <summary>Normalized finite mesh and its stable identity.</summary>
/// <param name="Triangles">Local-origin triangles in millimetres.</param>
/// <param name="Sha256">Canonical geometry digest.</param>
/// <param name="Diagnostics">Stable diagnostic codes.</param>
public sealed record NormalizedMesh(
    IReadOnlyList<MeshTriangle> Triangles,
    string Sha256,
    IReadOnlyList<string> Diagnostics);

/// <summary>Rigid build pose with a deterministic identifier.</summary>
/// <param name="Id">Stable tie-break identifier.</param>
/// <param name="Transform">Rigid transform.</param>
public sealed record Pose(string Id, Matrix4x4 Transform);

/// <summary>Geometry precision in millimetres.</summary>
/// <param name="CoordinateMm">Polygon coordinate precision.</param>
/// <param name="CurveChordMm">Maximum curve chord error.</param>
public sealed record GeometryTolerance(double CoordinateMm, double CurveChordMm);

/// <summary>Bounded analysis resources and cancellation.</summary>
/// <param name="MaximumTriangles">Triangle limit.</param>
/// <param name="MaximumLayers">Layer limit.</param>
/// <param name="MaximumPathSegments">Generated path-segment limit.</param>
/// <param name="CancellationToken">Caller cancellation.</param>
public sealed record AnalysisBudget(
    int MaximumTriangles,
    int MaximumLayers,
    int MaximumPathSegments,
    CancellationToken CancellationToken);

/// <summary>One closed polygon contour with outer/hole orientation retained.</summary>
/// <param name="Points">Ordered XY points in millimetres.</param>
/// <param name="IsHole">Whether this ring removes material.</param>
public sealed record PolygonRing(IReadOnlyList<Vector2> Points, bool IsHole);

/// <summary>Physical solid contours at one layer.</summary>
/// <param name="ZMm">Slice-plane height.</param>
/// <param name="ThicknessMm">Deposited layer thickness.</param>
/// <param name="Rings">Exterior and hole contours.</param>
public sealed record LayerContours(double ZMm, double ThicknessMm, IReadOnlyList<PolygonRing> Rings);

/// <summary>One ordered deposition path.</summary>
/// <param name="Points">Ordered XYZ path vertices.</param>
/// <param name="WidthMm">Extrusion width.</param>
/// <param name="HeightMm">Extrusion height.</param>
/// <param name="Role">Disjoint ledger role.</param>
/// <param name="MaterialId">Material/profile identity.</param>
/// <param name="SpeedMultiplier">Local overhang/first-layer speed modifier.</param>
public sealed record ExtrusionPath(
    IReadOnlyList<Vector3> Points,
    double WidthMm,
    double HeightMm,
    ExtrusionRole Role,
    string MaterialId,
    double SpeedMultiplier = 1d);

/// <summary>A polygonal classification mask at one physical layer.</summary>
/// <param name="ZMm">Physical layer centre.</param>
/// <param name="Rings">Exterior and hole contours.</param>
public sealed record LayerMask(double ZMm, IReadOnlyList<PolygonRing> Rings);

/// <summary>Planned model deposition and support-classification masks.</summary>
/// <param name="Paths">Disjoint model paths.</param>
/// <param name="BridgeMasks">Eligible two-anchor bridge regions.</param>
/// <param name="SupportDemandMasks">Unsupported regions that still require support.</param>
/// <param name="Diagnostics">Stable diagnostics.</param>
public sealed record ModelPathPlan(
    IReadOnlyList<ExtrusionPath> Paths,
    IReadOnlyList<LayerMask> BridgeMasks,
    IReadOnlyList<LayerMask> SupportDemandMasks,
    IReadOnlyList<string> Diagnostics);

/// <summary>Auditable support-region measurement.</summary>
/// <param name="RegionId">Stable region identifier.</param>
/// <param name="EnvelopeMm3">Unioned occupied geometric envelope.</param>
/// <param name="BodyDepositedMm3">Sparse/body deposited plastic.</param>
/// <param name="InterfaceDepositedMm3">Dense interface deposited plastic.</param>
/// <param name="SheathDepositedMm3">Perimeter/branch sheath deposited plastic.</param>
/// <param name="ReasonCodes">Accessibility/connectivity limitations.</param>
public sealed record SupportRegionReport(
    string RegionId,
    double EnvelopeMm3,
    double BodyDepositedMm3,
    double InterfaceDepositedMm3,
    double SheathDepositedMm3,
    IReadOnlyList<string> ReasonCodes);

/// <summary>Support paths and their auditable regions.</summary>
/// <param name="Paths">Support-only deposition paths.</param>
/// <param name="Regions">Support regions.</param>
/// <param name="UnsatisfiedDemand">Support demand that could not reach an allowed foundation.</param>
/// <param name="Overlay">Decimated support occupancy for advisory display.</param>
/// <param name="Diagnostics">Stable support limitations.</param>
public sealed record SupportPlan(
    IReadOnlyList<ExtrusionPath> Paths,
    IReadOnlyList<SupportRegionReport> Regions,
    IReadOnlyList<LayerMask> UnsatisfiedDemand,
    IReadOnlyList<LayerMask> Overlay,
    IReadOnlyList<string> Diagnostics);

/// <summary>Non-overlapping motion-time accounting.</summary>
/// <param name="RoleSeconds">Printing seconds by deposition role.</param>
/// <param name="TravelSeconds">Non-deposition motion seconds.</param>
/// <param name="CoolingSeconds">Cooling dwell seconds.</param>
/// <param name="PreparationSeconds">Critical-path plate preparation seconds.</param>
public sealed record MotionLedger(
    IReadOnlyDictionary<ExtrusionRole, double> RoleSeconds,
    double TravelSeconds,
    double CoolingSeconds,
    double PreparationSeconds)
{
    /// <summary>Gets total occupied-machine seconds.</summary>
    public double TotalSeconds => this.RoleSeconds.Values.Sum()
        + this.TravelSeconds
        + this.CoolingSeconds
        + this.PreparationSeconds;
}

/// <summary>Deposited material assigned to one and only one role.</summary>
/// <param name="Role">Ledger role.</param>
/// <param name="MaterialId">Material identifier.</param>
/// <param name="DepositedMm3">Deposited volume.</param>
/// <param name="Seconds">Role motion time.</param>
public sealed record RoleDeposition(
    ExtrusionRole Role,
    string MaterialId,
    double DepositedMm3,
    double Seconds);

/// <summary>Immutable physical simulation result, excluding money.</summary>
public sealed class SimulationResult
{
    private SimulationResult(
        string analysisVersion,
        string profileSha256,
        IReadOnlyList<RoleDeposition> roles,
        MotionLedger motion,
        IReadOnlyList<SupportRegionReport> supportRegions,
        IReadOnlyList<string> diagnostics)
    {
        this.AnalysisVersion = analysisVersion;
        this.ProfileSha256 = profileSha256;
        this.Roles = roles;
        this.Motion = motion;
        this.SupportRegions = supportRegions;
        this.Diagnostics = diagnostics;
        this.TotalDepositedMm3 = roles.Sum(role => role.DepositedMm3);
        this.SupportDepositedMm3 = roles
            .Where(role => role.Role is ExtrusionRole.SupportBody
                or ExtrusionRole.SupportInterface
                or ExtrusionRole.SupportSheath)
            .Sum(role => role.DepositedMm3);
        this.AdhesionDepositedMm3 = roles.Where(role => role.Role == ExtrusionRole.Adhesion).Sum(role => role.DepositedMm3);
        this.PurgeDepositedMm3 = roles.Where(role => role.Role == ExtrusionRole.Purge).Sum(role => role.DepositedMm3);
    }

    /// <summary>Gets the physical-analysis algorithm version.</summary>
    public string AnalysisVersion { get; }

    /// <summary>Gets the resolved manufacturing profile digest.</summary>
    public string ProfileSha256 { get; }

    /// <summary>Gets non-overlapping role ledger rows.</summary>
    public IReadOnlyList<RoleDeposition> Roles { get; }

    /// <summary>Gets the non-overlapping machine-time ledger.</summary>
    public MotionLedger Motion { get; }

    /// <summary>Gets auditable support region measurements.</summary>
    public IReadOnlyList<SupportRegionReport> SupportRegions { get; }

    /// <summary>Gets stable review and limitation reason codes.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Gets all deposited material, including adhesion and purge.</summary>
    public double TotalDepositedMm3 { get; }

    /// <summary>Gets support body, interface, and sheath deposition.</summary>
    public double SupportDepositedMm3 { get; }

    /// <summary>Gets adhesion deposition outside support ownership.</summary>
    public double AdhesionDepositedMm3 { get; }

    /// <summary>Gets purge deposition outside support ownership.</summary>
    public double PurgeDepositedMm3 { get; }

    /// <summary>Creates and validates a physical result ledger.</summary>
    /// <param name="analysisVersion">Algorithm revision.</param>
    /// <param name="profileSha256">Resolved profile digest.</param>
    /// <param name="roles">Disjoint deposition rows.</param>
    /// <returns>Validated immutable result.</returns>
    public static SimulationResult Create(
        string analysisVersion,
        string profileSha256,
        IReadOnlyList<RoleDeposition> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileSha256);
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Any(role => !double.IsFinite(role.DepositedMm3)
            || role.DepositedMm3 < 0
            || !double.IsFinite(role.Seconds)
            || role.Seconds < 0
            || string.IsNullOrWhiteSpace(role.MaterialId)))
        {
            throw new ArgumentException("Deposition rows require finite non-negative volume/time and a material ID.", nameof(roles));
        }

        var emptyMotion = new MotionLedger(
            Enum.GetValues<ExtrusionRole>().ToDictionary(role => role, _ => 0d),
            0,
            0,
            0);
        return new SimulationResult(analysisVersion, profileSha256, roles.ToArray(), emptyMotion, [], []);
    }

    /// <summary>Creates and validates a complete physical result with motion and support evidence.</summary>
    public static SimulationResult Create(
        string analysisVersion,
        string profileSha256,
        IReadOnlyList<RoleDeposition> roles,
        MotionLedger motion,
        IReadOnlyList<SupportRegionReport> supportRegions,
        IReadOnlyList<string> diagnostics)
    {
        SimulationResult basic = Create(analysisVersion, profileSha256, roles);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(supportRegions);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!double.IsFinite(motion.TotalSeconds) || motion.TotalSeconds < 0)
        {
            throw new ArgumentException("Motion ledger must contain finite non-negative time.", nameof(motion));
        }

        return new SimulationResult(
            basic.AnalysisVersion,
            basic.ProfileSha256,
            basic.Roles,
            motion,
            supportRegions.ToArray(),
            diagnostics.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Inputs for one deterministic physical simulation.</summary>
/// <param name="Mesh">Normalized mesh.</param>
/// <param name="Pose">Fixed build pose.</param>
/// <param name="Profile">Resolved manufacturing profile.</param>
/// <param name="Quantity">Requested part quantity.</param>
/// <param name="Budget">Resource budget.</param>
public sealed record SimulationRequest(
    NormalizedMesh Mesh,
    Pose Pose,
    ResolvedSimulationProfile Profile,
    int Quantity,
    AnalysisBudget Budget);

/// <summary>One evaluated build pose with explicit feasibility evidence.</summary>
public sealed record OrientationEvaluation(
    Pose Pose,
    bool Feasible,
    IReadOnlyList<string> ReasonCodes,
    double WidthMm,
    double DepthMm,
    double HeightMm,
    SimulationResult? Result);

/// <summary>One physical plate assignment and its recomputed result.</summary>
public sealed record PlateAssignment(int PlateNumber, int Quantity, SimulationResult Result);

/// <summary>Operator-reviewed proposal for geometry beyond an approved machine envelope.</summary>
public sealed record PartitionProposal(
    string Axis,
    IReadOnlyList<double> CutPlanesMm,
    IReadOnlyList<string> ReasonCodes);

/// <summary>Machine, orientation, packing, and optional partition scenario.</summary>
public sealed record MachineScenario(
    string MachineId,
    Pose? SelectedPose,
    IReadOnlyList<OrientationEvaluation> Orientations,
    IReadOnlyList<PlateAssignment> Plates,
    PartitionProposal? Partition,
    IReadOnlyList<string> Diagnostics);
