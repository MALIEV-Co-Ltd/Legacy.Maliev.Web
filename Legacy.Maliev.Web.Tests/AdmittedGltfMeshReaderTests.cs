// <copyright file="AdmittedGltfMeshReaderTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests;

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Legacy.Maliev.Web.Application.Pricing.Simulation;
using Xunit;

public sealed class AdmittedGltfMeshReaderTests
{
    // Exact committed source fixtures, not exports made by the reader under test.
    internal const string OriginalGltf = """{"scene":0,"scenes":[{"nodes":[0]}],"asset":{"version":"2.0","generator":"https://github.com/mikedh/trimesh"},"accessors":[{"componentType":5125,"type":"SCALAR","bufferView":0,"count":36,"max":[7],"min":[0]},{"componentType":5126,"type":"VEC3","byteOffset":0,"bufferView":1,"count":8,"max":[5.0,5.0,5.0],"min":[-5.0,-5.0,-5.0]}],"meshes":[{"name":"geometry_0","extras":{"shape":"box","extents":[10.0,10.0,10.0]},"primitives":[{"attributes":{"POSITION":1},"indices":0,"mode":4}]}],"nodes":[{"name":"world","children":[1]},{"name":"geometry_0","mesh":0}],"buffers":[{"uri":"data:application/octet-stream;base64,AQAAAAMAAAAAAAAABAAAAAEAAAAAAAAAAAAAAAMAAAACAAAAAgAAAAQAAAAAAAAAAQAAAAcAAAADAAAABQAAAAEAAAAEAAAABQAAAAcAAAABAAAAAwAAAAcAAAACAAAABgAAAAQAAAACAAAAAgAAAAcAAAAGAAAABgAAAAUAAAAEAAAABwAAAAUAAAAGAAAAAACgwAAAoMAAAKDAAACgwAAAoMAAAKBAAACgwAAAoEAAAKDAAACgwAAAoEAAAKBAAACgQAAAoMAAAKDAAACgQAAAoMAAAKBAAACgQAAAoEAAAKDAAACgQAAAoEAAAKBA","byteLength":240}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":144},{"buffer":0,"byteOffset":144,"byteLength":96}]}""";
    internal const string OriginalGlb = "Z2xURgIAAADAAwAAtAIAAEpTT057InNjZW5lIjowLCJzY2VuZXMiOlt7Im5vZGVzIjpbMF19XSwiYXNzZXQiOnsidmVyc2lvbiI6IjIuMCIsImdlbmVyYXRvciI6Imh0dHBzOi8vZ2l0aHViLmNvbS9taWtlZGgvdHJpbWVzaCJ9LCJhY2Nlc3NvcnMiOlt7ImNvbXBvbmVudFR5cGUiOjUxMjUsInR5cGUiOiJTQ0FMQVIiLCJidWZmZXJWaWV3IjowLCJjb3VudCI6MzYsIm1heCI6WzddLCJtaW4iOlswXX0seyJjb21wb25lbnRUeXBlIjo1MTI2LCJ0eXBlIjoiVkVDMyIsImJ5dGVPZmZzZXQiOjAsImJ1ZmZlclZpZXciOjEsImNvdW50Ijo4LCJtYXgiOls1LjAsNS4wLDUuMF0sIm1pbiI6Wy01LjAsLTUuMCwtNS4wXX1dLCJtZXNoZXMiOlt7Im5hbWUiOiJnZW9tZXRyeV8wIiwiZXh0cmFzIjp7InNoYXBlIjoiYm94IiwiZXh0ZW50cyI6WzEwLjAsMTAuMCwxMC4wXX0sInByaW1pdGl2ZXMiOlt7ImF0dHJpYnV0ZXMiOnsiUE9TSVRJT04iOjF9LCJpbmRpY2VzIjowLCJtb2RlIjo0fV19XSwibm9kZXMiOlt7Im5hbWUiOiJ3b3JsZCIsImNoaWxkcmVuIjpbMV19LHsibmFtZSI6Imdlb21ldHJ5XzAiLCJtZXNoIjowfV0sImJ1ZmZlcnMiOlt7ImJ5dGVMZW5ndGgiOjI0MH1dLCJidWZmZXJWaWV3cyI6W3siYnVmZmVyIjowLCJieXRlT2Zmc2V0IjowLCJieXRlTGVuZ3RoIjoxNDR9LHsiYnVmZmVyIjowLCJieXRlT2Zmc2V0IjoxNDQsImJ5dGVMZW5ndGgiOjk2fV19IPAAAABCSU4AAQAAAAMAAAAAAAAABAAAAAEAAAAAAAAAAAAAAAMAAAACAAAAAgAAAAQAAAAAAAAAAQAAAAcAAAADAAAABQAAAAEAAAAEAAAABQAAAAcAAAABAAAAAwAAAAcAAAACAAAABgAAAAQAAAACAAAAAgAAAAcAAAAGAAAABgAAAAUAAAAEAAAABwAAAAUAAAAGAAAAAACgwAAAoMAAAKDAAACgwAAAoMAAAKBAAACgwAAAoEAAAKDAAACgwAAAoEAAAKBAAACgQAAAoMAAAKDAAACgQAAAoMAAAKBAAACgQAAAoEAAAKDAAACgQAAAoEAAAKBA";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalCube_PreservesSourceMillimetresAndPrintingRotation(bool binary)
    {
        byte[] bytes = binary ? Convert.FromBase64String(OriginalGlb) : Encoding.UTF8.GetBytes(OriginalGltf);
        MeshInput mesh = AdmittedGltfMeshReader.Read(bytes);
        Assert.Equal(12, mesh.Triangles.Count);
        Assert.Equal(1, mesh.UnitsToMillimetres);
        Assert.Equal(Matrix4x4.Identity, mesh.ComponentTransform);
        Assert.Equal(new Vector3(-5, -5, -5), mesh.Triangles[0].A);
        Assert.Equal(new Vector3(-5, -5, 5), mesh.Triangles[0].B);
        Assert.Equal(new Vector3(-5, 5, -5), mesh.Triangles[0].C);
        Assert.Equal(Enumerable.Repeat(0, 12), mesh.PositiveFillGroups);
        NormalizedMesh normalized = MeshNormalizer.Normalize(mesh, new GeometryTolerance(0.001, 0.02));
        Assert.Empty(normalized.Diagnostics);
        Assert.Equal(new Vector3(10), Maximum(normalized.Triangles));
    }

    [Theory]
    [InlineData(5121)]
    [InlineData(5123)]
    [InlineData(5125)]
    public void IndexWidths_DecodeActualTriangles(int componentType)
    {
        var doc = Triangle(componentType);
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Fact]
    public void NonIndexedTriangle_UsesPositionOrder()
    {
        var doc = Triangle();
        Primitive(doc).Remove("indices");
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TriangleModes_KeepBothQuadFacesAndWinding(int mode)
    {
        float[] positions = [0, 0, 0, 2, 0, 0, 0, 3, 0, 2, 3, 0];
        uint[] indices = mode == 4 ? [0, 1, 2, 1, 3, 2] : mode == 5 ? [0, 1, 2, 3] : [0, 1, 3, 2];
        var doc = Model(positions, indices);
        Primitive(doc)["mode"] = mode;
        MeshInput mesh = Read(doc);
        Assert.Equal(2, mesh.Triangles.Count);
        Assert.All(mesh.Triangles, t => Assert.True(Vector3.Cross(t.B - t.A, t.C - t.A).Y < 0));
    }

    [Fact]
    public void StridedPositions_HonorBothOffsetsWithoutReadingPadding()
    {
        var doc = Triangle();
        byte[] data = new byte[4 + 3 * 16];
        for (int i = 0; i < 3; i++)
        {
            WriteFloat(data, 4 + i * 16, 999);
            WriteFloat(data, 8 + i * 16, i == 1 ? 2 : 0);
            WriteFloat(data, 12 + i * 16, i == 2 ? 3 : 0);
            WriteFloat(data, 16 + i * 16, 0);
        }
        SetBuffer(doc, data);
        doc["bufferViews"] = new JsonArray(new JsonObject { ["buffer"] = 0, ["byteOffset"] = 4, ["byteLength"] = 48, ["byteStride"] = 16 });
        doc["accessors"] = new JsonArray(new JsonObject { ["bufferView"] = 0, ["byteOffset"] = 4, ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC3" });
        Primitive(doc).Remove("indices");
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Theory]
    [InlineData(5120)]
    [InlineData(5121)]
    [InlineData(5122)]
    [InlineData(5123)]
    public void QuantizedNormalizedPosition_UsesLogicalComponents(int type)
    {
        byte[] data = type == 5120 ? [0, 0, 0, 127, 0, 0, 0, 127, 0]
            : type == 5121 ? [0, 0, 0, 255, 0, 0, 0, 255, 0]
            : type == 5122 ? [0, 0, 0, 0, 0, 0, 255, 127, 0, 0, 0, 0, 0, 0, 255, 127, 0, 0]
            : [0, 0, 0, 0, 0, 0, 255, 255, 0, 0, 0, 0, 0, 0, 255, 255, 0, 0];
        var doc = Triangle();
        SetBuffer(doc, data);
        doc["extensionsUsed"] = new JsonArray("KHR_mesh_quantization");
        doc["bufferViews"] = new JsonArray(new JsonObject { ["buffer"] = 0, ["byteLength"] = data.Length });
        doc["accessors"] = new JsonArray(new JsonObject { ["bufferView"] = 0, ["componentType"] = type, ["normalized"] = true, ["count"] = 3, ["type"] = "VEC3" });
        Primitive(doc).Remove("indices");
        AssertTriangle(Read(doc), Vector3.Zero, new(1, 0, 0), new(0, 0, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SparsePosition_OverridesBaseOrZeroInitializedAccessor(bool hasBase)
    {
        var doc = Triangle();
        byte[] data = new byte[64];
        if (hasBase) WriteFloat(data, 12, 99);
        data[36] = 1; data[37] = 2;
        WriteFloat(data, 40, 2); WriteFloat(data, 44, 0); WriteFloat(data, 48, 0);
        WriteFloat(data, 52, 0); WriteFloat(data, 56, 3); WriteFloat(data, 60, 0);
        SetBuffer(doc, data);
        doc["bufferViews"] = new JsonArray(
            new JsonObject { ["buffer"] = 0, ["byteLength"] = 36 },
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = 36, ["byteLength"] = 2 },
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = 40, ["byteLength"] = 24 });
        var accessor = new JsonObject { ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC3",
            ["sparse"] = new JsonObject { ["count"] = 2, ["indices"] = new JsonObject { ["bufferView"] = 1, ["componentType"] = 5121 },
                ["values"] = new JsonObject { ["bufferView"] = 2 } } };
        if (hasBase) accessor["bufferView"] = 0;
        doc["accessors"] = new JsonArray(accessor);
        Primitive(doc).Remove("indices");
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Fact]
    public void SelectedScene_ExcludesOtherSceneButDefaultsToZeroWhenAbsent()
    {
        var doc = Triangle();
        doc["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0 }, new JsonObject { ["mesh"] = 0, ["translation"] = new JsonArray(8, 0, 0) });
        doc["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }, new JsonObject { ["nodes"] = new JsonArray(1) });
        doc["scene"] = 1;
        AssertTriangle(Read(doc), new(8, 0, 0), new(10, 0, 0), new(8, 0, 3));
        doc.Remove("scene");
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Fact]
    public void NestedTrsAndRepeatedMesh_KeepIndependentWorldGeometryAndGroups()
    {
        var doc = Triangle();
        doc["nodes"] = new JsonArray(
            new JsonObject { ["translation"] = new JsonArray(10, 0, 0), ["scale"] = new JsonArray(2, 1, 1), ["children"] = new JsonArray(1) },
            new JsonObject { ["mesh"] = 0, ["translation"] = new JsonArray(1, 0, 0) },
            new JsonObject { ["mesh"] = 0, ["translation"] = new JsonArray(-4, 0, 0) });
        doc["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0, 2) });
        MeshInput mesh = Read(doc);
        Assert.Equal(new Vector3(12, 0, 0), mesh.Triangles[0].A);
        Assert.Equal(new Vector3(16, 0, 0), mesh.Triangles[0].B);
        Assert.Equal(new Vector3(-4, 0, 0), mesh.Triangles[1].A);
        Assert.Equal(new[] { 0, 1 }, mesh.PositiveFillGroups);
    }

    [Fact]
    public void MatrixShear_IsNotLostThroughTrsDecomposition()
    {
        var doc = Triangle();
        doc["nodes"]![0]!["matrix"] = new JsonArray(1, 0, 0, 0, 2, 1, 0, 0, 0, 0, 1, 0, 4, 5, 6, 1);
        AssertTriangle(Read(doc), new(4, -6, 5), new(6, -6, 5), new(10, -6, 8));
    }

    [Fact]
    public void Reflection_RewindsExpandedTriangleForPositiveFill()
    {
        var doc = Triangle();
        doc["nodes"]![0]!["scale"] = new JsonArray(-1, 1, 1);
        AssertTriangle(Read(doc), Vector3.Zero, new(0, 0, 3), new(-2, 0, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultMorph_UsesNodeOverrideOrMeshWeights(bool nodeOverride)
    {
        var doc = Triangle();
        AppendAttribute(doc, new float[] { 0, 0, 4, 0, 0, 4, 0, 0, 4 }, "VEC3");
        Primitive(doc)["targets"] = new JsonArray(new JsonObject { ["POSITION"] = 2 });
        doc["meshes"]![0]!["weights"] = new JsonArray(0.5);
        if (nodeOverride) doc["nodes"]![0]!["weights"] = new JsonArray(1);
        float expectedZ = nodeOverride ? -4 : -2;
        AssertTriangle(Read(doc), new(0, expectedZ, 0), new(2, expectedZ, 0), new(0, expectedZ, 3));
    }

    [Fact]
    public void InitialSkin_UsesJointWorldAndInverseBindInsteadOfBasePositions()
    {
        var doc = Triangle();
        AppendAttribute(doc, new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "VEC4", 5121);
        AppendAttribute(doc, new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }, "VEC4");
        AppendAttribute(doc, new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 1 }, "MAT4");
        Primitive(doc)["attributes"]!["JOINTS_0"] = 2;
        Primitive(doc)["attributes"]!["WEIGHTS_0"] = 3;
        doc["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0, ["skin"] = 0 }, new JsonObject { ["translation"] = new JsonArray(5, 0, 0) });
        doc["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0, 1) });
        doc["skins"] = new JsonArray(new JsonObject { ["joints"] = new JsonArray(1), ["inverseBindMatrices"] = 4 });
        AssertTriangle(Read(doc), new(4, 0, 0), new(6, 0, 0), new(4, 0, 3));
    }

    [Theory]
    [InlineData("http://example.invalid/private.bin")]
    [InlineData("https://127.0.0.1/private.bin")]
    [InlineData("file:///private.bin")]
    [InlineData("blob:opaque")]
    [InlineData("../buffer.bin")]
    [InlineData("/buffer.bin")]
    public void ExternalBuffer_NeverBecomesServerGeometry(string uri)
    {
        var doc = Triangle();
        doc["buffers"]![0]!["uri"] = uri;
        Assert.Throws<FormatException>(() => Read(doc));
    }

    [Theory]
    [InlineData("KHR_draco_mesh_compression")]
    [InlineData("EXT_meshopt_compression")]
    [InlineData("UNKNOWN_geometry")]
    public void UnsupportedRequiredExtension_FailsClosed(string name)
    {
        var doc = Triangle();
        doc["extensionsRequired"] = new JsonArray(name);
        Assert.Throws<FormatException>(() => Read(doc));
    }

    [Fact]
    public void OptionalMeshopt_WithOrdinaryFallbackKeepsCoreGeometry()
    {
        var doc = Triangle();
        doc["extensionsUsed"] = new JsonArray("EXT_meshopt_compression");
        doc["bufferViews"]![0]!["extensions"] = new JsonObject { ["EXT_meshopt_compression"] = new JsonObject { ["buffer"] = 0, ["count"] = 3, ["byteStride"] = 12 } };
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Theory]
    [InlineData("index")]
    [InlineData("offset")]
    [InlineData("stride")]
    [InlineData("count")]
    [InlineData("cycle")]
    [InlineData("parent")]
    [InlineData("scene")]
    [InlineData("nan")]
    [InlineData("version")]
    [InlineData("matrix-and-trs")]
    public void InvalidGeometryOrGraph_RejectsInsteadOfReturningPartialMesh(string kind)
    {
        var doc = Triangle();
        switch (kind)
        {
            case "index": doc["accessors"]![1]!["count"] = 4; break;
            case "offset": doc["accessors"]![0]!["byteOffset"] = int.MaxValue; break;
            case "stride": doc["bufferViews"]![0]!["byteStride"] = 8; break;
            case "count": doc["accessors"]![0]!["count"] = int.MaxValue; break;
            case "cycle": doc["nodes"]![0]!["children"] = new JsonArray(0); break;
            case "parent": doc["scenes"]![0]!["nodes"] = new JsonArray(0, 0); break;
            case "scene": doc["scene"] = 8; break;
            case "nan": { var bytes = Convert.FromBase64String(doc["buffers"]![0]!["uri"]!.GetValue<string>().Split(',')[1]); WriteFloat(bytes, 0, float.NaN); SetBuffer(doc, bytes); break; }
            case "version": doc["asset"]!["version"] = "1.0"; break;
            case "matrix-and-trs": doc["nodes"]![0]!["matrix"] = new JsonArray(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1); doc["nodes"]![0]!["translation"] = new JsonArray(1, 0, 0); break;
        }
        Assert.Throws<FormatException>(() => Read(doc));
    }

    [Theory]
    [InlineData("length")]
    [InlineData("version")]
    [InlineData("chunk")]
    [InlineData("truncated")]
    public void MalformedGlb_RejectsContainerBeforeGeometry(string kind)
    {
        byte[] bytes = Convert.FromBase64String(OriginalGlb);
        if (kind == "length") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 959);
        if (kind == "version") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
        if (kind == "chunk") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), uint.MaxValue);
        if (kind == "truncated") bytes = bytes[..^1];
        Assert.Throws<FormatException>(() => AdmittedGltfMeshReader.Read(bytes));
    }

    [Fact]
    public void ExpansionLimit_RejectsManyInstancesWithoutPartialResult()
    {
        var doc = Triangle();
        var nodes = new JsonArray();
        var roots = new JsonArray();
        for (int i = 0; i < 257; i++) { nodes.Add(new JsonObject { ["mesh"] = 0 }); roots.Add(i); }
        doc["nodes"] = nodes;
        doc["scenes"]![0]!["nodes"] = roots;
        Assert.Throws<FormatException>(() => Read(doc));
    }

    [Fact]
    public void CanceledOrOversizedInput_DoesNotParse()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AdmittedGltfMeshReader.Read(Encoding.UTF8.GetBytes(OriginalGltf), cancellation.Token));
        Assert.Throws<FormatException>(() => AdmittedGltfMeshReader.Read(new byte[(8 * 1024 * 1024) + 1]));
    }

    [Fact]
    public void BuffersAndAccessors_ResolveDifferentEmbeddedBuffers()
    {
        var doc = Triangle();
        byte[] original = Convert.FromBase64String(doc["buffers"]![0]!["uri"]!.GetValue<string>().Split(',')[1]);
        SetBuffer(doc, original[..36]);
        doc["buffers"]!.AsArray().Add(new JsonObject { ["byteLength"] = 12, ["uri"] = "data:application/gltf-buffer;base64," + Convert.ToBase64String(original[36..]) });
        doc["bufferViews"]![1]!["buffer"] = 1;
        doc["bufferViews"]![1]!["byteOffset"] = 0;
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Fact]
    public void PercentEncodedDataUri_DecodesOnlyEmbeddedBytes()
    {
        var doc = Triangle();
        string uri = doc["buffers"]![0]!["uri"]!.GetValue<string>();
        doc["buffers"]![0]!["uri"] = uri.Replace("A", "%41", StringComparison.Ordinal);
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Fact]
    public void NormalizedSignedMinimum_ClampsToMinusOne()
    {
        var doc = Triangle();
        byte[] bytes = [0, 0, 0, 128, 0, 0, 0, 127, 0];
        SetBuffer(doc, bytes);
        doc["extensionsUsed"] = new JsonArray("KHR_mesh_quantization");
        doc["bufferViews"] = new JsonArray(new JsonObject { ["buffer"] = 0, ["byteLength"] = 9 });
        doc["accessors"] = new JsonArray(new JsonObject { ["bufferView"] = 0, ["componentType"] = 5120, ["normalized"] = true, ["count"] = 3, ["type"] = "VEC3" });
        Primitive(doc).Remove("indices");
        AssertTriangle(Read(doc), Vector3.Zero, new(-1, 0, 0), new(0, 0, 1));
    }

    [Fact]
    public void MultiplePrimitivesOfOneInstance_UseOneSolidGroup()
    {
        var doc = Triangle();
        doc["meshes"]![0]!["primitives"]!.AsArray().Add(Primitive(doc).DeepClone());
        MeshInput mesh = Read(doc);
        Assert.Equal(2, mesh.Triangles.Count);
        Assert.Equal(new[] { 0, 0 }, mesh.PositiveFillGroups);
    }

    [Theory]
    [InlineData("uri-size")]
    [InlineData("view-length")]
    [InlineData("view-buffer")]
    [InlineData("normalized-index")]
    [InlineData("sparse-duplicate")]
    [InlineData("sparse-outside")]
    [InlineData("two-parents")]
    [InlineData("unknown-joint")]
    [InlineData("zero-weights")]
    public void MalformedOwnedData_RejectsBeforeReturningGeometry(string kind)
    {
        var doc = Triangle();
        switch (kind)
        {
            case "uri-size": doc["buffers"]![0]!["byteLength"] = 47; break;
            case "view-length": doc["bufferViews"]![0]!["byteLength"] = int.MaxValue; break;
            case "view-buffer": doc["bufferViews"]![0]!["buffer"] = 7; break;
            case "normalized-index": doc["accessors"]![1]!["normalized"] = true; break;
            case "sparse-duplicate":
            case "sparse-outside":
                {
                    byte[] bytes = new byte[64];
                    bytes[36] = 1; bytes[37] = kind == "sparse-duplicate" ? (byte)1 : (byte)3;
                    SetBuffer(doc, bytes);
                    doc["bufferViews"] = new JsonArray(
                        new JsonObject { ["buffer"] = 0, ["byteLength"] = 36 },
                        new JsonObject { ["buffer"] = 0, ["byteOffset"] = 36, ["byteLength"] = 2 },
                        new JsonObject { ["buffer"] = 0, ["byteOffset"] = 40, ["byteLength"] = 24 });
                    doc["accessors"]![0]!["sparse"] = new JsonObject { ["count"] = 2,
                        ["indices"] = new JsonObject { ["bufferView"] = 1, ["componentType"] = 5121 },
                        ["values"] = new JsonObject { ["bufferView"] = 2 } };
                    Primitive(doc).Remove("indices");
                    break;
                }
            case "two-parents":
                doc["nodes"] = new JsonArray(new JsonObject { ["children"] = new JsonArray(2) },
                    new JsonObject { ["children"] = new JsonArray(2) }, new JsonObject { ["mesh"] = 0 });
                break;
            case "unknown-joint":
            case "zero-weights":
                AppendAttribute(doc, new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "VEC4", 5121);
                AppendAttribute(doc, kind == "zero-weights" ? new float[12] : new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }, "VEC4");
                Primitive(doc)["attributes"]!["JOINTS_0"] = 2;
                Primitive(doc)["attributes"]!["WEIGHTS_0"] = 3;
                doc["nodes"]![0]!["skin"] = 0;
                doc["skins"] = new JsonArray(new JsonObject { ["joints"] = new JsonArray(kind == "unknown-joint" ? 4 : 0) });
                break;
        }
        Assert.Throws<FormatException>(() => Read(doc));
    }

    [Fact]
    public void JsonDuplicateKeysAndExcessiveDepth_AreNotSilentlyAccepted()
    {
        Assert.Throws<FormatException>(() => AdmittedGltfMeshReader.Read(Encoding.UTF8.GetBytes("""{"asset":{"version":"2.0","version":"1.0"}}""")));
        string deep = new string('[', 66) + "0" + new string(']', 66);
        Assert.Throws<FormatException>(() => AdmittedGltfMeshReader.Read(Encoding.UTF8.GetBytes(deep)));
        Assert.Throws<FormatException>(() => AdmittedGltfMeshReader.Read([0xff, 0xfe]));
    }

    [Fact]
    public void GlbUnknownTrailingChunk_DoesNotAlterOriginalGeometry()
    {
        byte[] original = Convert.FromBase64String(OriginalGlb);
        byte[] bytes = new byte[original.Length + 12];
        original.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(original.Length), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(original.Length + 4), 0x12345678);
        Assert.Equal(12, AdmittedGltfMeshReader.Read(bytes).Triangles.Count);
    }

    [Fact]
    public void UnusedVertexExpansion_IsStillChargedToOwnedBudget()
    {
        var doc = Model(new float[150_001 * 3], [0, 1, 2]);
        Assert.Throws<FormatException>(() => Read(doc));
    }

    [Fact]
    public void ZeroNodeMorphWeight_OverridesNonzeroMeshDefault()
    {
        var doc = Triangle();
        AppendAttribute(doc, new float[] { 0, 0, 4, 0, 0, 4, 0, 0, 4 }, "VEC3");
        Primitive(doc)["targets"] = new JsonArray(new JsonObject { ["POSITION"] = 2 });
        doc["meshes"]![0]!["weights"] = new JsonArray(1);
        doc["nodes"]![0]!["weights"] = new JsonArray(0);
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Fact]
    public void InitialSkin_OmittedInverseBindUsesIdentityAndDoesNotDoubleApplyMeshTransform()
    {
        var doc = Triangle();
        AppendAttribute(doc, new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "VEC4", 5121);
        AppendAttribute(doc, new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }, "VEC4");
        Primitive(doc)["attributes"]!["JOINTS_0"] = 2;
        Primitive(doc)["attributes"]!["WEIGHTS_0"] = 3;
        doc["nodes"] = new JsonArray(
            new JsonObject { ["mesh"] = 0, ["skin"] = 0, ["translation"] = new JsonArray(100, 0, 0) },
            new JsonObject { ["translation"] = new JsonArray(5, 0, 0) });
        doc["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0, 1) });
        doc["skins"] = new JsonArray(new JsonObject { ["joints"] = new JsonArray(1) });
        AssertTriangle(Read(doc), new(5, 0, 0), new(7, 0, 0), new(5, 0, 3));
    }

    [Fact]
    public void NonSurfacePrimitive_DoesNotAddPhysicalTriangles()
    {
        var doc = Triangle();
        var points = Primitive(doc).DeepClone().AsObject();
        points["mode"] = 0;
        doc["meshes"]![0]!["primitives"]!.AsArray().Insert(0, points);
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Fact]
    public void ParentQuaternion_ComposesAfterScaleAndBeforeTranslation()
    {
        var doc = Triangle();
        doc["nodes"] = new JsonArray(
            new JsonObject { ["translation"] = new JsonArray(10, 0, 0), ["scale"] = new JsonArray(2, 1, 1),
                ["rotation"] = new JsonArray(0, 0, 0.7071067811865476, 0.7071067811865476), ["children"] = new JsonArray(1) },
            new JsonObject { ["mesh"] = 0, ["translation"] = new JsonArray(1, 0, 0) });
        AssertTriangle(Read(doc), new(10, 0, 2), new(10, 0, 6), new(7, 0, 2));
    }

    [Theory]
    [InlineData(5123)]
    [InlineData(5125)]
    public void SparseWideIndices_ApplyOverridesBeyondByteIndexRange(int type)
    {
        var doc = Triangle();
        int width = type == 5123 ? 2 : 4, start = 12 + 2 * width;
        byte[] bytes = new byte[start + 24];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 256);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 257);
        if (width == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 256);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 257);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 256);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 257);
        }
        WriteFloat(bytes, start, 2);
        WriteFloat(bytes, start + 16, 3);
        SetBuffer(doc, bytes);
        doc["bufferViews"] = new JsonArray(
            new JsonObject { ["buffer"] = 0, ["byteLength"] = 12 },
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = 12, ["byteLength"] = 2 * width },
            new JsonObject { ["buffer"] = 0, ["byteOffset"] = start, ["byteLength"] = 24 });
        doc["accessors"] = new JsonArray(
            new JsonObject { ["componentType"] = 5126, ["count"] = 258, ["type"] = "VEC3",
                ["sparse"] = new JsonObject { ["count"] = 2, ["indices"] = new JsonObject { ["bufferView"] = 1, ["componentType"] = type },
                    ["values"] = new JsonObject { ["bufferView"] = 2 } } },
            new JsonObject { ["bufferView"] = 0, ["componentType"] = 5125, ["count"] = 3, ["type"] = "SCALAR" });
        AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    [Theory]
    [InlineData(4, 33, false)]
    [InlineData(4, 34, true)]
    [InlineData(5, 33, false)]
    [InlineData(5, 34, true)]
    [InlineData(6, 33, false)]
    [InlineData(6, 34, true)]
    public void ReusedDegenerateIndices_ChargeEveryCandidateTriangleBeforeExpansion(int mode, int repetitions, bool exceedsBudget)
    {
        var doc = Triangle();
        byte[] original = Convert.FromBase64String(doc["buffers"]![0]!["uri"]!.GetValue<string>().Split(',')[1]);
        int indexCount = mode == 4 ? 4500 : 1502;
        byte[] bytes = new byte[original.Length + indexCount];
        original.CopyTo(bytes, 0);
        SetBuffer(doc, bytes);
        doc["bufferViews"]!.AsArray().Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = original.Length, ["byteLength"] = indexCount });
        doc["accessors"]!.AsArray().Add(new JsonObject { ["bufferView"] = 2, ["componentType"] = 5121, ["count"] = indexCount, ["type"] = "SCALAR" });
        for (int i = 0; i < repetitions; i++)
        {
            var degenerate = Primitive(doc).DeepClone().AsObject();
            degenerate["indices"] = 2;
            degenerate["mode"] = mode;
            doc["meshes"]![0]!["primitives"]!.AsArray().Add(degenerate);
        }

        // The first real triangle makes the old output-only guard return a mesh, not reject an empty result.
        // Each repeated cached accessor costs 1,500 candidates even though every index is zero.
        if (exceedsBudget) Assert.Throws<FormatException>(() => Read(doc));
        else AssertTriangle(Read(doc), Vector3.Zero, new(2, 0, 0), new(0, 0, 3));
    }

    private static MeshInput Read(JsonObject doc) => AdmittedGltfMeshReader.Read(Encoding.UTF8.GetBytes(doc.ToJsonString()));

    private static void AssertTriangle(MeshInput mesh, Vector3 a, Vector3 b, Vector3 c)
    {
        MeshTriangle triangle = Assert.Single(mesh.Triangles);
        Assert.True(Vector3.Distance(a, triangle.A) < 0.00001f);
        Assert.True(Vector3.Distance(b, triangle.B) < 0.00001f);
        Assert.True(Vector3.Distance(c, triangle.C) < 0.00001f);
        Assert.Equal(new[] { 0 }, mesh.PositiveFillGroups);
    }

    private static Vector3 Maximum(IReadOnlyList<MeshTriangle> triangles) =>
        triangles.SelectMany(t => new[] { t.A, t.B, t.C }).Aggregate(Vector3.Zero, Vector3.Max);

    private static JsonObject Primitive(JsonObject doc) => doc["meshes"]![0]!["primitives"]![0]!.AsObject();

    private static JsonObject Triangle(int componentType = 5125) => Model([0, 0, 0, 2, 0, 0, 0, 3, 0], [0, 1, 2], componentType);

    private static JsonObject Model(float[] positions, uint[] indices, int componentType = 5125)
    {
        int width = componentType == 5121 ? 1 : componentType == 5123 ? 2 : 4;
        byte[] bytes = new byte[positions.Length * 4 + indices.Length * width];
        for (int i = 0; i < positions.Length; i++) WriteFloat(bytes, i * 4, positions[i]);
        for (int i = 0; i < indices.Length; i++)
        {
            int offset = positions.Length * 4 + i * width;
            if (width == 1) bytes[offset] = checked((byte)indices[i]);
            else if (width == 2) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), checked((ushort)indices[i]));
            else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), indices[i]);
        }
        var doc = JsonNode.Parse("""{"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}]}""")!.AsObject();
        SetBuffer(doc, bytes);
        doc["bufferViews"] = new JsonArray(new JsonObject { ["buffer"] = 0, ["byteLength"] = positions.Length * 4 }, new JsonObject { ["buffer"] = 0, ["byteOffset"] = positions.Length * 4, ["byteLength"] = indices.Length * width });
        doc["accessors"] = new JsonArray(
            new JsonObject { ["bufferView"] = 0, ["componentType"] = 5126, ["count"] = positions.Length / 3, ["type"] = "VEC3" },
            new JsonObject { ["bufferView"] = 1, ["componentType"] = componentType, ["count"] = indices.Length, ["type"] = "SCALAR" });
        return doc;
    }

    private static void AppendAttribute(JsonObject doc, float[] values, string shape, int type = 5126)
    {
        byte[] old = Convert.FromBase64String(doc["buffers"]![0]!["uri"]!.GetValue<string>().Split(',')[1]);
        int padding = (4 - old.Length % 4) % 4, start = old.Length + padding;
        int width = type == 5121 ? 1 : 4;
        var bytes = new byte[start + values.Length * width];
        old.CopyTo(bytes, 0);
        for (int i = 0; i < values.Length; i++)
            if (type == 5121) bytes[start + i] = checked((byte)values[i]);
            else WriteFloat(bytes, start + i * 4, values[i]);
        SetBuffer(doc, bytes);
        int view = doc["bufferViews"]!.AsArray().Count;
        doc["bufferViews"]!.AsArray().Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = start, ["byteLength"] = values.Length * width });
        doc["accessors"]!.AsArray().Add(new JsonObject { ["bufferView"] = view, ["componentType"] = type, ["type"] = shape, ["count"] = values.Length / (shape == "MAT4" ? 16 : shape == "VEC4" ? 4 : 3) });
    }

    private static void SetBuffer(JsonObject doc, byte[] bytes) =>
        doc["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = bytes.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bytes) });

    private static void WriteFloat(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), BitConverter.SingleToInt32Bits(value));

}
