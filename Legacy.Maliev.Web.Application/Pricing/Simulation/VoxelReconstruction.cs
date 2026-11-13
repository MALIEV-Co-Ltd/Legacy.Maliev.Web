namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System.Numerics;

/// <summary>Server-owned pricing-only recovery; original geometry remains the manufacturing source.</summary>
internal static class VoxelReconstruction
{
    private const int MaximumCells = 8_000_000;
    private const int MaximumPlaneCells = 262_144;
    private const int MaximumRayCount = 1_000_000;
    private const int MaximumAxisCells = 1024;
    private const int MaximumProjectionWork = 30_000_000;
    private const int MaximumVoxelWork = 40_000_000;
    private const int MaximumCrossings = 2_000_000;

    internal sealed record Result(IReadOnlyList<LayerContours> Layers, VoxelReconstructionInfo Provenance);

    internal static Result Build(NormalizedMesh mesh, Pose pose, double layerHeightMm, AnalysisBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(budget);
        var token = budget.CancellationToken;
        token.ThrowIfCancellationRequested();
        if (budget.MaximumTriangles < 1 || budget.MaximumLayers < 1 || budget.MaximumPathSegments < 1
            || !double.IsFinite(layerHeightMm) || layerHeightMm <= 0)
            throw Failure("geometry_requires_review");
        if (mesh.Triangles.Count > budget.MaximumTriangles) throw Failure("triangle_budget_exceeded");
        int[]? groups = mesh.PositiveFillGroups?.ToArray();
        if (groups is not null && (groups.Length != mesh.Triangles.Count || groups.Any(group => group < 0)
            || groups.Distinct().Take(257).Count() > 256)) throw Failure("geometry_groups_invalid");
        var triangles = new MeshTriangle[mesh.Triangles.Count];
        var minimum = new double[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var maximum = new double[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        bool reflected = groups is not null && pose.Transform.GetDeterminant() < 0;
        for (int index = 0; index < triangles.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            MeshTriangle t = mesh.Triangles[index];
            Vector3 a = Vector3.Transform(t.A, pose.Transform), b = Vector3.Transform(t.B, pose.Transform),
                c = Vector3.Transform(t.C, pose.Transform);
            triangles[index] = reflected ? new(a, c, b) : new(a, b, c);
            foreach (Vector3 p in new[] { a, b, c })
            {
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                    throw Failure("geometry_requires_review");
                for (int axis = 0; axis < 3; axis++)
                {
                    double value = Coordinate(p, axis);
                    minimum[axis] = Math.Min(minimum[axis], value);
                    maximum[axis] = Math.Max(maximum[axis], value);
                }
            }
        }
        double[] extent = Enumerable.Range(0, 3).Select(axis => maximum[axis] - minimum[axis]).ToArray();
        if (extent.Any(value => !double.IsFinite(value) || value < 0.2)) throw Failure("geometry_requires_review");
        double ratio = extent[2] / layerHeightMm;
        if (!double.IsFinite(ratio) || ratio > budget.MaximumLayers + 0.000001) throw Failure("layer_budget_exceeded");
        int layerCount = (int)Math.Ceiling(ratio - 0.000001);
        if (layerCount < 1 || layerCount > budget.MaximumLayers) throw Failure("layer_budget_exceeded");
        double size = 0;
        int[] axes = [];
        foreach (double candidate in new[] { 0.2, 0.3, 0.4, 0.5 })
        {
            double[] counts = extent.Select(value => Math.Ceiling(value / candidate)).ToArray();
            if (counts.Any(value => value > MaximumAxisCells)) continue;
            int[] proposed = counts.Select(value => (int)value).ToArray();
            long total = (long)proposed[0] * proposed[1] * proposed[2];
            if (total > MaximumCells || (long)proposed[0] * proposed[1] > MaximumPlaneCells) continue;
            size = candidate;
            axes = proposed;
            break;
        }
        if (size == 0) throw Failure("geometry_complexity_exceeded");

        int cells = checked(axes[0] * axes[1] * axes[2]);
        var occupied = new byte[cells];
        var votes = new byte[cells];
        long work = 0, projectionWork = 0;
        int generatedSegments = 0;
        IEnumerable<IGrouping<int, int>> bodies = Enumerable.Range(0, triangles.Length)
            .GroupBy(index => groups?[index] ?? 0).OrderBy(group => group.Key);
        foreach (var body in bodies)
        {
            token.ThrowIfCancellationRequested();
            Work(cells);
            Array.Clear(votes);
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                var rays = new Dictionary<int, List<Crossing>>();
                int crossingCount = 0;
                foreach (int triangleIndex in body)
                {
                    token.ThrowIfCancellationRequested();
                    MeshTriangle t = triangles[triangleIndex];
                    double au = Coordinate(t.A, u), av = Coordinate(t.A, v);
                    double bu = Coordinate(t.B, u), bv = Coordinate(t.B, v);
                    double cu = Coordinate(t.C, u), cv = Coordinate(t.C, v);
                    double determinant = (bu - au) * (cv - av) - (bv - av) * (cu - au);
                    if (Math.Abs(determinant) < 1e-12) continue;
                    int u0 = Math.Max(0, (int)Math.Ceiling((Math.Min(au, Math.Min(bu, cu)) - minimum[u]) / size - 0.5));
                    int u1 = Math.Min(axes[u] - 1, (int)Math.Floor((Math.Max(au, Math.Max(bu, cu)) - minimum[u]) / size - 0.5));
                    int v0 = Math.Max(0, (int)Math.Ceiling((Math.Min(av, Math.Min(bv, cv)) - minimum[v]) / size - 0.5));
                    int v1 = Math.Min(axes[v] - 1, (int)Math.Floor((Math.Max(av, Math.Max(bv, cv)) - minimum[v]) / size - 0.5));
                    projectionWork = checked(projectionWork + (long)Math.Max(0, u1 - u0 + 1) * Math.Max(0, v1 - v0 + 1));
                    if (projectionWork > MaximumProjectionWork) throw Failure("geometry_complexity_exceeded");
                    for (int row = v0; row <= v1; row++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int column = u0; column <= u1; column++)
                        {
                            double pu = minimum[u] + (column + 0.5) * size - au;
                            double pv = minimum[v] + (row + 0.5) * size - av;
                            double beta = (pu * (cv - av) - pv * (cu - au)) / determinant;
                            double gamma = ((bu - au) * pv - (bv - av) * pu) / determinant;
                            if (beta < -1e-10 || gamma < -1e-10 || beta + gamma > 1 + 1e-10) continue;
                            double position = Coordinate(t.A, axis)
                                + beta * (Coordinate(t.B, axis) - Coordinate(t.A, axis))
                                + gamma * (Coordinate(t.C, axis) - Coordinate(t.A, axis));
                            if (++crossingCount > MaximumCrossings) throw Failure("geometry_complexity_exceeded");
                            int key = row * axes[u] + column;
                            if (!rays.TryGetValue(key, out var crossings))
                            {
                                if (rays.Count >= MaximumRayCount) throw Failure("geometry_complexity_exceeded");
                                rays.Add(key, crossings = []);
                            }
                            crossings.Add(new(position, -Math.Sign(determinant)));
                        }
                    }
                }
                foreach (var ray in rays)
                {
                    token.ThrowIfCancellationRequested();
                    ray.Value.Sort((left, right) => left.Position.CompareTo(right.Position));
                    var crossings = new List<Crossing>(ray.Value.Count);
                    for (int index = 0; index < ray.Value.Count; index++)
                    {
                        Crossing crossing = ray.Value[index];
                        if (index == 0 || crossing.Position - ray.Value[index - 1].Position > 0.000001)
                            crossings.Add(crossing);
                        else if (groups is not null && crossing.Direction != crossings[^1].Direction)
                            throw Failure("geometry_requires_review");
                    }
                    if (crossings.Count % 2 != 0) continue;
                    int winding = 0;
                    for (int index = 0; index + 1 < crossings.Count; index++)
                    {
                        winding += crossings[index].Direction;
                        bool inside = groups is null ? index % 2 == 0 : winding > 0;
                        if (!inside) continue;
                        int start = Math.Max(0, (int)Math.Ceiling((crossings[index].Position - minimum[axis]) / size - 0.5));
                        int end = Math.Min(axes[axis] - 1, (int)Math.Floor((crossings[index + 1].Position - minimum[axis]) / size - 0.5));
                        Work(Math.Max(0, end - start + 1));
                        for (int cell = start; cell <= end; cell++)
                        {
                            if ((cell & 255) == 0) token.ThrowIfCancellationRequested();
                            int uCell = ray.Key % axes[u], vCell = ray.Key / axes[u];
                            int x = axis == 0 ? cell : u == 0 ? uCell : vCell;
                            int y = axis == 1 ? cell : u == 1 ? uCell : vCell;
                            int z = axis == 2 ? cell : u == 2 ? uCell : vCell;
                            votes[x + axes[0] * (y + axes[1] * z)] |= (byte)(1 << axis);
                        }
                    }
                }
            }
            for (int index = 0; index < cells; index++)
            {
                if ((index & 1023) == 0) token.ThrowIfCancellationRequested();
                if (votes[index] is 3 or 5 or 6 or 7) occupied[index] = 1;
            }
        }
        if (!occupied.Any(value => value != 0)) throw Failure("geometry_requires_review");
        var planes = new Dictionary<int, IReadOnlyList<PolygonRing>>();
        var layers = new List<LayerContours>(layerCount);
        for (int layer = 0; layer < layerCount; layer++)
        {
            token.ThrowIfCancellationRequested();
            double bottom = layer * layerHeightMm, thickness = Math.Min(layerHeightMm, extent[2] - bottom);
            double z = bottom + thickness / 2;
            int plane = Math.Min(axes[2] - 1, (int)Math.Floor(z / size));
            if (!planes.TryGetValue(plane, out var rings))
            {
                rings = Rings(plane);
                planes.Add(plane, rings);
            }
            Charge(rings.Sum(ring => ring.Points.Count));
            layers.Add(new(z, thickness, rings));
        }
        return new(layers, new("voxel-majority-v1", size));

        void Work(long count)
        {
            work = checked(work + count);
            if (work > MaximumVoxelWork) throw Failure("geometry_complexity_exceeded");
        }
        void Charge(int count)
        {
            generatedSegments = checked(generatedSegments + count);
            if (generatedSegments > budget.MaximumPathSegments) throw Failure("path_segment_budget_exceeded");
        }
        bool Has(int x, int y, int z) => x >= 0 && y >= 0 && x < axes[0] && y < axes[1]
            && occupied[x + axes[0] * (y + axes[1] * z)] != 0;
        IReadOnlyList<PolygonRing> Rings(int z)
        {
            Work((long)axes[0] * axes[1]);
            var edges = new List<Edge>();
            var outgoing = new Dictionary<int, List<int>>();
            void Add(int x, int y, int nx, int ny, int direction)
            {
                Charge(1);
                int key = y * (axes[0] + 1) + x;
                if (!outgoing.TryGetValue(key, out var targets)) outgoing.Add(key, targets = []);
                targets.Add(edges.Count);
                edges.Add(new(x, y, ny * (axes[0] + 1) + nx, direction));
            }
            for (int y = 0; y < axes[1]; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < axes[0]; x++)
                {
                    if (!Has(x, y, z)) continue;
                    if (!Has(x, y - 1, z)) Add(x, y, x + 1, y, 0);
                    if (!Has(x + 1, y, z)) Add(x + 1, y, x + 1, y + 1, 1);
                    if (!Has(x, y + 1, z)) Add(x + 1, y + 1, x, y + 1, 2);
                    if (!Has(x - 1, y, z)) Add(x, y + 1, x, y, 3);
                }
            }
            var used = new bool[edges.Count];
            var rings = new List<PolygonRing>();
            for (int start = 0; start < edges.Count; start++)
            {
                token.ThrowIfCancellationRequested();
                if (used[start]) continue;
                int current = start;
                var points = new List<Vector2>();
                do
                {
                    token.ThrowIfCancellationRequested();
                    if (used[current]) throw Failure("geometry_requires_review");
                    used[current] = true;
                    Edge edge = edges[current];
                    points.Add(new((float)(minimum[0] + Math.Min(extent[0], edge.X * size)),
                        (float)(minimum[1] + Math.Min(extent[1], edge.Y * size))));
                    int next = -1;
                    if (outgoing.TryGetValue(edge.End, out var candidates))
                        foreach (int turn in new[] { 1, 0, 3, 2 })
                        {
                            foreach (int candidate in candidates)
                                if ((!used[candidate] || candidate == start)
                                    && (edges[candidate].Direction - edge.Direction + 4) % 4 == turn)
                                { next = candidate; break; }
                            if (next != -1) break;
                        }
                    if (next == -1) throw Failure("geometry_requires_review");
                    current = next;
                } while (current != start);
                var compact = new List<Vector2>();
                for (int index = 0; index < points.Count; index++)
                {
                    Vector2 before = points[(index + points.Count - 1) % points.Count],
                        p = points[index], after = points[(index + 1) % points.Count];
                    if ((double)(p.X - before.X) * (after.Y - p.Y)
                        != (double)(p.Y - before.Y) * (after.X - p.X)) compact.Add(p);
                }
                if (compact.Count < 3) throw Failure("geometry_requires_review");
                double area = 0;
                for (int index = 0; index < compact.Count; index++)
                {
                    Vector2 a = compact[index], b = compact[(index + 1) % compact.Count];
                    area += (double)a.X * b.Y - (double)b.X * a.Y;
                }
                if (area == 0) throw Failure("geometry_requires_review");
                rings.Add(new(compact.ToArray(), area < 0));
            }
            return rings.ToArray();
        }
    }

    private static double Coordinate(Vector3 point, int axis) => axis == 0 ? point.X : axis == 1 ? point.Y : point.Z;
    private static SimulationGeometryException Failure(string code) => new(code, "Bounded voxel pricing reconstruction is unavailable.");
    private readonly record struct Crossing(double Position, int Direction);
    private readonly record struct Edge(int X, int Y, int End, int Direction);
}
