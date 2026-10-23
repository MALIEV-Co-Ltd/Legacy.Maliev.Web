using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Literal physical controls for the server-only recoverable-mesh fallback.</summary>
public sealed class VoxelReconstructionTests
{
    [Theory]
    [InlineData(false, "", true)]
    [InlineData(false, "mesh_open_edges", false)]
    [InlineData(false, "mesh_nonmanifold_edges", false)]
    [InlineData(false, "mesh_degenerate_triangles", false)]
    [InlineData(true, "mesh_open_edges", true)]
    [InlineData(true, "mesh_nonmanifold_edges", true)]
    [InlineData(true, "mesh_degenerate_triangles", true)]
    [InlineData(true, "support_unsatisfied_demand", false)]
    [InlineData(true, "unknown_diagnostic", false)]
    public void PricingRecovery_AllowsOnlyRecoveredMeshWarnings(
        bool reconstructed, string diagnostic, bool priceable)
    {
        string[] diagnostics = diagnostic.Length == 0 ? [] : [diagnostic];
        var physical = SimulationResult.Create(FdmSimulationEngine.AnalysisVersion, new string('B', 64),
            [new RoleDeposition(ExtrusionRole.OuterWall, "PLA", 1000, 60)],
            new MotionLedger(new Dictionary<ExtrusionRole, double> { [ExtrusionRole.OuterWall] = 60 }, 0, 0, 0),
            [], diagnostics, reconstructed ? new("voxel-majority-v1", 0.2) : null);

        Assert.Equal(priceable, physical.HasPriceableDiagnostics);
        if (priceable)
            Assert.True(PricingEngine.QuoteFdmSimulation(physical, PricingCatalog.ResolveMaterial("PLA")!, 1, 1).UnitPrice > 0);
        else
            Assert.Throws<ArgumentException>(() =>
                PricingEngine.QuoteFdmSimulation(physical, PricingCatalog.ResolveMaterial("PLA")!, 1, 1));
    }

    [Fact]
    public void OpenBox_RecoversActualInteriorAndRetainsApproximationProvenance()
    {
        var mesh = Normalize(Box(Vector3.Zero, new(10)).Skip(2));
        var result = VoxelReconstruction.Build(mesh, Pose(), 0.2, Budget());

        Assert.Equal("voxel-majority-v1", result.Provenance.Method);
        Assert.Equal(0.2, result.Provenance.VoxelSizeMm);
        Assert.Equal(50, result.Layers.Count);
        Assert.All(result.Layers, layer => Assert.Equal(100, Area(layer), 3));
        Assert.Equal(1000, result.Layers.Sum(layer => Area(layer) * layer.ThicknessMm), 3);
    }

    [Fact]
    public void LegacyOppositeDuplicateCrossing_DeduplicatesPositionsWithoutInventingWindingRequirements()
    {
        MeshTriangle[] open = Box(Vector3.Zero, new(10)).Skip(2).ToArray();
        var mesh = Normalize([.. open, Reverse(open[2])]);
        Assert.Contains("mesh_nonmanifold_edges", mesh.Diagnostics);

        var result = VoxelReconstruction.Build(mesh, Pose(), 0.2, Budget());

        Assert.Equal("voxel-majority-v1", result.Provenance.Method);
        Assert.Equal(0.2, result.Provenance.VoxelSizeMm);
        Assert.Equal(50, result.Layers.Count);
        Assert.All(result.Layers, layer => Assert.Equal(100, Area(layer), 3));
        Assert.Equal(1000, result.Layers.Sum(layer => Area(layer) * layer.ThicknessMm), 3);
        var grouped = MeshNormalizer.Normalize(new MeshInput([.. open, Reverse(open[2])], 1, Matrix4x4.Identity)
        { PositiveFillGroups = Enumerable.Repeat(0, open.Length + 1).ToArray() }, new(0.001, 0.02));
        Assert.Equal("geometry_requires_review", Assert.Throws<SimulationGeometryException>(() =>
            VoxelReconstruction.Build(grouped, Pose(), 0.2, Budget())).ReasonCode);
    }

    [Fact]
    public void LegacyChainedNearCoincidentCrossings_UseOriginalAdjacentRawDeduplication()
    {
        MeshTriangle[] open = Box(Vector3.Zero, new(10)).Skip(2).ToArray();
        var triangles = open.ToList();
        foreach (float offset in new[] { 0.0000006f, 0.0000012f })
            foreach (int index in new[] { 8, 9 })
            {
                var triangle = open[index];
                var shift = new Vector3(offset, 0, 0);
                triangles.Add(new(triangle.A + shift, triangle.B + shift, triangle.C + shift));
            }
        var result = VoxelReconstruction.Build(Normalize(triangles), Pose(), 0.2, Budget());

        Assert.Equal("voxel-majority-v1", result.Provenance.Method);
        Assert.Equal(0.2, result.Provenance.VoxelSizeMm);
        Assert.Equal(50, result.Layers.Count);
        Assert.All(result.Layers, layer => Assert.Equal(100, Area(layer), 3));
        Assert.Equal(1000, result.Layers.Sum(layer => Area(layer) * layer.ThicknessMm), 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HollowShell_PreservesCavityRegardlessOfLegacyWinding(bool reversed)
    {
        var triangles = Box(Vector3.Zero, new(10))
            .Concat(Box(new(3), new(7)).Select(Reverse)).ToArray();
        if (reversed) triangles = triangles.Select(Reverse).ToArray();
        var result = VoxelReconstruction.Build(Normalize(triangles), Pose(), 0.2, Budget());

        Assert.Equal(50, result.Layers.Count);
        Assert.Equal(936, result.Layers.Sum(layer => Area(layer) * layer.ThicknessMm), 3);
        Assert.Equal(2, result.Layers[25].Rings.Count);
        Assert.Single(result.Layers[25].Rings, ring => ring.IsHole);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositiveObjectGroups_UnionOverlapWithoutInventingNestedHoles(bool nested)
    {
        MeshTriangle[] outer = Box(Vector3.Zero, new(10)).ToArray();
        MeshTriangle[] second = Box(nested ? new(3) : new(5, 0, 0),
            nested ? new(7) : new(15, 10, 10)).ToArray();
        var input = new MeshInput([.. outer, .. second], 1, Matrix4x4.Identity)
        { PositiveFillGroups = [.. Enumerable.Repeat(0, 12), .. Enumerable.Repeat(1, 12)] };
        var mesh = MeshNormalizer.Normalize(input, new(0.001, 0.02));
        var result = VoxelReconstruction.Build(mesh, Pose(), 0.2, Budget());

        Assert.NotEmpty(result.Layers);
        Assert.Equal(nested ? 1000 : 1500,
            result.Layers.Sum(layer => Area(layer) * layer.ThicknessMm), 3);
        Assert.All(result.Layers, layer => Assert.DoesNotContain(layer.Rings, ring => ring.IsHole));
    }

    [Theory]
    [InlineData(false, false, 1000)]
    [InlineData(true, false, 936)]
    [InlineData(true, true, 936)]
    public void OnePositiveObject_PreservesSignedCavityAndReflectedWinding(bool cavity, bool reflected, double volume)
    {
        var inner = Box(new(3), new(7));
        if (cavity) inner = inner.Select(Reverse);
        MeshTriangle[] triangles = [.. Box(Vector3.Zero, new(10)), .. inner];
        var input = new MeshInput(triangles, 1,
            reflected ? Matrix4x4.CreateScale(-1, 1, 1) : Matrix4x4.Identity)
        { PositiveFillGroups = Enumerable.Repeat(0, triangles.Length).ToArray() };
        var mesh = MeshNormalizer.Normalize(input, new(0.001, 0.02));
        var result = VoxelReconstruction.Build(mesh, Pose(), 0.2, Budget());

        Assert.NotEmpty(result.Layers);
        Assert.Equal(volume, result.Layers.Sum(layer => Area(layer) * layer.ThicknessMm), 3);
        Assert.Equal(cavity ? 2 : 1, result.Layers[25].Rings.Count);
    }

    [Theory]
    [InlineData(210, 0.3)]
    [InlineData(310, 0.4)]
    [InlineData(410, 0.5)]
    public void CandidatePitch_SelectsFirstAdmittedSourceGrid(float length, double pitch)
    {
        var result = VoxelReconstruction.Build(
            Normalize(Box(Vector3.Zero, new(length, 6, 6))), Pose(), 0.2, Budget());

        Assert.Equal(pitch, result.Provenance.VoxelSizeMm);
        Assert.NotEmpty(result.Layers);
        Assert.True(result.Layers.Sum(layer => Area(layer) * layer.ThicknessMm) > 0);
    }

    [Fact]
    public void SquarePlane_UsesOriginal262144CellCapBeforeSelectingPitch()
    {
        var result = VoxelReconstruction.Build(
            Normalize(Box(Vector3.Zero, new(110, 110, 10)).Skip(2)), Pose(), 0.2, Budget());

        Assert.Equal(0.3, result.Provenance.VoxelSizeMm);
        Assert.NotEmpty(result.Layers);
        Assert.Contains(result.Layers, layer => layer.Rings.Count > 0);
    }

    [Fact]
    public void Sheet_DoesNotInventASolid()
    {
        var mesh = Normalize([new(Vector3.Zero, new(10, 0, 0), new(0, 10, 0))]);
        var failure = Assert.Throws<SimulationGeometryException>(() =>
            VoxelReconstruction.Build(mesh, Pose(), 0.2, Budget()));
        Assert.Equal("geometry_requires_review", failure.ReasonCode);
    }

    [Theory]
    [InlineData(500, 500, 500)]
    [InlineData(600, 1, 1)]
    public void OversizeGrid_IsRefusedBeforeOccupancyAllocation(float x, float y, float z)
    {
        var failure = Assert.Throws<SimulationGeometryException>(() =>
            VoxelReconstruction.Build(Normalize(Box(Vector3.Zero, new(x, y, z))), Pose(), 1, Budget()));
        Assert.Equal("geometry_complexity_exceeded", failure.ReasonCode);
    }

    [Fact]
    public void LayerAndContourBudgets_AreNotBypassedByRecovery()
    {
        var mesh = Normalize(Box(Vector3.Zero, new(10)).Skip(2));
        Assert.Equal("layer_budget_exceeded", Assert.Throws<SimulationGeometryException>(() =>
            VoxelReconstruction.Build(mesh, Pose(), 0.2, Budget() with { MaximumLayers = 1 })).ReasonCode);
        Assert.Equal("path_segment_budget_exceeded", Assert.Throws<SimulationGeometryException>(() =>
            VoxelReconstruction.Build(mesh, Pose(), 0.2, Budget() with { MaximumPathSegments = 1 })).ReasonCode);
    }

    [Fact]
    public void CallerCancellation_IsNeverConvertedToAReviewOrAPrice()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => VoxelReconstruction.Build(
            Normalize(Box(Vector3.Zero, new(10))), Pose(), 0.2,
            Budget() with { CancellationToken = cancellation.Token }));
    }

    [Fact]
    public void TrustedFallback_IsActiveWhileStrictPrimitiveAndValidSolidsStayUnchanged()
    {
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();
        Assert.True(catalog.TryResolveTrustedProfile("PLA", BuildPreference.Standard, out var profile));
        var open = Normalize(Box(Vector3.Zero, new(10)).Skip(2));
        var request = new SimulationRequest(open, Pose(), profile!, 1, Budget());
        Assert.Equal("geometry_requires_review", Assert.Throws<SimulationGeometryException>(() =>
            FdmSimulationEngine.Estimate(request)).ReasonCode);
        var recovered = FdmSimulationEngine.EstimateRecoverable(request);
        Assert.NotNull(recovered.GeometryReconstruction);
        Assert.True(recovered.TotalDepositedMm3 > 0);
        Assert.True(recovered.Motion.TotalSeconds > 0);
        var closed = request with { Mesh = Normalize(Box(Vector3.Zero, new(10))) };
        var strict = FdmSimulationEngine.Estimate(closed);
        var unchanged = FdmSimulationEngine.EstimateRecoverable(closed);
        Assert.Null(unchanged.GeometryReconstruction);
        Assert.Equal(strict.TotalDepositedMm3, unchanged.TotalDepositedMm3);
        Assert.Equal(strict.Motion.TotalSeconds, unchanged.Motion.TotalSeconds);
        Assert.Equal(strict.Diagnostics, unchanged.Diagnostics);
    }

    [Fact]
    public void ReceiptDigest_PreservesOldNullProvenanceBytesAndBindsEveryNewProvenanceValue()
    {
        var roles = new[] { new RoleDeposition(ExtrusionRole.OuterWall, "PLA", 1, 2) };
        var motion = new MotionLedger(new Dictionary<ExtrusionRole, double>(), 0, 0, 0);
        var plain = SimulationResult.Create("analysis", new string('a', 64), roles, motion, [], []);
        var binding = new InstantQuotationPhysicalAnalysisBinding("session", "owner", Guid.NewGuid(), Guid.NewGuid(),
            new string('b', 64), "PLA", BuildPreference.Standard, 1, "profile", "PLA");
        var old = InstantQuotationPhysicalAnalysisReceipt.Create(binding, "PLA", plain, 1);
        string originalCanonical = "analysis|" + new string('a', 64) + "|1|0|0|1|";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(originalCanonical))), old.PhysicalSha256);
        Assert.Null(old.GeometryReconstruction);
        var first = SimulationResult.Create("analysis", new string('a', 64), roles, motion, [], [],
            new("voxel-majority-v1", 0.2));
        var second = SimulationResult.Create("analysis", new string('a', 64), roles, motion, [], [],
            new("voxel-majority-v1", 0.3));
        var a = InstantQuotationPhysicalAnalysisReceipt.Create(binding, "PLA", first, 1);
        var b = InstantQuotationPhysicalAnalysisReceipt.Create(binding, "PLA", second, 1);
        Assert.NotEqual(old.PhysicalSha256, a.PhysicalSha256);
        Assert.NotEqual(a.PhysicalSha256, b.PhysicalSha256);
        Assert.Equal(first.GeometryReconstruction, a.GeometryReconstruction);
    }

    private static NormalizedMesh Normalize(IEnumerable<MeshTriangle> triangles) =>
        MeshNormalizer.Normalize(new(triangles.ToArray(), 1, Matrix4x4.Identity), new(0.001, 0.02));

    private static Pose Pose() => new("source", Matrix4x4.Identity);

    private static AnalysisBudget Budget() => new(100_000, 2_000, 1_000_000, default);

    private static double Area(LayerContours layer)
    {
        Assert.NotEmpty(layer.Rings);
        return layer.Rings.Sum(ring =>
        {
            double twice = 0;
            for (int index = 0; index < ring.Points.Count; index++)
            {
                Vector2 a = ring.Points[index], b = ring.Points[(index + 1) % ring.Points.Count];
                twice += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            return (ring.IsHole ? -1 : 1) * Math.Abs(twice) / 2;
        });
    }

    private static MeshTriangle Reverse(MeshTriangle triangle) => new(triangle.A, triangle.C, triangle.B);

    private static IEnumerable<MeshTriangle> Box(Vector3 minimum, Vector3 maximum)
    {
        Vector3[] p = [new(minimum.X, minimum.Y, minimum.Z), new(maximum.X, minimum.Y, minimum.Z),
            new(maximum.X, maximum.Y, minimum.Z), new(minimum.X, maximum.Y, minimum.Z),
            new(minimum.X, minimum.Y, maximum.Z), new(maximum.X, minimum.Y, maximum.Z),
            new(maximum.X, maximum.Y, maximum.Z), new(minimum.X, maximum.Y, maximum.Z)];
        int[][] faces = [[0,2,1],[0,3,2],[4,5,6],[4,6,7],[0,1,5],[0,5,4],
            [1,2,6],[1,6,5],[2,3,7],[2,7,6],[3,0,4],[3,4,7]];
        return faces.Select(face => new MeshTriangle(p[face[0]], p[face[1]], p[face[2]]));
    }
}
