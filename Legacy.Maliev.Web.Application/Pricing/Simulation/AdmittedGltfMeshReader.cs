// <copyright file="AdmittedGltfMeshReader.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

/// <summary>Decodes self-contained admitted glTF2/GLB bytes; never resolves customer URIs.</summary>
internal static class AdmittedGltfMeshReader
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal const int MaximumTriangles = 50_000;
    private const int MaximumInstances = 256;
    private const int MaximumNodes = 4096;
    private const int MaximumExpandedVertices = 150_000;
    private const int MaximumDecodedComponents = 2_000_000;

    internal static MeshInput Read(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.IsEmpty || bytes.Length > MaximumBytes) throw Invalid();
        try
        {
            byte[] json = bytes.ToArray();
            byte[]? bin = null;
            if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x46546c67)
            {
                (json, bin) = ReadGlb(bytes, cancellationToken);
            }

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            ValidateJson(document.RootElement, cancellationToken, refCount: new int[1]);
            return new Reader(document.RootElement, bin, cancellationToken).Read();
        }
        catch (Exception exception) when (exception is JsonException or OverflowException or ArgumentException or InvalidOperationException)
        {
            throw new FormatException("The admitted glTF input is invalid or exceeds its analysis budget.", exception);
        }
    }

    private static (byte[] Json, byte[]? Bin) ReadGlb(ReadOnlySpan<byte> bytes, CancellationToken token)
    {
        if (bytes.Length < 20 || bytes.Length % 4 != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 2
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != bytes.Length) throw Invalid();
        byte[]? json = null, bin = null;
        int index = 0;
        for (int position = 12; position < bytes.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (index == 1024) throw Invalid();
            Range(position, 8, bytes.Length);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..]);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(position + 4)..]);
            position += 8;
            if (length > int.MaxValue || length % 4 != 0) throw Invalid();
            Range(position, (int)length, bytes.Length);
            if (index == 0 && type != 0x4e4f534a) throw Invalid();
            if (type == 0x4e4f534a)
            {
                if (json is not null || index != 0 || length == 0) throw Invalid();
                json = bytes.Slice(position, (int)length).ToArray();
            }
            else if (type == 0x004e4942)
            {
                if (bin is not null || index != 1) throw Invalid();
                bin = bytes.Slice(position, (int)length).ToArray();
            }

            position += (int)length;
        }

        return (json ?? throw Invalid(), bin);
    }

    private static void ValidateJson(JsonElement element, CancellationToken token, int[] refCount)
    {
        token.ThrowIfCancellationRequested();
        if (++refCount[0] > 200_000) throw Invalid();
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                ValidateJson(property.Value, token, refCount);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray()) ValidateJson(value, token, refCount);
        }
    }

    private sealed class Reader
    {
        private readonly JsonElement root;
        private readonly CancellationToken token;
        private readonly JsonElement[] nodes, meshes, views, accessors, skins;
        private readonly byte[][] buffers;
        private readonly Accessor?[] decoded;
        private readonly int[] parents;
        private readonly Matrix4x4[] worlds;
        private readonly byte[] worldState;
        private readonly HashSet<int> selectedNodes = [];
        private readonly List<MeshTriangle> triangles = [];
        private readonly List<int> groups = [];
        private readonly bool quantization;
        private int decodedComponents, decodedBytes, instances, expandedVertices, candidateTriangles;

        internal Reader(JsonElement root, byte[]? bin, CancellationToken token)
        {
            this.root = root;
            this.token = token;
            if (root.ValueKind != JsonValueKind.Object || Text(Required(root, "asset"), "version") != "2.0") throw Invalid();
            if (Required(root, "asset").TryGetProperty("minVersion", out var minimum) && minimum.GetString() != "2.0") throw Invalid();
            foreach (var extension in Array(root, "extensionsRequired", 64))
            {
                if (extension.ValueKind != JsonValueKind.String || !SupportedExtension(extension.GetString())) throw Invalid();
            }

            quantization = Array(root, "extensionsUsed", 64).Any(value => value.GetString() == "KHR_mesh_quantization")
                || Array(root, "extensionsRequired", 64).Any(value => value.GetString() == "KHR_mesh_quantization");
            nodes = Array(root, "nodes", MaximumNodes);
            meshes = Array(root, "meshes", 1024);
            views = Array(root, "bufferViews", 4096);
            accessors = Array(root, "accessors", 4096);
            skins = Array(root, "skins", 256);
            decoded = new Accessor?[accessors.Length];
            var definitions = Array(root, "buffers", 64);
            buffers = new byte[definitions.Length][];
            for (int i = 0; i < buffers.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int length = Int(definitions[i], "byteLength", required: true);
                if (length <= 0 || length > MaximumBytes - decodedBytes) throw Invalid();
                decodedBytes += length;
                if (definitions[i].TryGetProperty("uri", out var uri))
                {
                    string text = uri.GetString() ?? throw Invalid();
                    int comma = text.IndexOf(',');
                    if (comma < 0 || !(text[..comma].Equals("data:application/octet-stream;base64", StringComparison.OrdinalIgnoreCase)
                        || text[..comma].Equals("data:application/gltf-buffer;base64", StringComparison.OrdinalIgnoreCase))) throw Invalid();
                    string payload = Uri.UnescapeDataString(text[(comma + 1)..]);
                    if ((long)payload.Length > ((long)length + 2) / 3 * 4) throw Invalid();
                    byte[] content = Convert.FromBase64String(payload);
                    if (content.Length != length) throw Invalid();
                    buffers[i] = content;
                }
                else
                {
                    if (i != 0 || bin is null || bin.Length < length || bin.Length - length > 3) throw Invalid();
                    for (int pad = length; pad < bin.Length; pad++) if (bin[pad] != 0) throw Invalid();
                    buffers[i] = bin.AsSpan(0, length).ToArray();
                }
            }

            if (bin is not null && (definitions.Length == 0 || definitions[0].TryGetProperty("uri", out _))) throw Invalid();
            parents = Enumerable.Repeat(-1, nodes.Length).ToArray();
            worlds = new Matrix4x4[nodes.Length];
            worldState = new byte[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                foreach (var child in Array(nodes[i], "children", MaximumNodes))
                {
                    int childId = Index(child, nodes.Length);
                    if (parents[childId] != -1) throw Invalid();
                    parents[childId] = i;
                }
            }

            for (int i = 0; i < nodes.Length; i++) _ = World(i, 0);
        }

        internal MeshInput Read()
        {
            JsonElement[] scenes = Array(root, "scenes", 256);
            int scene = Int(root, "scene");
            if (scene >= scenes.Length) throw Invalid();
            JsonElement[] roots = Array(scenes[scene], "nodes", MaximumNodes);
            foreach (var value in roots)
            {
                int id = Index(value, nodes.Length);
                if (parents[id] != -1) throw Invalid();
                Select(id, 0);
            }

            foreach (var value in roots) Expand(Index(value, nodes.Length), 0);
            if (triangles.Count == 0) throw Invalid();
            return new MeshInput(triangles.ToArray(), 1, Matrix4x4.Identity)
            {
                PositiveFillGroups = groups.ToArray(),
            };
        }

        private void Select(int id, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 64 || !selectedNodes.Add(id)) throw Invalid();
            foreach (var child in Array(nodes[id], "children", MaximumNodes)) Select(Index(child, nodes.Length), depth + 1);
        }

        private void Expand(int id, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 64) throw Invalid();
            JsonElement node = nodes[id];
            // The viewer renders every GPU instance; this reader cannot safely quote only its base mesh.
            if (node.TryGetProperty("extensions", out var nodeExtensions)
                && nodeExtensions.TryGetProperty("EXT_mesh_gpu_instancing", out _)) throw Invalid();

            if (node.TryGetProperty("mesh", out var meshId))
            {
                if (instances == MaximumInstances) throw Invalid();
                int group = instances++;
                JsonElement mesh = meshes[Index(meshId, meshes.Length)];
                foreach (var primitive in Array(mesh, "primitives", 1024)) Primitive(node, mesh, primitive, worlds[id], group);
            }

            foreach (var child in Array(node, "children", MaximumNodes)) Expand(Index(child, nodes.Length), depth + 1);
        }

        private void Primitive(JsonElement node, JsonElement mesh, JsonElement primitive, Matrix4x4 world, int group)
        {
            token.ThrowIfCancellationRequested();
            if (primitive.TryGetProperty("extensions", out var extensions)
                && extensions.TryGetProperty("KHR_draco_mesh_compression", out _)) throw Invalid();
            int mode = Int(primitive, "mode", fallback: 4);
            if (mode is >= 0 and <= 3) return;
            if (mode is < 4 or > 6) throw Invalid();
            JsonElement attributes = Required(primitive, "attributes");
            int positionId = Index(Required(attributes, "POSITION"), accessors.Length);
            int indexCount = primitive.TryGetProperty("indices", out var declaredIndexId)
                ? Int(accessors[Index(declaredIndexId, accessors.Length)], "count", required: true)
                : Int(accessors[positionId], "count", required: true);
            if (indexCount < 3 || mode == 4 && indexCount % 3 != 0) throw Invalid();
            int candidates = mode == 4 ? indexCount / 3 : indexCount - 2;
            // Prepay all primitive occurrences, including degenerate and invalid/restart indices.
            // Cached accessors must not turn an output-only triangle cap into unbounded repeated work.
            if (candidates > MaximumTriangles - candidateTriangles) throw Invalid();
            candidateTriangles += candidates;
            Accessor position = Get(positionId);
            Position(position);
            if (position.Count > MaximumExpandedVertices - expandedVertices) throw Invalid();
            expandedVertices += position.Count;
            var vertices = new Vector3[position.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                vertices[i] = new(Float(position.Values[i * 3]), Float(position.Values[i * 3 + 1]), Float(position.Values[i * 3 + 2]));
            }

            JsonElement[] targets = Array(primitive, "targets", 64);
            double[] weights = node.TryGetProperty("weights", out var nodeWeights) ? Numbers(nodeWeights, targets.Length)
                : mesh.TryGetProperty("weights", out var meshWeights) ? Numbers(meshWeights, targets.Length) : new double[targets.Length];
            for (int target = 0; target < targets.Length; target++)
            {
                if (!targets[target].TryGetProperty("POSITION", out var targetId)) continue;
                Accessor morph = Get(Index(targetId, accessors.Length));
                Position(morph);
                if (morph.Count != position.Count) throw Invalid();
                for (int i = 0; i < vertices.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    vertices[i] += new Vector3(Float(morph.Values[i * 3] * weights[target]),
                        Float(morph.Values[i * 3 + 1] * weights[target]), Float(morph.Values[i * 3 + 2] * weights[target]));
                    Finite(vertices[i]);
                }
            }

            int[] orientation = new int[vertices.Length];
            if (node.TryGetProperty("skin", out var skinId))
            {
                Skin(vertices, orientation, attributes, skins[Index(skinId, skins.Length)]);
            }
            else
            {
                int sign = Sign(world);
                for (int i = 0; i < vertices.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    vertices[i] = Vector3.Transform(vertices[i], world);
                    orientation[i] = sign;
                }
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Finite(vertices[i]);
                // Source printing convention is millimetres, Y-up to Z-up; not glTF's generic metre convention.
                vertices[i] = new(vertices[i].X, -vertices[i].Z, vertices[i].Y);
            }

            int[] indices;
            if (primitive.TryGetProperty("indices", out var indexId))
            {
                Accessor index = Get(Index(indexId, accessors.Length));
                if (index.Size != 1 || index.Type is not (5121 or 5123 or 5125) || index.Normalized) throw Invalid();
                indices = new int[index.Count];
                for (int i = 0; i < indices.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    double value = index.Values[i];
                    if (value < 0 || value >= vertices.Length || value != Math.Truncate(value)) throw Invalid();
                    indices[i] = (int)value;
                }
            }
            else indices = Enumerable.Range(0, vertices.Length).ToArray();

            if (indices.Length < 3 || mode == 4 && indices.Length % 3 != 0) throw Invalid();
            for (int i = 0; mode == 4 ? i < indices.Length : i + 2 < indices.Length; i += mode == 4 ? 3 : 1)
            {
                token.ThrowIfCancellationRequested();
                int a = mode == 6 ? indices[0] : indices[i];
                int b = indices[i + 1], c = indices[i + 2];
                if (mode == 5 && i % 2 != 0) (a, b) = (b, a);
                if (a == b || b == c || a == c) continue;
                if (orientation[a] != orientation[b] || orientation[a] != orientation[c]) throw Invalid();
                if (orientation[a] < 0) (b, c) = (c, b);
                if (triangles.Count == MaximumTriangles) throw Invalid();
                triangles.Add(new(vertices[a], vertices[b], vertices[c]));
                groups.Add(group);
            }
        }

        private void Skin(Vector3[] vertices, int[] orientation, JsonElement attributes, JsonElement skin)
        {
            JsonElement[] jointIds = Array(skin, "joints", 256);
            if (jointIds.Length == 0) throw Invalid();
            var jointMatrices = new Matrix4x4[jointIds.Length];
            Accessor? inverse = skin.TryGetProperty("inverseBindMatrices", out var inverseId) ? Get(Index(inverseId, accessors.Length)) : null;
            if (inverse is not null && (inverse.Size != 16 || inverse.Type != 5126 || inverse.Normalized || inverse.Count < jointIds.Length)) throw Invalid();
            var unique = new HashSet<int>();
            for (int i = 0; i < jointIds.Length; i++)
            {
                int id = Index(jointIds[i], nodes.Length);
                if (!selectedNodes.Contains(id) || !unique.Add(id)) throw Invalid();
                jointMatrices[i] = (inverse is null ? Matrix4x4.Identity : Matrix(inverse.Values.AsSpan(i * 16, 16))) * worlds[id];
            }

            var sets = new List<(Accessor Joints, Accessor Weights)>();
            for (int set = 0; set < 4; set++)
            {
                bool hasJoints = attributes.TryGetProperty("JOINTS_" + set, out var js);
                bool hasWeights = attributes.TryGetProperty("WEIGHTS_" + set, out var ws);
                if (hasJoints != hasWeights) throw Invalid();
                if (!hasJoints) break;
                Accessor joints = Get(Index(js, accessors.Length)), weights = Get(Index(ws, accessors.Length));
                if (joints.Count != vertices.Length || weights.Count != vertices.Length || joints.Size != 4 || weights.Size != 4
                    || joints.Type is not (5121 or 5123) || joints.Normalized
                    || !(weights.Type == 5126 && !weights.Normalized || weights.Type is 5121 or 5123 && weights.Normalized)) throw Invalid();
                sets.Add((joints, weights));
            }

            if (sets.Count == 0) throw Invalid();
            foreach (var property in attributes.EnumerateObject())
                if ((property.Name.StartsWith("JOINTS_", StringComparison.Ordinal) || property.Name.StartsWith("WEIGHTS_", StringComparison.Ordinal))
                    && !Enumerable.Range(0, sets.Count).Any(i => property.Name == "JOINTS_" + i || property.Name == "WEIGHTS_" + i)) throw Invalid();

            for (int i = 0; i < vertices.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                Matrix4x4 blended = default;
                double total = 0;
                foreach (var set in sets)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        double weight = set.Weights.Values[i * 4 + j], joint = set.Joints.Values[i * 4 + j];
                        if (weight < 0 || joint < 0 || joint >= jointMatrices.Length || joint != Math.Truncate(joint)) throw Invalid();
                        total += weight;
                        blended += jointMatrices[(int)joint] * Float(weight);
                    }
                }

                if (!double.IsFinite(total) || total <= 0 || Math.Abs(total - 1) > 0.02) throw Invalid();
                blended *= Float(1 / total);
                orientation[i] = Sign(blended);
                vertices[i] = Vector3.Transform(vertices[i], blended);
            }
        }

        private Matrix4x4 World(int id, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 64 || worldState[id] == 1) throw Invalid();
            if (worldState[id] == 2) return worlds[id];
            worldState[id] = 1;
            JsonElement node = nodes[id];
            Matrix4x4 local;
            if (node.TryGetProperty("matrix", out var matrix))
            {
                if (node.TryGetProperty("translation", out _) || node.TryGetProperty("rotation", out _) || node.TryGetProperty("scale", out _)) throw Invalid();
                local = Matrix(Numbers(matrix, 16));
            }
            else
            {
                double[] t = node.TryGetProperty("translation", out var translation) ? Numbers(translation, 3) : [0, 0, 0];
                double[] s = node.TryGetProperty("scale", out var scale) ? Numbers(scale, 3) : [1, 1, 1];
                double[] r = node.TryGetProperty("rotation", out var rotation) ? Numbers(rotation, 4) : [0, 0, 0, 1];
                var quaternion = new Quaternion(Float(r[0]), Float(r[1]), Float(r[2]), Float(r[3]));
                if (Math.Abs(quaternion.LengthSquared() - 1) > 0.0001) throw Invalid();
                local = Matrix4x4.CreateScale(Float(s[0]), Float(s[1]), Float(s[2]))
                    * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(quaternion))
                    * Matrix4x4.CreateTranslation(Float(t[0]), Float(t[1]), Float(t[2]));
            }

            worlds[id] = parents[id] < 0 ? local : local * World(parents[id], depth + 1);
            _ = Sign(worlds[id]);
            worldState[id] = 2;
            return worlds[id];
        }

        private Accessor Get(int id)
        {
            token.ThrowIfCancellationRequested();
            if (decoded[id] is { } cached) return cached;
            JsonElement definition = accessors[id];
            int count = Int(definition, "count", required: true), type = Int(definition, "componentType", required: true);
            int width = type switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw Invalid() };
            string shape = Text(definition, "type");
            int rows = shape switch { "SCALAR" => 1, "VEC2" or "MAT2" => 2, "VEC3" or "MAT3" => 3, "VEC4" or "MAT4" => 4, _ => throw Invalid() };
            int columns = shape.StartsWith("MAT", StringComparison.Ordinal) ? rows : 1, size = rows * columns;
            if (count <= 0 || (long)count * size > MaximumDecodedComponents - decodedComponents) throw Invalid();
            decodedComponents += count * size;
            int columnBytes = columns == 1 ? rows * width : (rows * width + 3) / 4 * 4;
            int elementBytes = columnBytes * columns;
            bool normalized = definition.TryGetProperty("normalized", out var normal) && normal.GetBoolean();
            if (normalized && type is not (5120 or 5121 or 5122 or 5123)) throw Invalid();
            var values = new double[count * size];
            if (definition.TryGetProperty("bufferView", out var viewId))
            {
                View view = GetView(Index(viewId, views.Length));
                int offset = Int(definition, "byteOffset"), stride = view.Stride == 0 ? elementBytes : view.Stride;
                if (stride < elementBytes || offset % width != 0 || (view.Offset + offset) % width != 0) throw Invalid();
                Range(offset, checked((count - 1) * stride + elementBytes), view.Length);
                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int c = 0; c < size; c++)
                        values[i * size + c] = Component(view.Bytes, view.Offset + offset + i * stride + c / rows * columnBytes + c % rows * width, type, normalized);
                }
            }
            else if (Int(definition, "byteOffset") != 0) throw Invalid();

            if (definition.TryGetProperty("sparse", out var sparse))
            {
                int sparseCount = Int(sparse, "count", required: true);
                if (sparseCount <= 0 || sparseCount > count) throw Invalid();
                JsonElement si = Required(sparse, "indices"), sv = Required(sparse, "values");
                int indexType = Int(si, "componentType", required: true);
                int indexWidth = indexType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw Invalid() };
                View indices = GetView(Index(Required(si, "bufferView"), views.Length));
                View data = GetView(Index(Required(sv, "bufferView"), views.Length));
                int io = Int(si, "byteOffset"), vo = Int(sv, "byteOffset");
                if (indices.Stride != 0 || data.Stride != 0 || (indices.Offset + io) % indexWidth != 0 || (data.Offset + vo) % width != 0) throw Invalid();
                Range(io, checked(sparseCount * indexWidth), indices.Length);
                Range(vo, checked(sparseCount * elementBytes), data.Length);
                int previous = -1;
                for (int i = 0; i < sparseCount; i++)
                {
                    token.ThrowIfCancellationRequested();
                    double raw = Component(indices.Bytes, indices.Offset + io + i * indexWidth, indexType, false);
                    if (raw <= previous || raw >= count) throw Invalid();
                    int target = (int)raw;
                    previous = target;
                    for (int c = 0; c < size; c++)
                        values[target * size + c] = Component(data.Bytes, data.Offset + vo + i * elementBytes + c / rows * columnBytes + c % rows * width, type, normalized);
                }
            }

            var result = new Accessor(values, count, size, type, normalized);
            decoded[id] = result;
            return result;
        }

        private View GetView(int id)
        {
            JsonElement definition = views[id];
            byte[] bytes = buffers[Index(Required(definition, "buffer"), buffers.Length)];
            int offset = Int(definition, "byteOffset"), length = Int(definition, "byteLength", required: true), stride = Int(definition, "byteStride");
            if (length <= 0 || stride != 0 && (stride < 4 || stride > 252 || stride % 4 != 0)) throw Invalid();
            Range(offset, length, bytes.Length);
            return new View(bytes, offset, length, stride);
        }

        private void Position(Accessor value)
        {
            if (value.Size != 3 || !(value.Type == 5126 && !value.Normalized
                || quantization && value.Type is 5120 or 5121 or 5122 or 5123)) throw Invalid();
        }
    }

    private sealed record Accessor(double[] Values, int Count, int Size, int Type, bool Normalized);
    private sealed record View(byte[] Bytes, int Offset, int Length, int Stride);

    private static double Component(byte[] bytes, int offset, int type, bool normalized)
    {
        ReadOnlySpan<byte> data = bytes.AsSpan(offset);
        double value = type switch
        {
            5120 => unchecked((sbyte)data[0]),
            5121 => data[0],
            5122 => BinaryPrimitives.ReadInt16LittleEndian(data),
            5123 => BinaryPrimitives.ReadUInt16LittleEndian(data),
            5125 => BinaryPrimitives.ReadUInt32LittleEndian(data),
            5126 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)),
            _ => throw Invalid(),
        };
        if (!double.IsFinite(value)) throw Invalid();
        return !normalized ? value : type switch
        {
            5120 => Math.Max(value / 127, -1),
            5121 => value / 255,
            5122 => Math.Max(value / 32767, -1),
            5123 => value / 65535,
            _ => throw Invalid(),
        };
    }

    private static Matrix4x4 Matrix(ReadOnlySpan<double> values)
    {
        if (values.Length != 16 || values[3] != 0 || values[7] != 0 || values[11] != 0 || values[15] != 1) throw Invalid();
        return new Matrix4x4(Float(values[0]), Float(values[1]), Float(values[2]), Float(values[3]),
            Float(values[4]), Float(values[5]), Float(values[6]), Float(values[7]),
            Float(values[8]), Float(values[9]), Float(values[10]), Float(values[11]),
            Float(values[12]), Float(values[13]), Float(values[14]), Float(values[15]));
    }

    private static int Sign(Matrix4x4 matrix)
    {
        float determinant = matrix.GetDeterminant();
        if (!float.IsFinite(determinant) || determinant == 0) throw Invalid();
        return determinant < 0 ? -1 : 1;
    }

    private static float Float(double value)
    {
        float result = (float)value;
        if (!float.IsFinite(result)) throw Invalid();
        return result;
    }

    private static void Finite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) throw Invalid();
    }

    private static double[] Numbers(JsonElement value, int count)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count) throw Invalid();
        var result = new double[count];
        int index = 0;
        foreach (var element in value.EnumerateArray())
        {
            if (!element.TryGetDouble(out double number) || !double.IsFinite(number)) throw Invalid();
            result[index++] = number;
        }

        return result;
    }

    private static JsonElement[] Array(JsonElement value, string name, int limit)
    {
        if (!value.TryGetProperty(name, out var array)) return [];
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > limit) throw Invalid();
        return array.EnumerateArray().ToArray();
    }

    private static JsonElement Required(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result : throw Invalid();

    private static string Text(JsonElement value, string name) => Required(value, name).GetString() ?? throw Invalid();

    private static int Int(JsonElement value, string name, bool required = false, int fallback = 0)
    {
        if (!value.TryGetProperty(name, out var element)) return required ? throw Invalid() : fallback;
        if (!element.TryGetInt32(out int result) || result < 0) throw Invalid();
        return result;
    }

    private static int Index(JsonElement value, int count) =>
        value.TryGetInt32(out int result) && result >= 0 && result < count ? result : throw Invalid();

    private static void Range(int offset, int length, int total)
    {
        if (offset < 0 || length < 0 || (long)offset + length > total) throw Invalid();
    }

    private static bool SupportedExtension(string? name) => name is
        "KHR_mesh_quantization" or "KHR_materials_unlit" or "KHR_materials_pbrSpecularGlossiness"
        or "KHR_materials_clearcoat" or "KHR_materials_transmission" or "KHR_texture_transform"
        or "KHR_texture_basisu" or "EXT_texture_webp" or "KHR_lights_punctual";

    private static FormatException Invalid() => new("The admitted glTF input is invalid, unsupported, or exceeds its analysis budget.");
}
