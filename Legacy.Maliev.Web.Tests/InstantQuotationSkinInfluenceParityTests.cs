using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationSkinInfluenceParityTests
{
    public static IEnumerable<object[]> RenderedCases()
    {
        foreach (var name in new[] { "float", "float-zero", "uint8", "uint16", "uint8-zero", "uint16-zero", "uint8-interleaved", "uint16-sparse" })
            foreach (var binary in new[] { false, true })
                foreach (var unit in new[] { InstantQuotationGltfLengthUnit.Millimetres, InstantQuotationGltfLengthUnit.Metres })
                    yield return [name, binary, unit];
    }

    [Theory]
    [MemberData(nameof(RenderedCases))]
    public void OriginalFirstFourInfluences_MatchTrustedGeometryAndClaim(
        string name, bool binary, InstantQuotationGltfLengthUnit unit)
    {
        var document = Fixture(name);
        var bytes = binary ? InstantQuotationGltfUnitPolicyTests.BinaryFixture(document) : Encoding.UTF8.GetBytes(document.ToJsonString());
        var digest = SHA256.HashData(bytes);
        var policy = new InstantQuotationGltfUnitPolicy(1, unit);
        if (name is "uint8-zero" or "uint16-zero")
        {
            // The original integer path keeps zero weights, so its rendered geometry collapses.
            Assert.Throws<FormatException>(() => AdmittedGltfMeshReader.Read(bytes, default, policy));
            Assert.Equal(digest, SHA256.HashData(bytes));
            return;
        }

        var scale = unit == InstantQuotationGltfLengthUnit.Metres ? 1000d : 1d;
        var (center, size) = name switch
        {
            "float" => (new[] { 0.0675, -0.3925, 0.16 }, new[] { 0.15, 1.05, 0.5 }),
            "float-zero" => (new[] { 0.045, -0.325, 0.15 }, new[] { 0.1, 0.9, 0.4 }),
            _ => (new[] { 0.639, -0.721, 0.322 }, new[] { 0.06, 0.42, 0.2 }),
        };
        var parsed = AdmittedGltfMeshReader.Read(bytes, default, policy, preserveRenderingWinding: true);
        Assert.Equal(scale, parsed.UnitsToMillimetres);
        Assert.Equal(12, parsed.Triangles.Count);
        var vertices = parsed.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();
        foreach (var (axis, values) in new[]
        {
            (0, vertices.Select(vertex => (double)vertex.X).ToArray()),
            (1, vertices.Select(vertex => (double)vertex.Y).ToArray()),
            (2, vertices.Select(vertex => (double)vertex.Z).ToArray()),
        })
        {
            Assert.InRange(Math.Abs(values.Min() * scale - (center[axis] - size[axis] / 2) * scale), 0, 0.00001 * scale);
            Assert.InRange(Math.Abs(values.Max() * scale - (center[axis] + size[axis] / 2) * scale), 0, 0.00001 * scale);
        }

        var volume = size.Aggregate(1d, (product, dimension) => product * dimension) * scale * scale * scale;
        var claim = new InstantQuotationGeometryClaim(1, Convert.ToHexStringLower(digest),
            size[0] * scale, size[1] * scale, size[2] * scale, volume, 1,
            Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
            12, 1, true, false, false, 1)
        { GltfUnitPolicy = policy };
        Assert.True(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim, default));
        Assert.False(AdmittedGltfMeshReader.MatchesClaimScale(bytes, claim with { DimensionXmm = claim.DimensionXmm * 2 }, default));
        Assert.Equal(digest, SHA256.HashData(bytes));
    }

    [Theory]
    [InlineData("missing-weights")]
    [InlineData("invalid-joint")]
    [InlineData("negative-weight")]
    [InlineData("invalid-total")]
    [InlineData("unsupported-set")]
    [InlineData("wrong-count")]
    public void UnrenderedInfluences_StillRequireValidOwnedData(string mutation)
    {
        var document = Fixture("float");
        var attributes = document["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject();
        var joints = attributes["JOINTS_1"]!.GetValue<int>();
        var weights = attributes["WEIGHTS_1"]!.GetValue<int>();
        switch (mutation)
        {
            case "missing-weights": attributes.Remove("WEIGHTS_1"); break;
            case "unsupported-set": attributes["JOINTS_4"] = joints; attributes["WEIGHTS_4"] = weights; break;
            case "wrong-count": document["accessors"]![weights]!["count"] = 7; break;
            default:
                var buffer = document["buffers"]![0]!;
                var content = Convert.FromBase64String(buffer["uri"]!.GetValue<string>().Split(',')[1]);
                var accessor = document["accessors"]![mutation == "invalid-joint" ? joints : weights]!;
                var offset = document["bufferViews"]![accessor["bufferView"]!.GetValue<int>()]!["byteOffset"]!.GetValue<int>();
                if (mutation == "invalid-joint") content[offset] = 2;
                else BinaryPrimitives.WriteSingleLittleEndian(content.AsSpan(offset), mutation == "negative-weight" ? -0.6f : 0.2f);
                buffer["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(content);
                break;
        }

        Assert.Throws<FormatException>(() => AdmittedGltfMeshReader.Read(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    private static JsonObject Fixture(string name)
    {
        var path = Path.GetFullPath(Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(),
            "..", "Legacy.Maliev.Web.Tests", "TestAssets", "GltfUnits", $"skin-influences-{name}.gltf"));
        return JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    }
}
