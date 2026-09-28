// <copyright file="AdmittedStlMeshReader.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>Parses bounded, server-admitted STL bytes into physical simulation input.</summary>
internal static class AdmittedStlMeshReader
{
    internal const int MaximumBytes = 32 * 1024 * 1024;
    internal const int MaximumTriangles = 500_000;

    internal static MeshInput Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
        {
            throw new FormatException("The STL input exceeds the physical analysis byte budget.");
        }

        if (bytes.Length >= 84)
        {
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(80, 4));
            if (count > 0 && count <= MaximumTriangles && 84L + (count * 50L) == bytes.Length)
            {
                var triangles = new MeshTriangle[checked((int)count)];
                for (int index = 0, offset = 84; index < triangles.Length; index++, offset += 50)
                {
                    triangles[index] = new(
                        Vertex(bytes.Slice(offset + 12, 12)),
                        Vertex(bytes.Slice(offset + 24, 12)),
                        Vertex(bytes.Slice(offset + 36, 12)));
                }

                return new MeshInput(triangles, 1, Matrix4x4.Identity);
            }
        }

        return ReadAscii(bytes);
    }

    private static MeshInput ReadAscii(ReadOnlySpan<byte> bytes)
    {
        string content = new UTF8Encoding(false, true).GetString(bytes);
        using var reader = new StringReader(content);
        var triangles = new List<MeshTriangle>();
        var vertices = new List<Vector3>(3);
        bool sawSolid = false;
        bool sawEndSolid = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 4096)
            {
                throw new FormatException("An ASCII STL line exceeds the analysis budget.");
            }

            string trimmed = line.Trim();
            if (trimmed.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
            {
                sawSolid = true;
            }
            else if (trimmed.StartsWith("endsolid", StringComparison.OrdinalIgnoreCase))
            {
                sawEndSolid = true;
            }
            else if (trimmed.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase))
            {
                string[] coordinates = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (coordinates.Length != 4 || !sawSolid || sawEndSolid)
                {
                    throw new FormatException("An ASCII STL vertex is malformed.");
                }

                vertices.Add(new(Parse(coordinates[1]), Parse(coordinates[2]), Parse(coordinates[3])));
                if (vertices.Count == 3)
                {
                    if (triangles.Count == MaximumTriangles)
                    {
                        throw new FormatException("The STL triangle budget was exceeded.");
                    }

                    triangles.Add(new(vertices[0], vertices[1], vertices[2]));
                    vertices.Clear();
                }
            }
        }

        if (!sawSolid || !sawEndSolid || triangles.Count == 0 || vertices.Count != 0)
        {
            throw new FormatException("The ASCII STL contains no complete solid.");
        }

        return new MeshInput(triangles, 1, Matrix4x4.Identity);
    }

    private static Vector3 Vertex(ReadOnlySpan<byte> bytes) => new(
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8, 4))));

    private static float Parse(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            && float.IsFinite(parsed)
            ? parsed
            : throw new FormatException("The ASCII STL contains a non-finite coordinate.");
}
