// <copyright file="AdmittedThreeMfMeshReader.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing.Simulation;

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;

/// <summary>Reads bounded, package-local 3MF build occurrences from admitted bytes.</summary>
internal static class AdmittedThreeMfMeshReader
{
    private const int MaximumBytes = AdmittedStlMeshReader.MaximumBytes;
    private const int MaximumExpandedBytes = 16 * 1024 * 1024;
    private const int MaximumEntries = 128;
    private const int MaximumModels = 16;
    private const int MaximumObjects = 4096;
    private const int MaximumInstances = 256;
    private const int MaximumDepth = 32;
    private const int MaximumVertices = 150_000;
    private const int MaximumTriangles = AdmittedStlMeshReader.MaximumTriangles;
    private static readonly XNamespace Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private static readonly XNamespace Production = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ModelRelationship = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";

    internal static MeshInput Read(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.IsEmpty || bytes.Length > MaximumBytes) throw Invalid();
        ValidateDirectory(bytes);
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count is 0 or > MaximumEntries) throw Invalid();
            var parts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) continue;
                string path = Path(entry.FullName);
                if (entry.Length < 0 || entry.Length > MaximumBytes || parts.ContainsKey(path)) throw Invalid();
                using var input = entry.Open();
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    expanded += read;
                    if (expanded > MaximumExpandedBytes || output.Length + read > MaximumBytes) throw Invalid();
                    output.Write(buffer, 0, read);
                }
                if (output.Length != entry.Length) throw Invalid();
                parts.Add(path, output.ToArray());
            }

            XDocument Xml(string path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!parts.TryGetValue(path, out var content)) throw Invalid();
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumBytes,
                    MaxCharactersFromEntities = 0,
                };
                using (var validationStream = new MemoryStream(content, writable: false))
                using (var validation = XmlReader.Create(validationStream, settings))
                {
                    int nodes = 0;
                    while (validation.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (validation.Depth > 64 || ++nodes > 500_000) throw Invalid();
                    }
                }
                using var xmlStream = new MemoryStream(content, writable: false);
                using var reader = XmlReader.Create(xmlStream, settings);
                return XDocument.Load(reader);
            }

            HashSet<string> ModelTargets(string relsPath, string sourcePath)
            {
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!parts.ContainsKey(relsPath)) return targets;
                var root = Xml(relsPath).Root;
                if (root is null || root.Name != Relationships + "Relationships") throw Invalid();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var relation in root.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (relation.Name != Relationships + "Relationship"
                        || !ids.Add(Required(relation, "Id"))) throw Invalid();
                    if ((string?)relation.Attribute("Type") != ModelRelationship) continue;
                    if ((string?)relation.Attribute("TargetMode") is { } mode && mode != "Internal") throw Invalid();
                    string target = Path(Required(relation, "Target"), sourcePath, relationship: true);
                    if (!targets.Add(target) || !parts.ContainsKey(target)) throw Invalid();
                }
                return targets;
            }

            if (!parts.ContainsKey("/[Content_Types].xml")) throw Invalid();
            var types = Xml("/[Content_Types].xml").Root;
            XNamespace typesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
            if (types is null || types.Name != typesNamespace + "Types") throw Invalid();
            var roots = ModelTargets("/_rels/.rels", "/");
            if (roots.Count != 1) throw Invalid();
            string rootPath = roots.Single();
            string rootDirectory = rootPath[..(rootPath.LastIndexOf('/') + 1)];
            string rootFile = rootPath[(rootPath.LastIndexOf('/') + 1)..];
            var related = ModelTargets(rootDirectory + "_rels/" + rootFile + ".rels", rootPath);
            related.Add(rootPath);
            var models = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
            int objects = 0, vertices = 0, triangles = 0;
            foreach (string path in parts.Keys.Where(p => p.EndsWith(".model", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (models.Count == MaximumModels) throw Invalid();
                bool hasType = types.Elements(typesNamespace + "Default").Any(e =>
                    string.Equals((string?)e.Attribute("Extension"), "model", StringComparison.OrdinalIgnoreCase)
                    && (string?)e.Attribute("ContentType") == "application/vnd.ms-package.3dmanufacturing-3dmodel+xml")
                    || types.Elements(typesNamespace + "Override").Any(e =>
                        string.Equals(Path(Required(e, "PartName")), path, StringComparison.OrdinalIgnoreCase)
                        && (string?)e.Attribute("ContentType") == "application/vnd.ms-package.3dmanufacturing-3dmodel+xml");
                if (!hasType) throw Invalid();
                var root = Xml(path).Root;
                if (root is null || root.Name != Core + "model") throw Invalid();
                foreach (string prefix in ((string?)root.Attribute("requiredextensions") ?? "")
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var ns = root.GetNamespaceOfPrefix(prefix);
                    if (ns != Core && ns != Production) throw Invalid();
                }
                double scale = (string?)root.Attribute("unit") switch
                {
                    null or "millimeter" => 1,
                    "micron" => 0.001,
                    "centimeter" => 10,
                    "inch" => 25.4,
                    "foot" => 304.8,
                    "meter" => 1000,
                    _ => throw Invalid(),
                };
                var resources = Single(root, "resources");
                var model = new Model(scale, new Dictionary<int, ObjectData>(), Single(root, "build"));
                foreach (var item in resources.Elements(Core + "object"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string type = (string?)item.Attribute("type") ?? "model";
                    if (++objects > MaximumObjects || type is not ("model" or "other" or "support" or "solidsupport" or "surface")) throw Invalid();
                    int id = Id(Required(item, "id"));
                    var meshes = item.Elements(Core + "mesh").ToArray();
                    var components = item.Elements(Core + "components").ToArray();
                    if (meshes.Length + components.Length != 1) throw Invalid();
                    if (item.Elements().Any(e => e.Name.Namespace == Core && e.Name.LocalName is not ("mesh" or "components" or "metadata" or "metadatagroup"))) throw Invalid();
                    var geometry = new List<MeshTriangle>();
                    var references = new List<Reference>();
                    if (meshes.Length == 1)
                    {
                        var points = new List<Vector3>();
                        foreach (var point in Single(meshes[0], "vertices").Elements())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (point.Name != Core + "vertex" || ++vertices > MaximumVertices) throw Invalid();
                            points.Add(new(Number(Required(point, "x")), Number(Required(point, "y")), Number(Required(point, "z"))));
                        }
                        foreach (var triangle in Single(meshes[0], "triangles").Elements())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (triangle.Name != Core + "triangle" || ++triangles > MaximumTriangles) throw Invalid();
                            int a = Index(Required(triangle, "v1"), points.Count);
                            int b = Index(Required(triangle, "v2"), points.Count);
                            int c = Index(Required(triangle, "v3"), points.Count);
                            if (a == b || b == c || a == c) throw Invalid();
                            geometry.Add(new(points[a], points[b], points[c]));
                        }
                        if (geometry.Count == 0) throw Invalid();
                    }
                    else
                    {
                        foreach (var component in components[0].Elements())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (component.Name != Core + "component" || references.Count == MaximumObjects) throw Invalid();
                            if (!string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase) && component.Attribute(Production + "path") is not null) throw Invalid();
                            references.Add(ParseReference(component));
                        }
                        if (references.Count == 0) throw Invalid();
                    }
                    if (!model.Objects.TryAdd(id, new ObjectData(type, geometry, references))) throw Invalid();
                }
                models.Add(path, model);
            }
            if (!models.TryGetValue(rootPath, out var rootModel)) throw Invalid();
            var outputTriangles = new List<MeshTriangle>();
            var groups = new List<int>();
            var stack = new HashSet<(string Path, int Id)>();
            int instances = 0;
            void Expand(string path, int id, Matrix4x4 transform, int depth)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (depth > MaximumDepth || !models.TryGetValue(path, out var model)
                    || !model.Objects.TryGetValue(id, out var obj) || !stack.Add((path.ToUpperInvariant(), id))) throw Invalid();
                try
                {
                    if (obj.Type != "model") throw Invalid();
                    if (obj.Triangles.Count > 0)
                    {
                        if (++instances > MaximumInstances || outputTriangles.Count + obj.Triangles.Count > MaximumTriangles) throw Invalid();
                        var units = Matrix4x4.CreateScale((float)model.Scale);
                        Matrix4x4 total = units * transform;
                        float determinant = total.GetDeterminant();
                        if (!float.IsFinite(determinant) || determinant == 0) throw Invalid();
                        foreach (var triangle in obj.Triangles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Vector3 a = Transform(triangle.A, total), b = Transform(triangle.B, total), c = Transform(triangle.C, total);
                            outputTriangles.Add(determinant < 0 ? new(a, c, b) : new(a, b, c));
                            groups.Add(instances - 1);
                        }
                    }
                    else
                    {
                        foreach (var reference in obj.References)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string target = Resolve(path, reference);
                            Expand(target, reference.Id, MillimetreTransform(reference.Transform, model.Scale) * transform, depth + 1);
                        }
                    }
                }
                finally { stack.Remove((path.ToUpperInvariant(), id)); }
            }
            string Resolve(string current, Reference reference)
            {
                if (reference.Path is { } explicitPath)
                {
                    if (!explicitPath.StartsWith('/')) throw Invalid();
                    string target = Path(explicitPath);
                    if (!related.Contains(target) || !models.TryGetValue(target, out var targetModel)
                        || !targetModel.Objects.ContainsKey(reference.Id)) throw Invalid();
                    return target;
                }
                if (models[current].Objects.ContainsKey(reference.Id)) return current;
                // Legacy browser packages sometimes omit production paths; never guess an ambiguous ID.
                var candidates = models.Where(m => related.Contains(m.Key) && m.Value.Objects.ContainsKey(reference.Id)).Select(m => m.Key).Take(2).ToArray();
                return candidates.Length == 1 ? candidates[0] : throw Invalid();
            }
            foreach (var item in rootModel.Build.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Name == Core + "metadatagroup") continue;
                if (item.Name != Core + "item") throw Invalid();
                string? printable = (string?)item.Attribute("printable");
                if (printable is not (null or "true" or "1" or "false" or "0")) throw Invalid();
                if (printable is "0" or "false") continue;
                var reference = ParseReference(item);
                string path = Resolve(rootPath, reference);
                Expand(path, reference.Id, MillimetreTransform(reference.Transform, rootModel.Scale), 0);
            }
            if (outputTriangles.Count == 0) throw Invalid();
            cancellationToken.ThrowIfCancellationRequested();
            return new MeshInput(outputTriangles, 1, Matrix4x4.Identity) { PositiveFillGroups = groups.ToArray() };
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or OverflowException or ArgumentException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Invalid();
        }
    }

    internal static void ValidateDirectory(ReadOnlySpan<byte> bytes)
    {
        // Bound central-directory allocation before ZipArchive materializes entry objects.
        for (int end = bytes.Length - 22; end >= Math.Max(0, bytes.Length - 65_557); end--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[end..]) != 0x06054b50) continue;
            ushort comment = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(end + 20)..]);
            if (end + 22 + comment != bytes.Length) throw Invalid();
            ushort count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(end + 10)..]);
            if (count is 0 or > MaximumEntries
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes[(end + 4)..]) != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(bytes[(end + 8)..]) != count) throw Invalid();
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(end + 16)..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(end + 12)..]);
            if ((long)offset + length != end) throw Invalid();
            int cursor = checked((int)offset);
            int observed = 0;
            while (cursor < end)
            {
                if (++observed > MaximumEntries || end - cursor < 46
                    || BinaryPrimitives.ReadUInt32LittleEndian(bytes[cursor..]) != 0x02014b50) throw Invalid();
                int size = 46 + BinaryPrimitives.ReadUInt16LittleEndian(bytes[(cursor + 28)..])
                    + BinaryPrimitives.ReadUInt16LittleEndian(bytes[(cursor + 30)..])
                    + BinaryPrimitives.ReadUInt16LittleEndian(bytes[(cursor + 32)..]);
                if (size > end - cursor) throw Invalid();
                cursor += size;
            }
            if (observed != count) throw Invalid();
            return;
        }
        throw Invalid();
    }

    private static Reference ParseReference(XElement element) => new(
        Id(Required(element, "objectid")), (string?)element.Attribute(Production + "path"),
        Matrix((string?)element.Attribute("transform")));

    private static Matrix4x4 MillimetreTransform(Matrix4x4 matrix, double scale)
    {
        matrix.M41 *= (float)scale;
        matrix.M42 *= (float)scale;
        matrix.M43 *= (float)scale;
        return matrix;
    }

    private static Matrix4x4 Matrix(string? value)
    {
        if (value is null) return Matrix4x4.Identity;
        string[] numbers = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length != 12) throw Invalid();
        float[] m = numbers.Select(Number).ToArray();
        var result = new Matrix4x4(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0,
            m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);
        if (!float.IsFinite(result.GetDeterminant()) || result.GetDeterminant() == 0) throw Invalid();
        return result;
    }

    private static string Path(string value, string source = "/", bool relationship = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024
            || value.IndexOfAny(['\\', ':', '?', '#', '\0']) >= 0) throw Invalid();
        string decoded = Uri.UnescapeDataString(value);
        if (decoded.IndexOfAny(['\\', ':', '?', '#', '\0', '%']) >= 0
            || decoded.StartsWith("//", StringComparison.Ordinal)
            || decoded.Count(c => c == '/') != value.Count(c => c == '/')) throw Invalid();
        var segments = new List<string>();
        if (!decoded.StartsWith('/') && relationship)
            segments.AddRange(source.Split('/').Skip(1).SkipLast(1));
        foreach (string part in decoded.TrimStart('/').Split('/'))
        {
            if (part is "" or ".") throw Invalid();
            if (part == "..")
            {
                if (!relationship || segments.Count == 0) throw Invalid();
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(part);
        }
        return "/" + string.Join('/', segments);
    }

    private static XElement Single(XElement parent, string name)
    {
        var elements = parent.Elements(Core + name).Take(2).ToArray();
        return elements.Length == 1 ? elements[0] : throw Invalid();
    }
    private static string Required(XElement element, XName name) =>
        (string?)element.Attribute(name) is { Length: > 0 } value ? value : throw Invalid();
    private static float Number(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) && float.IsFinite(result)
            ? result : throw Invalid();
    private static int Id(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) && result > 0 ? result : throw Invalid();
    private static int Index(string value, int count) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) && result >= 0 && result < count
            ? result : throw Invalid();
    private static Vector3 Transform(Vector3 point, Matrix4x4 matrix)
    {
        var result = Vector3.Transform(point, matrix);
        return float.IsFinite(result.X) && float.IsFinite(result.Y) && float.IsFinite(result.Z) ? result : throw Invalid();
    }
    private static FormatException Invalid() => new("The 3MF package is invalid, unsupported, or exceeds the physical analysis budget.");
    private sealed record Reference(int Id, string? Path, Matrix4x4 Transform);
    private sealed record ObjectData(string Type, IReadOnlyList<MeshTriangle> Triangles, IReadOnlyList<Reference> References);
    private sealed record Model(double Scale, Dictionary<int, ObjectData> Objects, XElement Build);
}
