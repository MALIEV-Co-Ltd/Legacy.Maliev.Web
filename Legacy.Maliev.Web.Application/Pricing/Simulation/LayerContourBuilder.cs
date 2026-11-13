// <copyright file="LayerContourBuilder.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using Clipper2Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

/// <summary>Extracts deterministic physical layer contours from normalized triangles.</summary>
public static class LayerContourBuilder
{
    private const double CoordinatePrecisionMm = 0.001;
    private const double MaximumSafeIntegerCoordinate = 1_000_000_000_000_000d;

    /// <summary>Builds topology-preserving contours at actual deposited layer heights.</summary>
    public static IReadOnlyList<LayerContours> Build(
        NormalizedMesh mesh,
        Pose pose,
        ResolvedSimulationProfile profile,
        AnalysisBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(budget);
        budget.CancellationToken.ThrowIfCancellationRequested();
        ValidateBudget(budget);
        if (mesh.Diagnostics.Contains("mesh_open_edges", StringComparer.Ordinal)
            || mesh.Diagnostics.Contains("mesh_nonmanifold_edges", StringComparer.Ordinal))
        {
            throw new SimulationGeometryException("geometry_requires_review", "Open or nonmanifold mesh topology requires review.");
        }

        if (mesh.Triangles.Count > budget.MaximumTriangles)
        {
            throw new SimulationGeometryException("triangle_budget_exceeded", "Mesh triangle count exceeds the analysis budget.");
        }

        int[]? groups = mesh.PositiveFillGroups?.ToArray();
        if (groups is not null && (groups.Length != mesh.Triangles.Count
            || groups.Any(group => group < 0) || groups.Distinct().Take(257).Count() > 256))
        {
            throw new SimulationGeometryException("geometry_groups_invalid", "Positive-fill grouping is invalid.");
        }

        bool reflected = groups is not null && pose.Transform.GetDeterminant() < 0;
        MeshTriangle[] triangles = mesh.Triangles.Select(triangle =>
        {
            var posed = Transform(triangle, pose.Transform);
            return reflected ? new MeshTriangle(posed.A, posed.C, posed.B) : posed;
        }).ToArray();
        if (triangles.Length == 0)
        {
            throw new SimulationGeometryException("geometry_empty", "A mesh must contain at least one triangle.");
        }

        double minimumZ = triangles.Min(MinimumZ);
        double maximumZ = triangles.Max(MaximumZ);
        double height = maximumZ - minimumZ;
        if (!double.IsFinite(height) || height <= 0)
        {
            throw new SimulationGeometryException("geometry_requires_review", "The posed mesh has no printable height.");
        }

        double layerRatio = height / profile.LayerHeightMm;
        double nearestLayerCount = Math.Round(layerRatio);
        int layerCount = checked((int)(Math.Abs(layerRatio - nearestLayerCount) <= 0.000001
            ? nearestLayerCount
            : Math.Ceiling(layerRatio)));
        if (layerCount > budget.MaximumLayers)
        {
            throw new SimulationGeometryException("layer_budget_exceeded", "Physical layer count exceeds the analysis budget.");
        }

        IndexedTriangle[] ordered = triangles
            .Select((triangle, index) => new IndexedTriangle(index, triangle, MinimumZ(triangle), MaximumZ(triangle), groups?[index] ?? 0))
            .OrderBy(item => item.MinimumZ)
            .ThenBy(item => item.Index)
            .ToArray();
        var active = new List<IndexedTriangle>();
        var output = new List<LayerContours>(layerCount);
        int nextTriangle = 0;
        int generatedSegments = 0;
        var booleanWork = new BooleanWorkBudget(budget);
        for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
        {
            budget.CancellationToken.ThrowIfCancellationRequested();
            double bottom = minimumZ + (layerIndex * profile.LayerHeightMm);
            double thickness = Math.Min(profile.LayerHeightMm, maximumZ - bottom);
            double plane = bottom + (thickness / 2d);

            while (nextTriangle < ordered.Length && ordered[nextTriangle].MinimumZ <= plane)
            {
                active.Add(ordered[nextTriangle++]);
            }

            active.RemoveAll(item => item.MaximumZ < plane);
            IReadOnlyList<PolygonRing> rings;
            if (groups is null)
            {
                List<Segment64> segments = ExtractSegments(active, plane, cancellationToken: budget.CancellationToken);
                Charge(segments.Count);
                rings = BuildRings(segments);
            }
            else
            {
                var solids = new Paths64();
                foreach (var group in active.GroupBy(item => item.Group).OrderBy(group => group.Key))
                {
                    budget.CancellationToken.ThrowIfCancellationRequested();
                    var segments = ExtractSegments(group.ToArray(), plane, directed: true, budget.CancellationToken);
                    Charge(segments.Count);
                    foreach (var ring in BuildRings(segments, directed: true, budget, Charge, booleanWork))
                    {
                        var path = new Path64(ring.Points.Select(point => PointKey.Create(point).ToPoint64()));
                        solids.Add(path);
                    }
                }
                budget.CancellationToken.ThrowIfCancellationRequested();
                rings = Union(solids, FillRule.Positive, budget, Charge, booleanWork);
                budget.CancellationToken.ThrowIfCancellationRequested();

            }
            if (rings.Count == 0)
            {
                throw new SimulationGeometryException("geometry_slice_empty", "A physical layer unexpectedly produced no solid contour.");
            }

            output.Add(new LayerContours(plane - minimumZ, thickness, rings));
        }

        return output;

        void Charge(int count)
        {
            if (count > budget.MaximumPathSegments - generatedSegments)
            {
                throw new SimulationGeometryException("path_segment_budget_exceeded", "Generated section paths exceed the analysis budget.");
            }
            generatedSegments = checked(generatedSegments + count);
        }
    }

    private static List<Segment64> ExtractSegments(
        IReadOnlyList<IndexedTriangle> active, double plane, bool directed = false,
        CancellationToken cancellationToken = default)
    {
        var unique = new SortedSet<Segment64>(Segment64Comparer.Instance);
        int processed = 0;
        foreach (IndexedTriangle indexed in active)
        {
            if ((processed++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            MeshTriangle triangle = indexed.Triangle;
            Vector3[] vertices = [triangle.A, triangle.B, triangle.C];
            Vector2[] intersections = new Vector2[3];
            int count = 0;
            for (int edge = 0; edge < 3; edge++)
            {
                Vector3 first = vertices[edge];
                Vector3 second = vertices[(edge + 1) % 3];
                bool crosses = (first.Z <= plane && second.Z > plane)
                    || (second.Z <= plane && first.Z > plane);
                if (!crosses)
                {
                    continue;
                }

                double fraction = (plane - first.Z) / (second.Z - first.Z);
                intersections[count++] = new Vector2(
                    (float)(first.X + ((second.X - first.X) * fraction)),
                    (float)(first.Y + ((second.Y - first.Y) * fraction)));
            }

            if (count != 2)
            {
                continue;
            }

            PointKey firstPoint = PointKey.Create(intersections[0]);
            PointKey secondPoint = PointKey.Create(intersections[1]);
            if (firstPoint == secondPoint)
            {
                continue;
            }

            if (directed)
            {
                Vector3 normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
                Vector2 tangent = new(-normal.Y, normal.X);
                if (Vector2.Dot(intersections[1] - intersections[0], tangent) < 0)
                    (firstPoint, secondPoint) = (secondPoint, firstPoint);
                unique.Add(new Segment64(firstPoint, secondPoint));
            }
            else
            {
                unique.Add(firstPoint.CompareTo(secondPoint) <= 0
                    ? new Segment64(firstPoint, secondPoint)
                    : new Segment64(secondPoint, firstPoint));
            }
        }

        return unique.ToList();
    }

    private static IReadOnlyList<PolygonRing> BuildRings(IReadOnlyList<Segment64> segments, bool directed = false,
        AnalysisBudget? budget = null, Action<int>? charge = null, BooleanWorkBudget? booleanWork = null)
    {
        var adjacency = new Dictionary<PointKey, List<int>>();
        for (int index = 0; index < segments.Count; index++)
        {
            AddAdjacency(adjacency, segments[index].First, index);
            AddAdjacency(adjacency, segments[index].Second, index);
        }

        if (adjacency.Values.Any(edges => edges.Count != 2))
        {
            throw new SimulationGeometryException("geometry_requires_review", "Section segments do not form closed two-manifold loops.");
        }

        bool[] visited = new bool[segments.Count];
        var paths = new Paths64();
        for (int startIndex = 0; startIndex < segments.Count; startIndex++)
        {
            if (visited[startIndex])
            {
                continue;
            }

            Segment64 start = segments[startIndex];
            PointKey startPoint = start.First;
            PointKey current = startPoint;
            int currentEdge = startIndex;
            var path = new Path64();
            do
            {
                path.Add(current.ToPoint64());
                visited[currentEdge] = true;
                Segment64 segment = segments[currentEdge];
                if (directed && segment.First != current)
                    throw new SimulationGeometryException("geometry_requires_review", "Oriented section edges are inconsistent.");
                PointKey next = segment.First == current ? segment.Second : segment.First;
                current = next;
                if (current == startPoint)
                {
                    break;
                }

                currentEdge = adjacency[current]
                    .Where(edge => !visited[edge] && (!directed || segments[edge].First == current))
                    .OrderBy(edge => segments[edge].Other(current))
                    .FirstOrDefault(-1);
                if (currentEdge < 0)
                {
                    throw new SimulationGeometryException("geometry_requires_review", "Section loop ended before returning to its start.");
                }
            }
            while (path.Count <= segments.Count);

            if (path.Count < 3 || current != startPoint)
            {
                throw new SimulationGeometryException("geometry_requires_review", "Section loop is not a valid closed polygon.");
            }

            paths.Add(path);
        }

        return Union(paths, directed ? FillRule.Positive : FillRule.EvenOdd, budget, charge, booleanWork);
    }

    private static IReadOnlyList<PolygonRing> Union(Paths64 paths, FillRule fill,
        AnalysisBudget? budget = null, Action<int>? charge = null, BooleanWorkBudget? booleanWork = null)
    {
        long admittedWork = long.MaxValue;
        if (budget is not null)
        {
            budget.CancellationToken.ThrowIfCancellationRequested();
            long edges = paths.Sum(path => (long)path.Count);
            admittedWork = booleanWork!.Admit(edges, paths.Count);
        }
        var clipper = new Clipper64();
        clipper.AddSubject(paths);
        var tree = new PolyTree64();
        if (!clipper.Execute(ClipType.Union, fill, tree))
        {
            throw new SimulationGeometryException("geometry_boolean_failed", "Integer polygon reconstruction failed.");
        }

        var rings = new List<PolygonRing>();
        if (budget is null)
        {
            for (int index = 0; index < tree.Count; index++) Append(tree[index], rings);
        }
        else
        {
            budget.CancellationToken.ThrowIfCancellationRequested();
            var pending = new Stack<PolyPath64>();
            for (int index = 0; index < tree.Count; index++) pending.Push(tree[index]);
            long observedWork = 0;
            while (pending.TryPop(out var node))
            {
                budget.CancellationToken.ThrowIfCancellationRequested();
                if (++observedWork > admittedWork) throw BooleanBudget();
                if (node.Polygon is { Count: >= 3 } polygon)
                {
                    if (observedWork + polygon.Count > admittedWork) throw BooleanBudget();
                    observedWork += polygon.Count;
                    charge?.Invoke(polygon.Count);
                    var points = new Vector2[polygon.Count];
                    int start = 0;
                    for (int index = 0; index < polygon.Count; index++)
                    {
                        if ((index & 1023) == 0) budget.CancellationToken.ThrowIfCancellationRequested();
                        points[index] = new((float)(polygon[index].X * CoordinatePrecisionMm),
                            (float)(polygon[index].Y * CoordinatePrecisionMm));
                        if (points[index].X < points[start].X
                            || (points[index].X == points[start].X && points[index].Y < points[start].Y)) start = index;
                    }
                    var canonical = new Vector2[points.Length];
                    for (int index = 0; index < points.Length; index++)
                    {
                        if ((index & 1023) == 0) budget.CancellationToken.ThrowIfCancellationRequested();
                        canonical[index] = points[(start + index) % points.Length];
                    }
                    rings.Add(new PolygonRing(canonical, node.IsHole));
                }
                if (observedWork + pending.Count + node.Count > admittedWork) throw BooleanBudget();
                for (int index = 0; index < node.Count; index++) pending.Push(node[index]);
            }
        }

        return rings
            .OrderBy(ring => ring.IsHole)
            .ThenBy(ring => ring.Points.Min(point => point.X))
            .ThenBy(ring => ring.Points.Min(point => point.Y))
            .ThenByDescending(ring => Math.Abs(SignedArea(ring.Points)))
            .ToArray();
    }

    private static SimulationGeometryException BooleanBudget() => new(
        "path_segment_budget_exceeded", "Positive-fill boolean output exceeded its admitted work bound.");

    internal sealed class BooleanWorkBudget(AnalysisBudget budget)
    {
        private long consumed;

        internal long Admit(long edges, long paths)
        {
            budget.CancellationToken.ThrowIfCancellationRequested();
            if (budget.MaximumBooleanWork <= 0)
                throw new SimulationGeometryException("analysis_budget_invalid", "Analysis budgets must be positive.");
            if (edges < 0 || paths < 0)
                throw new ArgumentOutOfRangeException(edges < 0 ? nameof(edges) : nameof(paths));

            long outputBound;
            long candidates;
            try
            {
                // Keep the synchronous Clipper call's conservative intersection/output admission bound.
                outputBound = checked((4 * edges * (edges - 1)) + (8 * edges) + (4 * paths));
                candidates = checked((edges * (edges - 1) / 2) + edges + paths);
            }
            catch (OverflowException)
            {
                throw BooleanBudget();
            }
            if (outputBound > budget.MaximumPathSegments || outputBound > int.MaxValue)
                throw BooleanBudget();
            if (candidates > budget.MaximumBooleanWork - consumed)
                throw new SimulationGeometryException("boolean_work_budget_exceeded", "Cumulative positive-fill candidate work exceeds the analysis budget.");
            consumed = checked(consumed + candidates);
            return outputBound;
        }
    }

    private static void Append(PolyPath64 node, List<PolygonRing> rings)
    {
        if (node.Polygon is { Count: >= 3 } polygon)
        {
            IReadOnlyList<Vector2> points = Canonicalize(polygon.Select(point => new Vector2(
                (float)(point.X * CoordinatePrecisionMm),
                (float)(point.Y * CoordinatePrecisionMm))).ToArray());
            rings.Add(new PolygonRing(points, node.IsHole));
        }

        for (int index = 0; index < node.Count; index++)
        {
            Append(node[index], rings);
        }
    }

    private static IReadOnlyList<Vector2> Canonicalize(IReadOnlyList<Vector2> points)
    {
        int start = Enumerable.Range(0, points.Count)
            .OrderBy(index => points[index].X)
            .ThenBy(index => points[index].Y)
            .First();
        return Enumerable.Range(0, points.Count).Select(offset => points[(start + offset) % points.Count]).ToArray();
    }

    private static double SignedArea(IReadOnlyList<Vector2> points)
    {
        double twiceArea = 0;
        for (int index = 0; index < points.Count; index++)
        {
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % points.Count];
            twiceArea += ((double)current.X * next.Y) - ((double)next.X * current.Y);
        }

        return twiceArea / 2d;
    }

    private static void AddAdjacency(Dictionary<PointKey, List<int>> adjacency, PointKey point, int segmentIndex)
    {
        if (!adjacency.TryGetValue(point, out List<int>? edges))
        {
            edges = [];
            adjacency.Add(point, edges);
        }

        edges.Add(segmentIndex);
    }

    private static MeshTriangle Transform(MeshTriangle triangle, Matrix4x4 transform) => new(
        Transform(triangle.A, transform),
        Transform(triangle.B, transform),
        Transform(triangle.C, transform));

    private static Vector3 Transform(Vector3 point, Matrix4x4 transform)
    {
        Vector3 result = Vector3.Transform(point, transform);
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y) || !float.IsFinite(result.Z))
        {
            throw new SimulationGeometryException("geometry_coordinate_invalid", "The build pose produced a non-finite coordinate.");
        }

        return result;
    }

    private static double MinimumZ(MeshTriangle triangle) => Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z));

    private static double MaximumZ(MeshTriangle triangle) => Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z));

    private static void ValidateBudget(AnalysisBudget budget)
    {
        if (budget.MaximumTriangles <= 0 || budget.MaximumLayers <= 0 || budget.MaximumPathSegments <= 0
            || budget.MaximumBooleanWork <= 0)
        {
            throw new SimulationGeometryException("analysis_budget_invalid", "Analysis budgets must be positive.");
        }
    }

    private readonly record struct IndexedTriangle(int Index, MeshTriangle Triangle, double MinimumZ, double MaximumZ, int Group);

    private readonly record struct Segment64(PointKey First, PointKey Second)
    {
        public PointKey Other(PointKey point) => this.First == point ? this.Second : this.First;
    }

    private sealed class Segment64Comparer : IComparer<Segment64>
    {
        public static Segment64Comparer Instance { get; } = new();

        public int Compare(Segment64 left, Segment64 right)
        {
            int first = left.First.CompareTo(right.First);
            return first != 0 ? first : left.Second.CompareTo(right.Second);
        }
    }

    private readonly record struct PointKey(long X, long Y) : IComparable<PointKey>
    {
        public static PointKey Create(Vector2 point) => new(Quantize(point.X), Quantize(point.Y));

        public int CompareTo(PointKey other)
        {
            int x = this.X.CompareTo(other.X);
            return x != 0 ? x : this.Y.CompareTo(other.Y);
        }

        public Point64 ToPoint64() => new(this.X, this.Y);

        private static long Quantize(double value)
        {
            double scaled = Math.Round(value / CoordinatePrecisionMm, MidpointRounding.AwayFromZero);
            if (!double.IsFinite(scaled) || Math.Abs(scaled) > MaximumSafeIntegerCoordinate)
            {
                throw new SimulationGeometryException("geometry_coordinate_overflow", "A polygon coordinate exceeds the checked Clipper range.");
            }

            return checked((long)scaled);
        }
    }
}
