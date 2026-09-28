// <copyright file="AdmittedStlMeshReader.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading;

/// <summary>Parses bounded, server-admitted STL bytes into physical simulation input.</summary>
internal static class AdmittedStlMeshReader
{
    // The clean-read transport allows 32 MiB. This bridge has a smaller budget because
    // normalization also materializes topology edges and a sorted canonical digest.
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal const int MaximumTriangles = 50_000;

    internal static MeshInput Read(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
        {
            throw new FormatException("The STL input exceeds the physical analysis byte budget.");
        }

        if (bytes.Length >= 84)
        {
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(80, 4));
            if (count > 0 && 84L + (count * 50L) == bytes.Length)
            {
                if (count > MaximumTriangles)
                {
                    throw new FormatException("The STL triangle budget was exceeded.");
                }

                var triangles = new MeshTriangle[checked((int)count)];
                for (int index = 0, offset = 84; index < triangles.Length; index++, offset += 50)
                {
                    if ((index & 1023) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    triangles[index] = new(
                        Vertex(bytes.Slice(offset + 12, 12)),
                        Vertex(bytes.Slice(offset + 24, 12)),
                        Vertex(bytes.Slice(offset + 36, 12)));
                }

                return new MeshInput(triangles, 1, Matrix4x4.Identity);
            }
        }

        return ReadAscii(bytes, cancellationToken);
    }

    private static MeshInput ReadAscii(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        string content = new UTF8Encoding(false, true).GetString(bytes);
        using var reader = new StringReader(content);
        var triangles = new List<MeshTriangle>();
        var vertices = new List<Vector3>(3);
        var state = AsciiState.BeforeSolid;
        bool completedSolid = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length > 4096)
            {
                throw new FormatException("An ASCII STL line exceeds the analysis budget.");
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (state == AsciiState.BeforeSolid && IsKeyword(trimmed, "solid"))
            {
                state = AsciiState.InSolid;
            }
            else if (state == AsciiState.InSolid && IsKeyword(trimmed, "facet"))
            {
                string[] normal = Tokens(trimmed);
                if (normal.Length != 5 || !string.Equals(normal[1], "normal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException("An ASCII STL facet normal is malformed.");
                }

                _ = Parse(normal[2]);
                _ = Parse(normal[3]);
                _ = Parse(normal[4]);
                state = AsciiState.BeforeLoop;
            }
            else if (state == AsciiState.BeforeLoop && HasTokens(trimmed, "outer", "loop"))
            {
                state = AsciiState.InLoop;
            }
            else if (state == AsciiState.InLoop && IsKeyword(trimmed, "vertex"))
            {
                string[] coordinates = Tokens(trimmed);
                if (coordinates.Length != 4 || vertices.Count == 3)
                {
                    throw new FormatException("An ASCII STL vertex is malformed.");
                }

                vertices.Add(new(Parse(coordinates[1]), Parse(coordinates[2]), Parse(coordinates[3])));
            }
            else if (state == AsciiState.InLoop && string.Equals(trimmed, "endloop", StringComparison.OrdinalIgnoreCase)
                && vertices.Count == 3)
            {
                state = AsciiState.AfterLoop;
            }
            else if (state == AsciiState.AfterLoop && string.Equals(trimmed, "endfacet", StringComparison.OrdinalIgnoreCase))
            {
                if (triangles.Count == MaximumTriangles)
                {
                    throw new FormatException("The STL triangle budget was exceeded.");
                }

                triangles.Add(new(vertices[0], vertices[1], vertices[2]));
                vertices.Clear();
                state = AsciiState.InSolid;
            }
            else if (state == AsciiState.InSolid && IsKeyword(trimmed, "endsolid"))
            {
                completedSolid = true;
                state = AsciiState.BeforeSolid;
            }
            else
            {
                throw new FormatException("The ASCII STL facet structure is malformed.");
            }
        }

        if (!completedSolid || state != AsciiState.BeforeSolid || triangles.Count == 0 || vertices.Count != 0)
        {
            throw new FormatException("The ASCII STL contains no complete solid.");
        }

        return new MeshInput(triangles, 1, Matrix4x4.Identity);
    }

    private static string[] Tokens(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsKeyword(string line, string keyword) =>
        line.Equals(keyword, StringComparison.OrdinalIgnoreCase)
        || (line.Length > keyword.Length
            && line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(line[keyword.Length]));

    private static bool HasTokens(string line, string first, string second)
    {
        string[] tokens = Tokens(line);
        return tokens.Length == 2
            && string.Equals(tokens[0], first, StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[1], second, StringComparison.OrdinalIgnoreCase);
    }

    private enum AsciiState
    {
        BeforeSolid,
        InSolid,
        BeforeLoop,
        InLoop,
        AfterLoop,
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
