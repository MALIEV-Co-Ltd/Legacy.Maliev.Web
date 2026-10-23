using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationGltfUnitPolicyTests
{
    [Theory]
    [InlineData(false, InstantQuotationGltfLengthUnit.Millimetres, 0.3, 0.1)]
    [InlineData(true, InstantQuotationGltfLengthUnit.Millimetres, 0.3, 0.1)]
    [InlineData(false, InstantQuotationGltfLengthUnit.Metres, 300, 100)]
    [InlineData(true, InstantQuotationGltfLengthUnit.Metres, 300, 100)]
    public void ExplicitUnits_ScaleFullWorldGeometryWithoutChangingRawBytes(
        bool binary, InstantQuotationGltfLengthUnit unit, double width, double height)
    {
        var bytes = TwoCubes(binary);
        var raw = bytes.ToArray();
        var digest = SHA256.HashData(bytes);
        var policy = new InstantQuotationGltfUnitPolicy(1, unit);

        var parsed = AdmittedGltfMeshReader.Read(bytes, default, policy);
        var mesh = MeshNormalizer.Normalize(parsed, new GeometryTolerance(0.001, 0.02));
        var vertices = mesh.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();

        Assert.Equal(24, mesh.Triangles.Count);
        Assert.Equal(width, vertices.Max(vertex => vertex.X), 3);
        Assert.Equal(height, vertices.Max(vertex => vertex.Y), 3);
        Assert.Equal(height, vertices.Max(vertex => vertex.Z), 3);
        Assert.Equal(unit == InstantQuotationGltfLengthUnit.Metres ? 1000 : 1, parsed.UnitsToMillimetres);
        Assert.Equal(raw, bytes);
        Assert.Equal(digest, SHA256.HashData(bytes));
    }

    [Theory]
    [InlineData(0, InstantQuotationGltfLengthUnit.Millimetres, false)]
    [InlineData(1, InstantQuotationGltfLengthUnit.Millimetres, true)]
    [InlineData(1, InstantQuotationGltfLengthUnit.Metres, true)]
    [InlineData(0, InstantQuotationGltfLengthUnit.Metres, false)]
    [InlineData(2, InstantQuotationGltfLengthUnit.Millimetres, false)]
    [InlineData(1, (InstantQuotationGltfLengthUnit)99, false)]
    public void NewGltfAdmission_RequiresExplicitKnownPolicy(int version, InstantQuotationGltfLengthUnit unit, bool accepted)
    {
        var policy = new InstantQuotationGltfUnitPolicy(version, unit);
        Assert.Equal(accepted, policy.IsValidForFile("part.GLB", newUpload: true));
        Assert.Equal(accepted, policy.IsValidForFile("part.gltf", newUpload: true));
    }

    [Fact]
    public void RestoredLegacyPolicy_PreservesMillimetresAndCannotScaleOtherFormats()
    {
        Assert.True(InstantQuotationGltfUnitPolicy.Legacy.IsValidForFile("old.glb", newUpload: false));
        Assert.True(InstantQuotationGltfUnitPolicy.Legacy.IsValidForFile("part.stl", newUpload: true));
        Assert.False(new InstantQuotationGltfUnitPolicy(1, InstantQuotationGltfLengthUnit.Metres)
            .IsValidForFile("part.stl", newUpload: true));
        var mesh = AdmittedGltfMeshReader.Read(TwoCubes(false));
        Assert.Equal(1, mesh.UnitsToMillimetres);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AdmissionScaleCheck_RejectsPolicyOnlyMismatchAndKeepsRealTransformRounding(bool binary, bool reflected)
    {
        var bytes = TwoCubes(binary, reflected);
        var policy = new InstantQuotationGltfUnitPolicy(1, InstantQuotationGltfLengthUnit.Metres);
        var claim = new InstantQuotationGeometryClaim(
            1, Convert.ToHexStringLower(SHA256.HashData(bytes)), 300, reflected ? 300 : 100, reflected ? 200 : 100,
            reflected ? 12_000_000 : 2_000_000, 120_000,
            Enumerable.Repeat(10_000d, 64).ToArray(), Enumerable.Repeat(400d, 64).ToArray(),
            24, 2, true, false, false, 1)
        {
            GltfUnitPolicy = policy,
        };

        Assert.True(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim, default));
        Assert.False(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim with
        {
            GltfUnitPolicy = new(1, InstantQuotationGltfLengthUnit.Millimetres),
        }, default));
        Assert.False(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim with { VolumeMm3 = 0.002 }, default));
        Assert.False(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim with { DimensionXmm = 0.3 }, default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Unit\":0}")]
    public void SuppliedPartialPolicy_CannotDeserializeAsAnExplicitChoice(string json)
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<InstantQuotationGltfUnitPolicy>(json));
    }

    private static byte[] TwoCubes(bool binary, bool reflected = false)
    {
        var document = JsonNode.Parse(AdmittedGltfMeshReaderTests.OriginalGltf)!.AsObject();
        document["nodes"]![0]!["scale"] = reflected ? new JsonArray(0.01, 0.02, -0.03) : new JsonArray(0.01, 0.01, 0.01);
        document["nodes"]![0]!["children"] = new JsonArray(1, 2);
        document["nodes"]!.AsArray().Add(new JsonObject
        {
            ["mesh"] = 0,
            ["translation"] = new JsonArray(20, 0, 0),
        });
        if (!binary) return Encoding.UTF8.GetBytes(document.ToJsonString());

        var buffer = document["buffers"]![0]!;
        var uri = buffer["uri"]!.GetValue<string>();
        var content = Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
        buffer.AsObject().Remove("uri");
        var json = Encoding.UTF8.GetBytes(document.ToJsonString());
        var paddedJsonLength = (json.Length + 3) & ~3;
        var bytes = new byte[12 + 8 + paddedJsonLength + 8 + content.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546c67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)paddedJsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4e4f534a);
        bytes.AsSpan(20, paddedJsonLength).Fill(0x20);
        json.CopyTo(bytes, 20);
        var binOffset = 20 + paddedJsonLength;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(binOffset), (uint)content.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(binOffset + 4), 0x004e4942);
        content.CopyTo(bytes, binOffset + 8);
        return bytes;
    }
    [Theory]
    [InlineData("morph", false, InstantQuotationGltfLengthUnit.Millimetres, 0.15, 0.0015)]
    [InlineData("morph", false, InstantQuotationGltfLengthUnit.Metres, 150, 1500000)]
    [InlineData("morph", true, InstantQuotationGltfLengthUnit.Millimetres, 0.15, 0.0015)]
    [InlineData("morph", true, InstantQuotationGltfLengthUnit.Metres, 150, 1500000)]
    [InlineData("skin", false, InstantQuotationGltfLengthUnit.Millimetres, 0.2, 0.002)]
    [InlineData("skin", false, InstantQuotationGltfLengthUnit.Metres, 200, 2000000)]
    [InlineData("skin", true, InstantQuotationGltfLengthUnit.Millimetres, 0.2, 0.002)]
    [InlineData("skin", true, InstantQuotationGltfLengthUnit.Metres, 200, 2000000)]
    [InlineData("morph-skin", false, InstantQuotationGltfLengthUnit.Millimetres, 0.3, 0.003)]
    [InlineData("morph-skin", false, InstantQuotationGltfLengthUnit.Metres, 300, 3000000)]
    [InlineData("morph-skin", true, InstantQuotationGltfLengthUnit.Millimetres, 0.3, 0.003)]
    [InlineData("morph-skin", true, InstantQuotationGltfLengthUnit.Metres, 300, 3000000)]
    public void RenderedDefaultPose_MatchesTrustedScaledBoundsAndRawVolume(
        string kind, bool binary, InstantQuotationGltfLengthUnit unit, double width, double volume)
    {
        var path = Path.GetFullPath(Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(),
            "..", "Legacy.Maliev.Web.Tests", "TestAssets", "GltfUnits", $"default-{kind}.gltf"));
        var source = File.ReadAllBytes(path);
        var document = JsonNode.Parse(source)!.AsObject();
        var bytes = binary ? BinaryFixture(document) : source;
        var raw = bytes.ToArray();
        var policy = new InstantQuotationGltfUnitPolicy(1, unit);
        var height = unit == InstantQuotationGltfLengthUnit.Metres ? 100d : 0.1d;
        var parsed = AdmittedGltfMeshReader.Read(bytes, default, policy, preserveRenderingWinding: true);
        var triangles = parsed.Triangles.Select(triangle => new MeshTriangle(
            triangle.A * (float)parsed.UnitsToMillimetres,
            triangle.B * (float)parsed.UnitsToMillimetres,
            triangle.C * (float)parsed.UnitsToMillimetres)).ToArray();
        var vertices = triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();

        var scale = unit == InstantQuotationGltfLengthUnit.Metres ? 1000d : 1d;
        var minimumX = kind == "morph" ? -0.055 : kind == "skin" ? 0.02 : -0.03;
        Assert.Equal(minimumX * scale, vertices.Min(vertex => vertex.X), 3);
        Assert.Equal(-0.09 * scale, vertices.Min(vertex => vertex.Y), 3);
        Assert.Equal(-0.02 * scale, vertices.Min(vertex => vertex.Z), 3);
        Assert.Equal(12, triangles.Length);
        Assert.Equal(width, vertices.Max(vertex => vertex.X) - vertices.Min(vertex => vertex.X), 3);
        Assert.Equal(height, vertices.Max(vertex => vertex.Y) - vertices.Min(vertex => vertex.Y), 3);
        Assert.Equal(height, vertices.Max(vertex => vertex.Z) - vertices.Min(vertex => vertex.Z), 3);
        var signedVolume = triangles.Sum(triangle =>
            ((double)triangle.A.X * ((double)triangle.B.Y * triangle.C.Z - (double)triangle.B.Z * triangle.C.Y)
            - (double)triangle.A.Y * ((double)triangle.B.X * triangle.C.Z - (double)triangle.B.Z * triangle.C.X)
            + (double)triangle.A.Z * ((double)triangle.B.X * triangle.C.Y - (double)triangle.B.Y * triangle.C.X)) / 6);
        Assert.InRange(Math.Abs(signedVolume), volume * 0.99999, volume * 1.00001);
        var claim = new InstantQuotationGeometryClaim(
            1, Convert.ToHexStringLower(SHA256.HashData(bytes)), width, height, height,
            volume, 1, Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
            12, 1, true, false, false, 1)
        {
            GltfUnitPolicy = policy,
        };
        Assert.True(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim, default));
        // A raw-POSITION claim has neither the authored morph nor the initial bone scale.
        Assert.False(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim with
        {
            DimensionXmm = height,
            VolumeMm3 = unit == InstantQuotationGltfLengthUnit.Metres ? 1_000_000 : 0.001,
        }, default));
        Assert.Equal(raw, bytes);
    }

    internal static byte[] BinaryFixture(JsonObject document)
    {
        var buffer = document["buffers"]![0]!;
        var content = Convert.FromBase64String(buffer["uri"]!.GetValue<string>().Split(',')[1]);
        buffer.AsObject().Remove("uri");
        var json = Encoding.UTF8.GetBytes(document.ToJsonString());
        var padded = (json.Length + 3) & ~3;
        var bytes = new byte[12 + 8 + padded + 8 + content.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546c67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)padded);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4e4f534a);
        bytes.AsSpan(20, padded).Fill(0x20);
        json.CopyTo(bytes, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20 + padded), (uint)content.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24 + padded), 0x004e4942);
        content.CopyTo(bytes, 28 + padded);
        return bytes;
    }

}
