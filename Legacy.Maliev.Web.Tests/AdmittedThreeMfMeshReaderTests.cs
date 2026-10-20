using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class AdmittedThreeMfMeshReaderTests
{
    private const string Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private const string Production = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    internal const string OriginalFixture = "UEsDBBQAAAAIAK2W+1zt70jpyAAAAEgBAAATAAAAW0NvbnRlbnRfVHlwZXNdLnhtbIXQ3UrEMBCG4fNeRZhTaVNdEJG2e+DPFawXEJJpG0wmITNd4t3LdkVBEM8/3ge+4VhjUGcs7BONcNv1oJBscp6WEd5Or+0DHKdmOH1kZFVjIB5hFcmPWrNdMRruUkaqMcypRCPcpbLobOy7WVDf9f29tokESVq5NGBqlBqecTZbEPVSBelKFwwM6um6vXAjmJyDt0Z8In0m9wtqv5CuYNg3vPrMNzUGUPovJSaH4R8m/qQPLhraZmNlK56W9uD2wDcy6P2YqfkEUEsDBBQAAAAIAK2W+1yD6SxnaAEAAEsEAAAQAAAAM0QvM2Rtb2RlbC5tb2RlbI2TTW7CMBCF95xi5D04cQhUyAm7XqDlACEx4Cq2ke1EoaevYodCyE+7sWS/T29mnjV034gSaqYNVzJB4SpAwGSuCi7PCTp8vi/f0D5dUKEKVkIluU2Q4GXJBbNMI2hEuSuzlmVyefhwD9Ik6GLtdYexyS9MZGYleK6VUSe7ypXAUSEyWZ2y3FaayzPOlWaYBGGMA4LSBQDVzKhK58y0NwCqjl8st8CLBIUI7O3KEuRacrhDBDOX+wWA1kxb/mvw9MgaaBIUILi589udOH3S1v8Qo3F1KP5dnszWn1KjCXlEfYSCh6lQq3kmz+VLUvdXqEPnWZMEEQR15D4Ap6NA5AHyOnePXXs29uxmaNYBGw9sZ826wqFn48nOumrrWbOwN+ZIZ2Gvs3jWjPQicVOMAtunapNmrQvxw9ynGAXa6AaZUTz4Yoof20KxXy63d7i3ePRY8bJIKbdMgKe6DcQpxV5cUOwWMV38AFBLAwQUAAAACACtlvtcsUlUd7kAAAAJAQAACwAAAF9yZWxzLy5yZWxzZc/BasMwEATQu79C7L1aOYZQiuVcSiHX4H6AkNeyqKQVkhKcvy8tBBp6mdu8YcbTHoO4Uamek4ZeKhCULC8+OQ2f88fLK5ymbrxQMM1zqpvPVewxpKphay2/IVa7UTRVcqa0x7ByiaZVycVhNvbLOMKDUkcsfw2YOiGeWDGb4qhpwOEdhyXyQkH+JojzoqFQUCDme6Z/y9HbwpXXJi3Hn65J19XYdi0+OTyofkDVP0wQOHUjPj2aum9QSwECFAAUAAAACACtlvtc7e9I6cgAAABIAQAAEwAAAAAAAAAAAAAAAAAAAAAAW0NvbnRlbnRfVHlwZXNdLnhtbFBLAQIUABQAAAAIAK2W+1yD6SxnaAEAAEsEAAAQAAAAAAAAAAAAAAAAAPkAAAAzRC8zZG1vZGVsLm1vZGVsUEsBAhQAFAAAAAgArZb7XLFJVHe5AAAACQEAAAsAAAAAAAAAAAAAAAAAjwIAAF9yZWxzLy5yZWxzUEsFBgAAAAADAAMAuAAAAHEDAAAAAA==";

    [Fact]
    public void OriginalArchive_IsExactAndUsesFortyByThirtyByTwentyMillimetres()
    {
        byte[] bytes = Convert.FromBase64String(OriginalFixture);
        Assert.Equal(1087, bytes.Length);
        byte[] header = Encoding.ASCII.GetBytes("blob 1087\0");
        Assert.Equal("3768c2ca28fdbf1510393f07a77151264abe9a80",
            Convert.ToHexStringLower(SHA1.HashData([.. header, .. bytes])));
        var mesh = Normalize(AdmittedThreeMfMeshReader.Read(bytes));
        Assert.Equal(12, mesh.Triangles.Count);
        Assert.Empty(mesh.Diagnostics);
        Assert.Equal(new Vector3(40, 30, 20), Maximum(mesh));
        Assert.All(Layers(mesh), layer => Assert.Equal(1200, Area(layer), 3));
    }

    [Theory]
    [InlineData("millimeter", 1)]
    [InlineData("micron", 0.001)]
    [InlineData("centimeter", 10)]
    [InlineData("inch", 25.4)]
    [InlineData("foot", 304.8)]
    [InlineData("meter", 1000)]
    public void ModelUnits_ScaleVerticesAndTranslations(string unit, double scale)
    {
        var input = Read(Model(Cube(1), "<item objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 2 3 4\"/>", unit));
        Assert.Equal(12, input.Triangles.Count);
        Assert.Equal(2 * scale, input.Triangles.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), 3);
        Assert.Equal(5 * scale, input.Triangles.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z))), 3);
    }

    [Fact]
    public void OmittedUnit_DefaultsToMillimetresAndCrossModelTranslationsUseParentUnits()
    {
        var omitted = Read(Model(Cube(10), "<item objectid=\"1\"/>").Replace(" unit=\"millimeter\"", "", StringComparison.Ordinal));
        Assert.Equal(new Vector3(10), Maximum(Normalize(omitted)));
        byte[] bytes = Package(("/3D/root.model", Model("", "<item objectid=\"1\" p:path=\"/3D/a.model\" transform=\"1 0 0 0 1 0 0 0 1 2 0 0\"/>", "inch")),
            ("/3D/a.model", Model(Cube(1), "", "centimeter")));
        var input = AdmittedThreeMfMeshReader.Read(bytes);
        Assert.Equal(50.8, input.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Min(v => v.X), 3);
        Assert.Equal(60.8, input.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Max(v => v.X), 3);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(5, 150)]
    [InlineData(20, 200)]
    public void RepeatedBuildOccurrences_AreUnionedNotXored(int offset, double expectedArea)
    {
        var mesh = Normalize(Read(Model(Cube(10),
            $"<item objectid=\"1\"/><item objectid=\"1\" transform=\"1 0 0 0 1 0 0 0 1 {offset} 0 0\"/>")));
        Assert.Empty(mesh.Diagnostics);
        Assert.All(Layers(mesh), layer => Assert.Equal(expectedArea, Area(layer), 3));
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 84)]
    public void OneMesh_UsesPositiveWindingForNestedSolidOrHole(bool reverse, double area)
    {
        string geometry = Cube(10, inner: true, reverseInner: reverse);
        var mesh = Normalize(Read(Model(geometry, "<item objectid=\"1\"/>")));
        Assert.All(Layers(mesh), layer => Assert.Equal(area, Area(layer), 3));
    }

    [Fact]
    public void Components_NestAffineTransformsAndPreserveReflectionWinding()
    {
        string resources = Cube(10)
            + "<object id=\"2\"><components><component objectid=\"1\" transform=\"-1 0 0 0 1 0 0 0 1 10 0 0\"/></components></object>"
            + "<object id=\"3\"><components><component objectid=\"2\" transform=\"1 0 0 0.5 1 0 0 0 1 3 0 0\"/></components></object>";
        var mesh = Normalize(Read(Model(resources, "<item objectid=\"3\"/>")));
        Assert.Empty(mesh.Diagnostics);
        Assert.Equal(new Vector3(15, 10, 10), Maximum(mesh));
        Assert.All(Layers(mesh), layer => Assert.Equal(100, Area(layer), 3));
    }

    [Fact]
    public void CrossModelIds_ArePathScopedAndChildBuildIsIgnored()
    {
        var bytes = Package(("/3D/root.model", Model(
            "<object id=\"1\"><components><component objectid=\"1\" p:path=\"/3D/a.model\"/><component objectid=\"1\" p:path=\"/3D/b.model\" transform=\"1 0 0 0 1 0 0 0 1 20 0 0\"/></components></object>",
            "<item objectid=\"1\"/>")),
            ("/3D/a.model", Model(Cube(10), "<item objectid=\"999\"/>")),
            ("/3D/b.model", Model(Cube(5), "<item objectid=\"999\"/>")));
        var mesh = Normalize(AdmittedThreeMfMeshReader.Read(bytes));
        Assert.Equal(24, mesh.Triangles.Count);
        Assert.Equal(new Vector3(25, 10, 10), Maximum(mesh));
    }

    [Fact]
    public void LegacyBareCrossModelReference_RequiresUniqueObjectId()
    {
        string root = Model("<object id=\"2\"><components><component objectid=\"1\"/></components></object>", "<item objectid=\"2\"/>");
        Assert.Equal(12, AdmittedThreeMfMeshReader.Read(Package(("/3D/root.model", root),
            ("/3D/a.model", Model(Cube(10), "")))).Triangles.Count);
        Assert.Throws<FormatException>(() => AdmittedThreeMfMeshReader.Read(Package(("/3D/root.model", root),
            ("/3D/a.model", Model(Cube(10), "")), ("/3D/b.model", Model(Cube(5), "")))));
    }

    [Theory]
    [InlineData("<object id=\"1\"><components><component objectid=\"1\"/></components></object>", "<item objectid=\"1\"/>")]
    [InlineData("", "<item objectid=\"99\"/>")]
    [InlineData("<object id=\"1\"><mesh><vertices><vertex x=\"NaN\" y=\"0\" z=\"0\"/></vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object>", "<item objectid=\"1\"/>")]
    [InlineData("<object id=\"1\"><mesh><vertices><vertex x=\"0\" y=\"0\" z=\"0\"/></vertices><triangles><triangle v1=\"-1\" v2=\"0\" v3=\"0\"/></triangles></mesh></object>", "<item objectid=\"1\"/>")]
    public void InvalidGraphOrGeometry_FailsClosed(string resources, string build) =>
        Assert.Throws<FormatException>(() => Read(Model(resources, build)));

    [Fact]
    public void DuplicateIdsAndTraversalAndDtdAndRequiredGeometryExtension_FailClosed()
    {
        Assert.Throws<FormatException>(() => Read(Model(Cube(10) + Cube(10), "<item objectid=\"1\"/>")));
        Assert.Throws<FormatException>(() => AdmittedThreeMfMeshReader.Read(Package(("../escape.model", Model(Cube(10), "<item objectid=\"1\"/>")))));
        Assert.Throws<FormatException>(() => Read("<!DOCTYPE model [<!ENTITY x SYSTEM 'file:///never-read'>]>" + Model(Cube(10), "<item objectid=\"1\"/>")));
        Assert.Throws<FormatException>(() => Read(Model(Cube(10), "<item objectid=\"1\"/>")
            .Replace("unit=\"millimeter\"", "unit=\"millimeter\" xmlns:unsupported=\"urn:unknown-geometry\" requiredextensions=\"unsupported\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void CancellationAndCompressedAndExpandedBudgets_FailBeforeSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AdmittedThreeMfMeshReader.Read([0], cancellation.Token));
        Assert.Throws<FormatException>(() => AdmittedThreeMfMeshReader.Read(new byte[8 * 1024 * 1024 + 1]));
        Assert.Throws<FormatException>(() => Read(Model(Cube(10), "<item objectid=\"1\"/>")
            .Replace("</model>", "<metadata name=\"oversize\">" + new string('x', 16 * 1024 * 1024 + 1) + "</metadata></model>", StringComparison.Ordinal)));
    }

    [Fact]
    public void GroupingAndFill_AffectDigestWithoutChangingLegacyDigestOrHoleBehavior()
    {
        var parsed = Read(Model(Cube(10, inner: true), "<item objectid=\"1\"/>"));
        var positive = Normalize(parsed);
        var legacy = Normalize(new MeshInput(parsed.Triangles, 1, Matrix4x4.Identity));
        Assert.NotEqual(legacy.Sha256, positive.Sha256);
        Assert.All(Layers(positive), layer => Assert.Equal(100, Area(layer), 3));
        Assert.All(Layers(legacy), layer => Assert.Equal(84, Area(layer), 3));
        var triangle = new MeshInput([new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY)], 1, Matrix4x4.Identity);
        // Frozen from the pre-change legacy canonical body: 0,0,0;0,1000,0;1000,0,0.
        Assert.Equal("3B1AFC8C69E20B1F0FE3064B6EE8DE1F6B5C813B1E173F013B3C22289888EACA", Normalize(triangle).Sha256);
    }


    [Fact]
    public void UnreferencedModelsCannotSupplyBareReferencesAndInertOtherObjectsDoNotPrint()
    {
        string root = Model("<object id=\"2\"><components><component objectid=\"1\"/></components></object>", "<item objectid=\"2\"/>");
        byte[] package = Package(("/3D/root.model", root), ("/3D/a.model", Model(Cube(10), "")));
        using var buffer = new MemoryStream();
        using (var output = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var input = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            foreach (var entry in input.Entries.Where(e => e.FullName != "3D/_rels/root.model.rels"))
            {
                using var source = entry.Open();
                using var destination = output.CreateEntry(entry.FullName).Open();
                source.CopyTo(destination);
            }
        }
        Assert.Throws<FormatException>(() => AdmittedThreeMfMeshReader.Read(buffer.ToArray()));
        string inert = Cube(10) + Cube(50).Replace("id=\"1\"", "id=\"2\" type=\"other\"").Replace(" type=\"model\"", "");
        var mesh = Normalize(Read(Model(inert,
            "<metadatagroup><metadata name=\"inert\">not quantity or material</metadata></metadatagroup><item objectid=\"1\"/>")));
        Assert.Equal(new Vector3(10), Maximum(mesh));
    }

    [Theory]
    [InlineData("depth")]
    [InlineData("instances")]
    [InlineData("models")]
    [InlineData("entries")]
    [InlineData("directory-mismatch")]
    public void PackageStructureBudgets_FailClosed(string budget)
    {
        byte[] bytes;
        if (budget == "depth")
            bytes = Package(("/3D/root.model", Model(Cube(10), "<item objectid=\"1\"/>").Replace("</model>",
                string.Concat(Enumerable.Repeat("<metadata>", 65)) + string.Concat(Enumerable.Repeat("</metadata>", 65)) + "</model>", StringComparison.Ordinal)));
        else if (budget == "instances")
            bytes = Package(("/3D/root.model", Model(Cube(10), string.Concat(Enumerable.Repeat("<item objectid=\"1\"/>", 257)))));
        else if (budget == "models")
            bytes = Package(Enumerable.Range(0, 17).Select(i => ($"/3D/{i}.model", Model(Cube(10), i == 0 ? "<item objectid=\"1\"/>" : ""))).ToArray());
        else if (budget == "directory-mismatch")
        {
            bytes = Package(("/3D/root.model", Model(Cube(10), "<item objectid=\"1\"/>")));
            bytes[^12] = 0;
            bytes[^11] = 0;
        }
        else
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                for (int i = 0; i < 129; i++) zip.CreateEntry($"part-{i}");
            bytes = memory.ToArray();
        }
        Assert.Throws<FormatException>(() => AdmittedThreeMfMeshReader.Read(bytes));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntersectingCombGrid_RespectsBooleanWorkAdmissionAndCancellation(bool cancelled)
    {
        var items = new StringBuilder();
        for (int index = 0; index < 128; index++)
        {
            items.Append(FormattableString.Invariant($"<item objectid=\"1\" transform=\"1000 0 0 0 0.2 0 0 0 1 0 {index * 2} 0\"/>"));
            items.Append(FormattableString.Invariant($"<item objectid=\"1\" transform=\"0.2 0 0 0 1000 0 0 0 1 {index * 2} 0 0\"/>"));
        }
        var mesh = Normalize(Read(Model(Cube(1), items.ToString())));
        Assert.Empty(mesh.Diagnostics);
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        Assert.True(FdmRuntimeProfileCatalog.LoadEmbedded().TryResolveTrustedProfile("PLA", BuildPreference.Standard, out var profile));
        var budget = new AnalysisBudget(50_000, 2000, 1_000_000, cancellation.Token);
        if (cancelled)
            Assert.ThrowsAny<OperationCanceledException>(() => LayerContourBuilder.Build(mesh, new("identity", Matrix4x4.Identity), profile!, budget));
        else
            Assert.Equal("path_segment_budget_exceeded", Assert.Throws<SimulationGeometryException>(() =>
                LayerContourBuilder.Build(mesh, new("identity", Matrix4x4.Identity), profile!, budget)).ReasonCode);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingStlAndObjHoles_KeepLegacyEvenOddFill(bool obj)
    {
        Vector3[] outer = [new(0,0,0),new(10,0,0),new(10,10,0),new(0,10,0),
            new(0,0,10),new(10,0,10),new(10,10,10),new(0,10,10)];
        Vector3[] inner = [new(3,3,0),new(7,3,0),new(7,7,0),new(3,7,0),
            new(3,3,10),new(7,3,10),new(7,7,10),new(3,7,10)];
        (int A,int B,int C)[] faces = [(0,2,1),(0,3,2),(4,5,6),(4,6,7),(0,1,5),(0,5,4),
            (1,2,6),(1,6,5),(2,3,7),(2,7,6),(3,0,4),(3,4,7)];
        var triangles = faces.Select(f => new MeshTriangle(outer[f.A], outer[f.B], outer[f.C]))
            .Concat(faces.Select(f => new MeshTriangle(inner[f.A], inner[f.C], inner[f.B]))).ToArray();
        MeshInput input;
        if (obj)
        {
            var text = new StringBuilder();
            foreach (var point in triangles.SelectMany(t => new[] { t.A,t.B,t.C }))
                text.AppendLine(FormattableString.Invariant($"v {point.X} {point.Y} {point.Z}"));
            for (int index=0;index<triangles.Length;index++)
                text.AppendLine(FormattableString.Invariant($"f {index*3+1} {index*3+2} {index*3+3}"));
            input = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(text.ToString()));
        }
        else
        {
            using var memory = new MemoryStream();
            using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen:true))
            {
                writer.Write(new byte[80]); writer.Write((uint)triangles.Length);
                foreach (var triangle in triangles)
                {
                    writer.Write(new byte[12]);
                    foreach (var point in new[] { triangle.A,triangle.B,triangle.C })
                    { writer.Write(point.X); writer.Write(point.Y); writer.Write(point.Z); }
                    writer.Write((ushort)0);
                }
            }
            input = AdmittedStlMeshReader.Read(memory.ToArray());
        }
        Assert.Null(input.PositiveFillGroups);
        var mesh = Normalize(input);
        Assert.Null(mesh.PositiveFillGroups);
        Assert.All(Layers(mesh), layer => Assert.Equal(84, Area(layer), 3));
    }


    [Fact]
    public void RightmostFooterInsideComment_CannotBypassDirectoryAdmission()
    {
        byte[] original = Convert.FromBase64String(OriginalFixture);
        int footer = original.Length - 22;
        byte[] crafted = new byte[original.Length + 23];
        original.CopyTo(crafted, 0);
        // The real footer owns a comment containing an alternate EOCD plus one trailing byte.
        crafted[footer + 20] = 23;
        crafted[footer + 21] = 0;
        original.AsSpan(footer, 22).CopyTo(crafted.AsSpan(original.Length, 22));
        crafted[original.Length + 8] = 0xFE;
        crafted[original.Length + 9] = 0xFF;
        crafted[original.Length + 10] = 0xFE;
        crafted[original.Length + 11] = 0xFF;
        crafted[original.Length + 20] = 0;
        crafted[original.Length + 21] = 0;
        Assert.Throws<FormatException>(() => AdmittedThreeMfMeshReader.ValidateDirectory(crafted));
        Assert.Throws<FormatException>(() => AdmittedThreeMfMeshReader.Read(crafted));
    }

    private static MeshInput Read(string model) => AdmittedThreeMfMeshReader.Read(Package(("/3D/root.model", model)));
    private static NormalizedMesh Normalize(MeshInput mesh) => MeshNormalizer.Normalize(mesh, new(0.001, 0.02));
    private static IReadOnlyList<LayerContours> Layers(NormalizedMesh mesh)
    {
        Assert.True(FdmRuntimeProfileCatalog.LoadEmbedded().TryResolveTrustedProfile("PLA", BuildPreference.Standard, out var profile));
        var layers = LayerContourBuilder.Build(mesh, new("identity", Matrix4x4.Identity), profile!,
            new(50_000, 2000, 1_000_000, default));
        Assert.NotEmpty(layers);
        return layers;
    }
    private static Vector3 Maximum(NormalizedMesh mesh) => mesh.Triangles
        .SelectMany(t => new[] { t.A, t.B, t.C }).Aggregate(Vector3.Zero, Vector3.Max);
    private static double Area(LayerContours layer) => layer.Rings.Sum(ring =>
    {
        double twice = 0;
        for (int i = 0; i < ring.Points.Count; i++)
        {
            var a = ring.Points[i]; var b = ring.Points[(i + 1) % ring.Points.Count];
            twice += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return (ring.IsHole ? -1 : 1) * Math.Abs(twice) / 2;
    });
    private static string Model(string resources, string build, string unit = "millimeter") =>
        $"<model xmlns=\"{Core}\" xmlns:p=\"{Production}\" unit=\"{unit}\"><resources>{resources}</resources><build>{build}</build></model>";
    private static byte[] Package(params (string Path, string Model)[] models)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string text) { using var writer = new StreamWriter(zip.CreateEntry(path.TrimStart('/')).Open(), new UTF8Encoding(false)); writer.Write(text); }
            Add("_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"{models[0].Path}\"/></Relationships>");
            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/></Types>");
            if (models.Length > 1)
                Add("3D/_rels/root.model.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + string.Join("", models.Skip(1).Select((model, i) => $"<Relationship Id=\"r{i}\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"{model.Path}\"/>")) + "</Relationships>");
            foreach (var model in models) Add(model.Path, model.Model);
        }
        return bytes.ToArray();
    }
    private static string Cube(int size, bool inner = false, bool reverseInner = false)
    {
        var vertices = new List<Vector3>(); var faces = new List<(int A, int B, int C)>();
        void Box(float low, float high)
        {
            int n = vertices.Count;
            vertices.AddRange([new(low, low, 0), new(high, low, 0), new(high, high, 0), new(low, high, 0),
                new(low, low, size), new(high, low, size), new(high, high, size), new(low, high, size)]);
            (int, int, int)[] box = [(0,2,1),(0,3,2),(4,5,6),(4,6,7),(0,1,5),(0,5,4),(1,2,6),(1,6,5),(2,3,7),(2,7,6),(3,0,4),(3,4,7)];
            foreach (var (a,b,c) in box) faces.Add((n+a,n+b,n+c));
        }
        Box(0, size);
        if (inner) { int begin = faces.Count; Box(3, 7); if (reverseInner) for (int i=begin;i<faces.Count;i++) faces[i]=(faces[i].A,faces[i].C,faces[i].B); }
        return "<object id=\"1\" type=\"model\"><mesh><vertices>"
            + string.Join("", vertices.Select(v => FormattableString.Invariant($"<vertex x=\"{v.X}\" y=\"{v.Y}\" z=\"{v.Z}\"/>")))
            + "</vertices><triangles>" + string.Join("", faces.Select(f => $"<triangle v1=\"{f.A}\" v2=\"{f.B}\" v3=\"{f.C}\"/>"))
            + "</triangles></mesh></object>";
    }
}
