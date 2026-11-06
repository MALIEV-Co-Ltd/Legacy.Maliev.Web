using System.Numerics;
using System.Text;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class AdmittedObjMeshReaderTests
{
    [Theory]
    [InlineData("o", 0, 100)]
    [InlineData("o", 5, 150)]
    [InlineData("o", 20, 200)]
    [InlineData("g", 0, 100)]
    [InlineData("g", 5, 150)]
    [InlineData("g", 20, 200)]
    public void DeclaredBodyOccurrences_UnionCoincidentOverlappingAndDisjointSolids(string record, int offset, double area)
    {
        var source = new StringBuilder();
        int vertices = 0;
        source.AppendLine($"{record} repeated-name");
        AppendBox(source, ref vertices, 0, 10, 0, 10);
        source.AppendLine($"{record} repeated-name");
        AppendBox(source, ref vertices, offset, offset + 10, 0, 10);
        var input = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source.ToString()));

        Assert.All(Layers(input), layer => Assert.Equal(area, Area(layer), 3));
        Assert.Equal(Enumerable.Repeat(0, 12).Concat(Enumerable.Repeat(1, 12)), input.PositiveFillGroups);
    }

    [Theory]
    [InlineData("o", false)]
    [InlineData("o", true)]
    [InlineData("g", false)]
    [InlineData("g", true)]
    public void OneDeclaredBody_PreservesItsInnerHoleAndWholeBodyWinding(string record, bool reverse)
    {
        var source = new StringBuilder($"{record} hollow\n");
        int vertices = 0;
        AppendBox(source, ref vertices, 0, 10, 0, 10, reverse);
        AppendBox(source, ref vertices, 3, 7, 3, 7, !reverse);
        var input = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source.ToString()));
        Assert.All(Layers(input), layer => Assert.Equal(84, Area(layer), 3));
    }

    [Fact]
    public void UndeclaredBodies_RetainLegacyEvenOddHoles()
    {
        var source = new StringBuilder();
        int vertices = 0;
        AppendBox(source, ref vertices, 0, 10, 0, 10);
        AppendBox(source, ref vertices, 3, 7, 3, 7);
        var input = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source.ToString()));
        Assert.Null(input.PositiveFillGroups);
        Assert.All(Layers(input), layer => Assert.Equal(84, Area(layer), 3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectGroupSubdivisions_PreserveTheCavity(bool reverse)
    {
        var source = new StringBuilder("o hollow\ng outer primary\n");
        int vertices = 0;
        AppendBox(source, ref vertices, 0, 10, 0, 10, reverse);
        source.AppendLine("g inner secondary");
        AppendBox(source, ref vertices, 3, 7, 3, 7, !reverse);
        var input = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source.ToString()));
        Assert.All(input.PositiveFillGroups!, group => Assert.Equal(0, group));
        Assert.All(Layers(input), layer => Assert.Equal(84, Area(layer), 3));
    }

    [Theory]
    [InlineData("o")]
    [InlineData("g")]
    public void EmptyDeclarationsAndMultipleLabels_DoNotDuplicateFanTriangles(string record)
    {
        var input = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat($"{record} empty\n", 300))
            + $"{record} primary secondary\nv 0 0 0\nv 8 0 0\nv 8 8 0\nv 0 8 0\nf 1 2 3 4\n"));
        Assert.Equal(2, input.Triangles.Count);
        Assert.Equal([0, 0], input.PositiveFillGroups);
    }

    [Theory]
    [InlineData("o")]
    [InlineData("g")]
    public void ObjectOccurrenceBudget_RefusesThe257thNonemptyBody(string record)
    {
        string source = "v 0 0 0\nv 1 0 0\nv 0 1 0\n"
            + string.Concat(Enumerable.Repeat($"{record} repeated\nf 1 2 3\n", 256));
        var input = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source));
        Assert.Equal(256, input.PositiveFillGroups!.Distinct().Count());
        Assert.Throws<FormatException>(() => AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(
            source + $"{record} repeated\nf 1 2 3\n")));
    }

    private static IReadOnlyList<LayerContours> Layers(MeshInput input)
    {
        var mesh = MeshNormalizer.Normalize(input, new(0.001, 0.02));
        Assert.Empty(mesh.Diagnostics);
        Assert.True(FdmRuntimeProfileCatalog.LoadEmbedded().TryResolveTrustedProfile("PLA", BuildPreference.Standard, out var profile));
        var layers = LayerContourBuilder.Build(mesh, new("identity", Matrix4x4.Identity), profile!,
            new(50_000, 2000, 1_000_000, default));
        Assert.NotEmpty(layers);
        return layers;
    }

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

    private static void AppendBox(StringBuilder source, ref int vertexCount, int lowX, int highX, int lowY, int highY, bool reverse = false)
    {
        Vector3[] points = [new(lowX, lowY, 0), new(highX, lowY, 0), new(highX, highY, 0), new(lowX, highY, 0),
            new(lowX, lowY, 10), new(highX, lowY, 10), new(highX, highY, 10), new(lowX, highY, 10)];
        foreach (var point in points) source.AppendLine(FormattableString.Invariant($"v {point.X} {point.Y} {point.Z}"));
        (int A, int B, int C)[] faces = [(0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4),
            (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7)];
        foreach (var (a, b, c) in faces)
            source.AppendLine($"f {vertexCount + a + 1} {vertexCount + (reverse ? c : b) + 1} {vertexCount + (reverse ? b : c) + 1}");
        vertexCount += points.Length;
    }

    [Theory]
    [InlineData("f 1 2 3 4")]
    [InlineData("f -4 -3 -2 -1")]
    [InlineData("f 1/1/1 2/2/1 3/3/1 4/4/1")]
    [InlineData("f 1//1 2//1 3//1 4//1")]
    public void ConvexFaces_ResolveIndicesAndPreserveMillimetreCoordinates(string face)
    {
        string source = "v 0 0 0\nv 8 0 0\nv 8 8 0\nv 0 8 0\n"
            + "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nvn 0 0 1\n" + face;

        var mesh = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source));

        Assert.Equal(2, mesh.Triangles.Count);
        Assert.Equal(1, mesh.UnitsToMillimetres);
        Assert.Equal(Matrix4x4.Identity, mesh.ComponentTransform);
        Assert.Equal(new Vector3(8, 8, 0), mesh.Triangles[0].C);
        Assert.Equal(new Vector3(0, 8, 0), mesh.Triangles[1].C);
    }

    [Fact]
    public void CosmeticRecordsAndContinuation_DoNotFetchMaterialsOrChangeGeometry()
    {
        var mesh = AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(
            "\uFEFF# original bytes remain hash-bound\r\nv 0 0 0 1\r\nv 8 0 0 1 0 0\r\nv 0 8 0\r\n"
            + "o part\ng shell\ns 1\nmtllib https://invalid.example/material.mtl\nusemtl paint\nusemap ignored\n"
            + "f 1 2 \\\r\n3 # surface\n"));

        Assert.Single(mesh.Triangles);
        Assert.Equal(new Vector3(0, 8, 0), mesh.Triangles[0].C);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v NaN 0 0")]
    [InlineData("v Infinity 0 0")]
    [InlineData("v 1e100 0 0")]
    [InlineData("v 0 0")]
    [InlineData("v 0 0 0 2")]
    [InlineData("v 0 0 0\nf 0 1 1")]
    [InlineData("v 0 0 0\nf 1 2 3")]
    [InlineData("v 0 0 0\nf -2 -1 -1")]
    [InlineData("v 0 0 0\nf 1x 1 1")]
    [InlineData("v 0 0 0\nf 1/1 1/1 1/1")]
    [InlineData("v 0 0 0\nf 1//2 1//2 1//2")]
    [InlineData("v 0 0 0\nf 1///1 1///1 1///1")]
    [InlineData("v 0 0 0\nf 1 1")]
    [InlineData("v 0 0 0\nl 1 1")]
    [InlineData("v 0 0 0\np 1")]
    [InlineData("curv 0 1 1 2")]
    [InlineData("call other.obj")]
    [InlineData("v 0 0 0\n\\")]
    public void MalformedOrUnsupportedGeometry_FailsClosed(string source) =>
        Assert.Throws<FormatException>(() => AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source)));

    [Theory]
    [InlineData("v 0 0 0\nv 2 0 0\nv 1 1 0\nv 2 2 0\nv 0 2 0\nf 1 2 3 4 5")]
    [InlineData("v 0 0 0\nv 2 0 0\nv 2 2 1\nv 0 2 0\nf 1 2 3 4")]
    [InlineData("v 0 0 0\nv 2 2 0\nv 0 2 0\nv 2 0 0\nf 1 2 3 4")]
    public void ConcaveNonplanarOrCrossingPolygons_AreNotSilentlyFanTriangulated(string source) =>
        Assert.Throws<FormatException>(() => AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(source)));

    [Fact]
    public void ByteLineVertexAndTriangleBudgets_AreEnforced()
    {
        Assert.Throws<FormatException>(() =>
            AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"
                + string.Concat(Enumerable.Repeat("# bounded filler\n", AdmittedObjMeshReader.MaximumBytes / 17 + 1)))));
        Assert.Throws<FormatException>(() =>
            AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"
                + "#" + new string('x', 4096))));
        Assert.Throws<FormatException>(() =>
            AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(
                string.Concat(Enumerable.Repeat("v 0 0 0\n", AdmittedObjMeshReader.MaximumVertices + 1)) + "f 1 2 3\n")));
        Assert.Throws<FormatException>(() =>
            AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\n"
                + string.Concat(Enumerable.Repeat("f 1 2 3\n", AdmittedObjMeshReader.MaximumTriangles + 1)))));
        Assert.Throws<DecoderFallbackException>(() => AdmittedObjMeshReader.Read([0xFF]));
    }

    [Fact]
    public void TextureNormalAndFaceBudgets_AreEnforced()
    {
        foreach (string record in new[] { "vt 0 0\n", "vn 0 0 1\n" })
        {
            Assert.Throws<FormatException>(() => AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(
                "v 0 0 0\nv 1 0 0\nv 0 1 0\n"
                + string.Concat(Enumerable.Repeat(record, AdmittedObjMeshReader.MaximumVertices + 1)) + "f 1 2 3\n")));
        }

        string vertices = string.Concat(Enumerable.Range(0, 257).Select(index =>
            FormattableString.Invariant($"v {Math.Cos(index * 2 * Math.PI / 257)} {Math.Sin(index * 2 * Math.PI / 257)} 0\n")));
        Assert.Throws<FormatException>(() => AdmittedObjMeshReader.Read(Encoding.UTF8.GetBytes(
            vertices + "f " + string.Join(" ", Enumerable.Range(1, 257)))));
    }

    [Fact]
    public void Cancellation_PrecedesDecodingOrAllocatingParserState()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            AdmittedObjMeshReader.Read([0xFF], cancellation.Token));
    }
}
