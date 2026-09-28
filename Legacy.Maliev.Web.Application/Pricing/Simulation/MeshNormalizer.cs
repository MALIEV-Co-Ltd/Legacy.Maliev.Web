// <copyright file="MeshNormalizer.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

/// <summary>Normalizes finite mesh input into stable local millimetre coordinates.</summary>
public static class MeshNormalizer
{
    /// <summary>Applies units and component transforms, diagnoses topology, and computes a stable digest.</summary>
    public static NormalizedMesh Normalize(MeshInput input, GeometryTolerance tolerance) =>
        Normalize(input, tolerance, CancellationToken.None);

    /// <summary>Applies units and component transforms with cooperative cancellation.</summary>
    public static NormalizedMesh Normalize(
        MeshInput input,
        GeometryTolerance tolerance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Triangles);
        ArgumentNullException.ThrowIfNull(tolerance);
        if (!double.IsFinite(input.UnitsToMillimetres) || input.UnitsToMillimetres <= 0)
        {
            throw new SimulationGeometryException("geometry_units_invalid", "Mesh units must be finite and positive.");
        }

        ValidateTolerance(tolerance);
        ValidateMatrix(input.ComponentTransform);
        if (input.Triangles.Count == 0)
        {
            throw new SimulationGeometryException("geometry_empty", "A mesh must contain at least one triangle.");
        }

        var transformed = new List<MeshTriangle>(input.Triangles.Count);
        Vector3 minimum = new(float.PositiveInfinity);
        int index = 0;
        foreach (MeshTriangle triangle in input.Triangles)
        {
            if ((index++ & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Vector3 a = Transform(triangle.A, input.ComponentTransform, input.UnitsToMillimetres);
            Vector3 b = Transform(triangle.B, input.ComponentTransform, input.UnitsToMillimetres);
            Vector3 c = Transform(triangle.C, input.ComponentTransform, input.UnitsToMillimetres);
            transformed.Add(new MeshTriangle(a, b, c));
            minimum = Vector3.Min(minimum, Vector3.Min(a, Vector3.Min(b, c)));
        }

        var normalized = new MeshTriangle[transformed.Count];
        for (int triangleIndex = 0; triangleIndex < transformed.Count; triangleIndex++)
        {
            if ((triangleIndex & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            MeshTriangle triangle = transformed[triangleIndex];
            normalized[triangleIndex] = new MeshTriangle(
                triangle.A - minimum, triangle.B - minimum, triangle.C - minimum);
        }

        var diagnostics = Diagnose(normalized, tolerance.CoordinateMm, cancellationToken);
        return new NormalizedMesh(normalized, ComputeDigest(normalized, tolerance.CoordinateMm, cancellationToken), diagnostics);
    }

    private static IReadOnlyList<string> Diagnose(
        IReadOnlyList<MeshTriangle> triangles,
        double coordinateMm,
        CancellationToken cancellationToken)
    {
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        var edges = new Dictionary<EdgeKey, int>();
        double minimumAreaVector = coordinateMm * coordinateMm;
        for (int index = 0; index < triangles.Count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            MeshTriangle triangle = triangles[index];
            Vector3 areaVector = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
            if (areaVector.LengthSquared() <= minimumAreaVector * minimumAreaVector)
            {
                diagnostics.Add("mesh_degenerate_triangles");
                continue;
            }

            CountEdge(edges, triangle.A, triangle.B, coordinateMm);
            CountEdge(edges, triangle.B, triangle.C, coordinateMm);
            CountEdge(edges, triangle.C, triangle.A, coordinateMm);
        }

        if (edges.Values.Any(count => count == 1))
        {
            diagnostics.Add("mesh_open_edges");
        }

        if (edges.Values.Any(count => count > 2))
        {
            diagnostics.Add("mesh_nonmanifold_edges");
        }

        return diagnostics.ToArray();
    }

    private static void CountEdge(Dictionary<EdgeKey, int> edges, Vector3 first, Vector3 second, double precision)
    {
        VertexKey a = VertexKey.Create(first, precision);
        VertexKey b = VertexKey.Create(second, precision);
        EdgeKey edge = a.CompareTo(b) <= 0 ? new EdgeKey(a, b) : new EdgeKey(b, a);
        edges[edge] = edges.GetValueOrDefault(edge) + 1;
    }

    private static string ComputeDigest(
        IReadOnlyList<MeshTriangle> triangles,
        double precision,
        CancellationToken cancellationToken)
    {
        var canonical = new string[triangles.Count];
        for (int index = 0; index < triangles.Count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            MeshTriangle triangle = triangles[index];
            VertexKey[] vertices =
            [
                VertexKey.Create(triangle.A, precision),
                VertexKey.Create(triangle.B, precision),
                VertexKey.Create(triangle.C, precision),
            ];
            Array.Sort(vertices);
            canonical[index] = string.Join(';', vertices.Select(vertex => vertex.ToString()));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Array.Sort(canonical, StringComparer.Ordinal);
        string body = string.Join('\n', canonical);
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }

    private static Vector3 Transform(Vector3 point, Matrix4x4 transform, double scale)
    {
        if (!IsFinite(point))
        {
            throw new SimulationGeometryException("geometry_coordinate_invalid", "Mesh coordinates must be finite.");
        }

        Vector3 transformed = Vector3.Transform(point, transform) * (float)scale;
        if (!IsFinite(transformed))
        {
            throw new SimulationGeometryException("geometry_coordinate_invalid", "The mesh transform produced a non-finite coordinate.");
        }

        return transformed;
    }

    private static void ValidateTolerance(GeometryTolerance tolerance)
    {
        if (!double.IsFinite(tolerance.CoordinateMm) || tolerance.CoordinateMm <= 0
            || !double.IsFinite(tolerance.CurveChordMm) || tolerance.CurveChordMm <= 0)
        {
            throw new SimulationGeometryException("geometry_tolerance_invalid", "Geometry tolerances must be finite and positive.");
        }
    }

    private static void ValidateMatrix(Matrix4x4 matrix)
    {
        float[] values =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        ];
        if (values.Any(value => !float.IsFinite(value)))
        {
            throw new SimulationGeometryException("geometry_transform_invalid", "Mesh transforms must be finite.");
        }
    }

    private static bool IsFinite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    private readonly record struct EdgeKey(VertexKey First, VertexKey Second);

    private readonly record struct VertexKey(long X, long Y, long Z) : IComparable<VertexKey>
    {
        public static VertexKey Create(Vector3 point, double precision) => new(
            Quantize(point.X, precision),
            Quantize(point.Y, precision),
            Quantize(point.Z, precision));

        public int CompareTo(VertexKey other)
        {
            int x = this.X.CompareTo(other.X);
            if (x != 0)
            {
                return x;
            }

            int y = this.Y.CompareTo(other.Y);
            return y != 0 ? y : this.Z.CompareTo(other.Z);
        }

        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{this.X},{this.Y},{this.Z}");

        private static long Quantize(double value, double precision)
        {
            double scaled = Math.Round(value / precision, MidpointRounding.AwayFromZero);
            if (!double.IsFinite(scaled) || scaled < long.MinValue || scaled > long.MaxValue)
            {
                throw new SimulationGeometryException("geometry_coordinate_overflow", "A normalized coordinate exceeds the checked integer range.");
            }

            return checked((long)scaled);
        }
    }
}

/// <summary>Stable fail-closed physical-geometry exception.</summary>
public sealed class SimulationGeometryException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public SimulationGeometryException(string reasonCode, string message)
        : base(message)
    {
        this.ReasonCode = reasonCode;
    }

    /// <summary>Gets the stable machine-readable reason code.</summary>
    public string ReasonCode { get; }
}
