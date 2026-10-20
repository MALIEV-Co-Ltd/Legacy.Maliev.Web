using System.Numerics;
using System.Text;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class AdmittedObjMeshReaderTests
{
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
