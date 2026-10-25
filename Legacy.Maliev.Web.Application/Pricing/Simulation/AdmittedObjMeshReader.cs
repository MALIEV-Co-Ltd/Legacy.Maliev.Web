// <copyright file="AdmittedObjMeshReader.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>Reads bounded polygonal OBJ geometry from exact server-admitted bytes.</summary>
internal static class AdmittedObjMeshReader
{
    internal const int MaximumBytes = AdmittedStlMeshReader.MaximumBytes;
    internal const int MaximumTriangles = AdmittedStlMeshReader.MaximumTriangles;
    internal const int MaximumVertices = MaximumTriangles * 3;
    private const int MaximumLineLength = 4096;
    private const int MaximumFaceVertices = 256;

    // OBJ has no physical unit declaration. Match the quotation preview's millimetre convention.
    // Curves, points, lines, non-unit homogeneous weights and non-convex/non-planar faces
    // require review; external material records are cosmetic and are never dereferenced.
    internal static MeshInput Read(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
        {
            throw new FormatException("The OBJ input exceeds the physical analysis byte budget.");
        }

        string content = new UTF8Encoding(false, true).GetString(bytes);
        using var reader = new StringReader(content);
        var vertices = new List<Vector3>();
        var triangles = new List<MeshTriangle>();
        int textureCount = 0, normalCount = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length > MaximumLineLength)
            {
                throw new FormatException("An OBJ line exceeds the physical analysis budget.");
            }

            while (line.EndsWith('\\'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? next = reader.ReadLine();
                if (next is null || line.Length + next.Length > MaximumLineLength)
                {
                    throw new FormatException("An OBJ continuation is incomplete or exceeds the line budget.");
                }

                line = line[..^1] + " " + next;
            }

            int comment = line.IndexOf('#');
            string record = (comment < 0 ? line : line[..comment]).Trim().TrimStart('\uFEFF');
            if (record.Length == 0)
            {
                continue;
            }

            string[] tokens = record.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0])
            {
                case "v":
                    if (tokens.Length is not (4 or 5 or 7) || vertices.Count == MaximumVertices)
                    {
                        throw new FormatException("An OBJ vertex is malformed or exceeds the vertex budget.");
                    }

                    for (int index = 1; index < tokens.Length; index++)
                    {
                        _ = Coordinate(tokens[index]);
                    }

                    if (tokens.Length == 5 && Coordinate(tokens[4]) != 1)
                    {
                        throw new FormatException("Non-unit homogeneous OBJ coordinates require review.");
                    }

                    vertices.Add(new(Coordinate(tokens[1]), Coordinate(tokens[2]), Coordinate(tokens[3])));
                    break;
                case "vt":
                    if (tokens.Length is < 2 or > 4 || textureCount == MaximumVertices)
                    {
                        throw new FormatException("An OBJ texture coordinate is malformed or exceeds the budget.");
                    }

                    for (int index = 1; index < tokens.Length; index++)
                    {
                        _ = Coordinate(tokens[index]);
                    }

                    textureCount++;
                    break;
                case "vn":
                    if (tokens.Length != 4 || normalCount == MaximumVertices)
                    {
                        throw new FormatException("An OBJ normal is malformed or exceeds the budget.");
                    }

                    _ = Coordinate(tokens[1]);
                    _ = Coordinate(tokens[2]);
                    _ = Coordinate(tokens[3]);
                    normalCount++;
                    break;
                case "f":
                    int count = tokens.Length - 1;
                    if (count is < 3 or > MaximumFaceVertices || triangles.Count + count - 2 > MaximumTriangles)
                    {
                        throw new FormatException("An OBJ face is malformed or exceeds the triangle budget.");
                    }

                    var face = new Vector3[count];
                    for (int index = 0; index < count; index++)
                    {
                        string[] reference = tokens[index + 1].Split('/');
                        if (reference.Length > 3 || reference[0].Length == 0
                            || (reference.Length == 2 && reference[1].Length == 0)
                            || (reference.Length == 3 && reference[2].Length == 0))
                        {
                            throw new FormatException("An OBJ face reference is malformed.");
                        }

                        face[index] = vertices[ResolveIndex(reference[0], vertices.Count)];
                        if (reference.Length >= 2 && reference[1].Length > 0)
                        {
                            _ = ResolveIndex(reference[1], textureCount);
                        }

                        if (reference.Length == 3)
                        {
                            _ = ResolveIndex(reference[2], normalCount);
                        }
                    }

                    if (count > 3)
                    {
                        RequireConvexPlanarFace(face, cancellationToken);
                    }

                    for (int index = 1; index < count - 1; index++)
                    {
                        triangles.Add(new(face[0], face[index], face[index + 1]));
                    }

                    break;
                case "o":
                case "g":
                case "s":
                case "mtllib":
                case "usemtl":
                case "usemap":
                    break;
                default:
                    throw new FormatException("The OBJ contains unsupported geometric records.");
            }
        }

        if (triangles.Count == 0)
        {
            throw new FormatException("The OBJ contains no polygonal surface.");
        }

        return new MeshInput(triangles, 1, Matrix4x4.Identity);
    }

    private static float Coordinate(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            && float.IsFinite(parsed)
            ? parsed
            : throw new FormatException("The OBJ contains a malformed or non-finite coordinate.");

    private static int ResolveIndex(string value, int count)
    {
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed)
            || parsed == 0)
        {
            throw new FormatException("An OBJ index is malformed.");
        }

        long resolved = parsed > 0 ? (long)parsed - 1 : (long)count + parsed;
        return resolved >= 0 && resolved < count
            ? (int)resolved
            : throw new FormatException("An OBJ index references an unavailable element.");
    }

    private static void RequireConvexPlanarFace(Vector3[] face, CancellationToken cancellationToken)
    {
        Vector3 normal = Vector3.Cross(face[1] - face[0], face[2] - face[0]);
        float length = normal.Length();
        if (!float.IsFinite(length) || length <= 0)
        {
            throw new FormatException("A degenerate OBJ polygon requires review.");
        }

        normal /= length;
        for (int edge = 0; edge < face.Length; edge++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float distance = Vector3.Dot(face[edge] - face[0], normal);
            if (!float.IsFinite(distance) || Math.Abs(distance) > 0.001f)
            {
                throw new FormatException("A non-planar OBJ polygon requires review.");
            }

            Vector3 start = face[edge], direction = face[(edge + 1) % face.Length] - start;
            if (direction.LengthSquared() <= 0)
            {
                throw new FormatException("A repeated OBJ polygon vertex requires review.");
            }

            foreach (Vector3 point in face)
            {
                float side = Vector3.Dot(Vector3.Cross(direction, point - start), normal);
                if (!float.IsFinite(side) || side < 0)
                {
                    throw new FormatException("A non-convex OBJ polygon requires review.");
                }
            }
        }
    }
}
