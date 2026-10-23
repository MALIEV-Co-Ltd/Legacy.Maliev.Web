// <copyright file="FdmSimulationGeometryTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests;

using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Xunit;

/// <summary>Freezes scale-independent, topology-preserving physical layer extraction.</summary>
public sealed class FdmSimulationGeometryTests
{
    private static readonly GeometryTolerance Tolerance = new(0.001, 0.02);

    /// <summary>Extracts the analytic section of a watertight cube on every physical layer.</summary>
    [Fact]
    public void Build_Cube_ProducesExpectedAreaOnEveryLayer()
    {
        NormalizedMesh mesh = Normalize(Box(Vector3.Zero, new Vector3(10)));

        IReadOnlyList<LayerContours> layers = LayerContourBuilder.Build(mesh, IdentityPose(), Profile(0.2), Budget());

        Assert.Equal(50, layers.Count);
        Assert.All(layers, layer => Assert.Equal(100, NetArea(layer), 3));
        Assert.Equal(0.1, layers[0].ZMm, 6);
        Assert.Equal(9.9, layers[^1].ZMm, 6);
    }

    /// <summary>Ordinary rounded bodies fit the generated-path allowance with either fill ownership.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_RoundedBody_PreservesEveryPhysicalLayerWithinPathBudget(bool grouped)
    {
        const int sides = 32;
        const float radius = 10;
        NormalizedMesh mesh = MeshNormalizer.Normalize(RoundedBody(grouped), Tolerance);
        Assert.Empty(mesh.Diagnostics);

        IReadOnlyList<LayerContours> layers = LayerContourBuilder.Build(mesh, IdentityPose(), Profile(0.2), Budget());

        Assert.Equal(100, layers.Count);
        double expectedArea = sides * radius * radius * Math.Sin(2 * Math.PI / sides) / 2;
        Assert.All(layers, layer =>
        {
            Assert.Single(layer.Rings);
            Assert.False(layer.Rings[0].IsHole);
            Assert.InRange(Math.Abs(NetArea(layer) - expectedArea), 0, 0.05);
        });
        Assert.Equal(0.1, layers[0].ZMm, 6);
        Assert.Equal(19.9, layers[^1].ZMm, 6);
    }

    /// <summary>Counts pair candidates independently from the conservative per-call output bound.</summary>
    [Fact]
    public void BooleanBudget_PairUnitsAndOutputBoundAreIndependent()
    {
        AnalysisBudget budget = Budget() with { MaximumBooleanWork = 2081 };
        var work = new LayerContourBuilder.BooleanWorkBudget(budget);

        Assert.Equal(16644, work.Admit(64, 1));
        Assert.Equal(0, work.Admit(0, 0));
        Assert.Equal("boolean_work_budget_exceeded", Assert.Throws<SimulationGeometryException>(() => work.Admit(1, 1)).ReasonCode);
        Assert.Equal(1_000_000, budget.MaximumPathSegments);
        Assert.Equal(1_000_000, Budget().MaximumBooleanWork);
    }

    /// <summary>Refuses the first candidate beyond the independent cumulative allowance.</summary>
    [Fact]
    public void BooleanBudget_CumulativeBoundaryIsExact()
    {
        var work = new LayerContourBuilder.BooleanWorkBudget(Budget() with { MaximumBooleanWork = 22 });
        Assert.Equal(84, work.Admit(4, 1));
        Assert.Equal(84, work.Admit(4, 1));
        Assert.Equal("boolean_work_budget_exceeded", Assert.Throws<SimulationGeometryException>(() => work.Admit(1, 0)).ReasonCode);
    }

    /// <summary>Invalid independent limits fail before returning any physical contours.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BooleanBudget_NonpositiveLimitIsRejected(int maximum)
    {
        Assert.Equal("analysis_budget_invalid", Assert.Throws<SimulationGeometryException>(() =>
            LayerContourBuilder.Build(Normalize(Box(Vector3.Zero, new Vector3(10))), IdentityPose(), Profile(0.2),
                Budget() with { MaximumBooleanWork = maximum })).ReasonCode);
    }

    /// <summary>Extreme counts fail closed instead of overflowing into an unbounded admission or generic error.</summary>
    [Theory]
    [InlineData((long)int.MaxValue, 1L)]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(0L, long.MaxValue)]
    public void BooleanBudget_OverflowIsAControlledRefusal(long edges, long paths)
    {
        var work = new LayerContourBuilder.BooleanWorkBudget(Budget(segments: int.MaxValue));
        Assert.Equal("path_segment_budget_exceeded", Assert.Throws<SimulationGeometryException>(() => work.Admit(edges, paths)).ReasonCode);
    }

    /// <summary>Charges extracted and both emitted contour generations, never their Boolean estimate.</summary>
    [Theory]
    [InlineData(799)]
    [InlineData(800)]
    public void Build_GroupedOutput_ConsumesExactGeneratedPathAllowance(int maximum)
    {
        IReadOnlyList<MeshTriangle> triangles = Box(Vector3.Zero, new Vector3(10));
        var mesh = MeshNormalizer.Normalize(new MeshInput(triangles, 1, Matrix4x4.Identity)
        {
            PositiveFillGroups = new int[triangles.Count],
        }, Tolerance);
        if (maximum == 799)
        {
            Assert.Equal("path_segment_budget_exceeded", Assert.Throws<SimulationGeometryException>(() =>
                LayerContourBuilder.Build(mesh, IdentityPose(), Profile(0.2), Budget(segments: maximum))).ReasonCode);
            return;
        }

        var layers = LayerContourBuilder.Build(mesh, IdentityPose(), Profile(0.2), Budget(segments: maximum));
        Assert.Equal(50, layers.Count);
        Assert.All(layers, layer =>
        {
            Assert.Single(layer.Rings);
            Assert.Equal(100, NetArea(layer), 3);
        });
    }

    /// <summary>The recovery entry point cannot convert a Boolean work refusal into physical evidence.</summary>
    [Fact]
    public void BooleanBudget_RefusalCannotEnterVoxelRecovery()
    {
        var mesh = MeshNormalizer.Normalize(RoundedBody(grouped: true), Tolerance);
        var request = new SimulationRequest(mesh, IdentityPose(), Profile(0.2), 1,
            Budget() with { MaximumBooleanWork = 1 });
        Assert.Equal("boolean_work_budget_exceeded", Assert.Throws<SimulationGeometryException>(() =>
            FdmSimulationEngine.EstimateRecoverable(request)).ReasonCode);
    }

    internal static MeshInput RoundedBody(bool grouped, float height = 20)
    {
        const int sides = 32;
        const float radius = 10;
        var triangles = new List<MeshTriangle>();
        for (int index = 0; index < sides; index++)
        {
            double angle = 2 * Math.PI * index / sides;
            double nextAngle = 2 * Math.PI * (index + 1) / sides;
            Vector3 first = new(radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle), 0);
            Vector3 next = new(radius * (float)Math.Cos(nextAngle), radius * (float)Math.Sin(nextAngle), 0);
            Vector3 topFirst = first + new Vector3(0, 0, height);
            Vector3 topNext = next + new Vector3(0, 0, height);
            triangles.Add(new(Vector3.Zero, next, first));
            triangles.Add(new(new Vector3(0, 0, height), topFirst, topNext));
            triangles.Add(new(first, next, topNext));
            triangles.Add(new(first, topNext, topFirst));
        }

        return new MeshInput(triangles, 1, Matrix4x4.Identity)
        {
            PositiveFillGroups = grouped ? new int[triangles.Count] : null,
        };
    }

    /// <summary>Preserves a void as a hole rather than charging its material as solid.</summary>
    [Fact]
    public void Build_HollowTube_ExcludesHoleFromArea()
    {
        var triangles = new List<MeshTriangle>();
        triangles.AddRange(Box(Vector3.Zero, new Vector3(10)));
        triangles.AddRange(Box(new Vector3(3, 3, 0), new Vector3(7, 7, 10)).Select(Reverse));
        NormalizedMesh mesh = Normalize(triangles);

        LayerContours layer = Assert.Single(LayerContourBuilder.Build(mesh, IdentityPose(), Profile(20), Budget()));

        Assert.Equal(84, NetArea(layer), 3);
        Assert.Single(layer.Rings, ring => ring.IsHole);
        Assert.Single(layer.Rings, ring => !ring.IsHole);
    }

    /// <summary>Keeps disconnected solid islands as independent exterior rings.</summary>
    [Fact]
    public void Build_DisconnectedIslands_PreservesBothSolids()
    {
        var triangles = new List<MeshTriangle>();
        triangles.AddRange(Box(Vector3.Zero, new Vector3(10)));
        triangles.AddRange(Box(new Vector3(20, 0, 0), new Vector3(30, 10, 10)));

        LayerContours layer = Assert.Single(LayerContourBuilder.Build(Normalize(triangles), IdentityPose(), Profile(20), Budget()));

        Assert.Equal(200, NetArea(layer), 3);
        Assert.Equal(2, layer.Rings.Count(ring => !ring.IsHole));
    }

    /// <summary>Uses topology rather than global winding as an arbitrary repair signal.</summary>
    [Fact]
    public void Build_ReversedWinding_ProducesSamePhysicalSection()
    {
        NormalizedMesh normal = Normalize(Box(Vector3.Zero, new Vector3(10)));
        NormalizedMesh reversed = Normalize(Box(Vector3.Zero, new Vector3(10)).Select(Reverse));

        LayerContours first = Assert.Single(LayerContourBuilder.Build(normal, IdentityPose(), Profile(20), Budget()));
        LayerContours second = Assert.Single(LayerContourBuilder.Build(reversed, IdentityPose(), Profile(20), Budget()));

        Assert.Equal(NetArea(first), NetArea(second), 6);
    }

    /// <summary>Does not turn an open shell into a plausible successful empty or solid slice.</summary>
    [Fact]
    public void Build_OpenMesh_FailsForReview()
    {
        List<MeshTriangle> open = Box(Vector3.Zero, new Vector3(10)).Skip(2).ToList();
        NormalizedMesh mesh = Normalize(open);

        SimulationGeometryException exception = Assert.Throws<SimulationGeometryException>(() =>
            LayerContourBuilder.Build(mesh, IdentityPose(), Profile(0.2), Budget()));

        Assert.Contains("mesh_open_edges", mesh.Diagnostics);
        Assert.Equal("geometry_requires_review", exception.ReasonCode);
    }

    /// <summary>Maintains fixed coordinate precision independently of part size and source translation.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(250)]
    [InlineData(500)]
    [InlineData(1500)]
    public void Build_TranslatedScaledCube_PreservesAnalyticArea(double size)
    {
        Vector3 offset = new(1_000_000, -2_000_000, 500_000);
        NormalizedMesh mesh = Normalize(Box(offset, offset + new Vector3((float)size)));

        LayerContours layer = Assert.Single(LayerContourBuilder.Build(mesh, IdentityPose(), Profile(size * 2), Budget()));

        Assert.Equal(size * size, NetArea(layer), 2);
    }

    /// <summary>Retains a sub-millimetre island next to metre-scale geometry.</summary>
    [Fact]
    public void Build_LargePartWithTinyDetail_DoesNotScaleAwayDetail()
    {
        var triangles = new List<MeshTriangle>();
        triangles.AddRange(Box(Vector3.Zero, new Vector3(1500, 1500, 1)));
        triangles.AddRange(Box(new Vector3(1501, 0, 0), new Vector3(1501.5f, 0.5f, 1)));

        LayerContours layer = Assert.Single(LayerContourBuilder.Build(Normalize(triangles), IdentityPose(), Profile(2), Budget()));

        Assert.Equal(2, layer.Rings.Count(ring => !ring.IsHole));
        Assert.Equal(2_250_000.25, NetArea(layer), 2);
    }

    /// <summary>Assigns geometry touching a layer plane once using the half-open ownership rule.</summary>
    [Fact]
    public void Build_CoplanarInternalFace_DoesNotDuplicateTheSection()
    {
        var triangles = new List<MeshTriangle>();
        triangles.AddRange(Box(Vector3.Zero, new Vector3(10, 10, 0.2f)));

        LayerContours layer = Assert.Single(LayerContourBuilder.Build(Normalize(triangles), IdentityPose(), Profile(0.2), Budget()));

        Assert.Equal(100, NetArea(layer), 3);
        Assert.Single(layer.Rings);
    }

    /// <summary>Reports cancellation instead of returning a partial successful slice.</summary>
    [Fact]
    public void Build_Cancelled_Throws()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => LayerContourBuilder.Build(
            Normalize(Box(Vector3.Zero, new Vector3(10))),
            IdentityPose(),
            Profile(0.2),
            Budget(cancellationToken: source.Token)));
    }

    /// <summary>Reports each operation budget explicitly instead of returning an empty success.</summary>
    [Theory]
    [InlineData(1, 100, 1000, "triangle_budget_exceeded")]
    [InlineData(100, 1, 1000, "layer_budget_exceeded")]
    [InlineData(100, 100, 1, "path_segment_budget_exceeded")]
    public void Build_BudgetExhausted_FailsClosed(int triangles, int layers, int segments, string reasonCode)
    {
        SimulationGeometryException exception = Assert.Throws<SimulationGeometryException>(() => LayerContourBuilder.Build(
            Normalize(Box(Vector3.Zero, new Vector3(10))),
            IdentityPose(),
            Profile(0.2),
            Budget(triangles, layers, segments)));

        Assert.Equal(reasonCode, exception.ReasonCode);
    }

    /// <summary>Rejects integer polygon coordinates that exceed the checked representation.</summary>
    [Fact]
    public void Build_CoordinateOverflow_FailsClosed()
    {
        Matrix4x4 pose = Matrix4x4.CreateTranslation(float.MaxValue, 0, 0);

        SimulationGeometryException exception = Assert.Throws<SimulationGeometryException>(() => LayerContourBuilder.Build(
            Normalize(Box(Vector3.Zero, new Vector3(10))),
            new Pose("overflow", pose),
            Profile(20),
            Budget()));

        Assert.Equal("geometry_coordinate_overflow", exception.ReasonCode);
    }

    private static NormalizedMesh Normalize(IEnumerable<MeshTriangle> triangles) => MeshNormalizer.Normalize(
        new MeshInput(triangles.ToArray(), 1, Matrix4x4.Identity),
        Tolerance);

    private static Pose IdentityPose() => new("identity", Matrix4x4.Identity);

    private static AnalysisBudget Budget(
        int triangles = 1000,
        int layers = 10_000,
        int segments = 1_000_000,
        CancellationToken cancellationToken = default) => new(triangles, layers, segments, cancellationToken);

    private static ResolvedSimulationProfile Profile(double layerHeight) => ResolvedSimulationProfile.Resolve(
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
            ["layer_height"] = layerHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            ["top_shell_layers"] = "4",
            ["bottom_shell_layers"] = "4",
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

    private static double NetArea(LayerContours layer) => layer.Rings.Sum(ring => ring.IsHole ? -Area(ring.Points) : Area(ring.Points));

    private static double Area(IReadOnlyList<Vector2> points)
    {
        double twiceArea = 0;
        for (int index = 0; index < points.Count; index++)
        {
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % points.Count];
            twiceArea += ((double)current.X * next.Y) - ((double)next.X * current.Y);
        }

        return Math.Abs(twiceArea) / 2;
    }

    private static MeshTriangle Reverse(MeshTriangle triangle) => new(triangle.A, triangle.C, triangle.B);

    private static IReadOnlyList<MeshTriangle> Box(Vector3 minimum, Vector3 maximum)
    {
        Vector3 p000 = new(minimum.X, minimum.Y, minimum.Z);
        Vector3 p100 = new(maximum.X, minimum.Y, minimum.Z);
        Vector3 p110 = new(maximum.X, maximum.Y, minimum.Z);
        Vector3 p010 = new(minimum.X, maximum.Y, minimum.Z);
        Vector3 p001 = new(minimum.X, minimum.Y, maximum.Z);
        Vector3 p101 = new(maximum.X, minimum.Y, maximum.Z);
        Vector3 p111 = new(maximum.X, maximum.Y, maximum.Z);
        Vector3 p011 = new(minimum.X, maximum.Y, maximum.Z);

        return
        [
            new(p000, p110, p100), new(p000, p010, p110),
            new(p001, p101, p111), new(p001, p111, p011),
            new(p000, p100, p101), new(p000, p101, p001),
            new(p010, p011, p111), new(p010, p111, p110),
            new(p000, p001, p011), new(p000, p011, p010),
            new(p100, p110, p111), new(p100, p111, p101),
        ];
    }
}
