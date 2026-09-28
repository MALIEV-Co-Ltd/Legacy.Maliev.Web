using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationAdmittedMeshServiceTests
{
    private const string SessionId = "owner-bound-session";
    private const string Owner = "member-42";
    private static readonly Guid PartId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task CleanAdmittedBox_ProducesUploadBoundMeshForTrustedPhysicalSimulation()
    {
        byte[] bytes = BinaryBox(8);
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        var reader = new RecordingInputReader(Ready(bytes, digest));
        var service = new InstantQuotationAdmittedMeshService(reader);

        var result = await service.ReadAsync(SessionId, Owner, PartId, FileId, digest, default);

        Assert.True(result.IsReady);
        Assert.Equal(digest, result.UploadSha256);
        Assert.Equal(12, result.Mesh!.Triangles.Count);
        Assert.Equal((SessionId, Owner, PartId), reader.LastRequest);
        Assert.True(FdmRuntimeProfileCatalog.LoadEmbedded().TryResolveTrustedProfile(
            "PLA", BuildPreference.Standard, out var profile));
        var physical = FdmSimulationEngine.Estimate(new SimulationRequest(
            result.Mesh,
            new Pose("identity", Matrix4x4.Identity),
            profile!,
            1,
            new AnalysisBudget(100, 1000, 100_000, default)));
        Assert.Equal(FdmSimulationEngine.AnalysisVersion, physical.AnalysisVersion);
        Assert.Equal(profile!.ResolvedProfileSha256, physical.ProfileSha256);
        Assert.True(physical.Motion.TotalSeconds > 0);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("descriptor")]
    [InlineData("content")]
    [InlineData("format")]
    public async Task ChangedUploadOrUnsupportedFormat_NeverProducesMesh(string alteration)
    {
        byte[] bytes = BinaryBox(8);
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        var response = Ready(bytes, digest);
        response = alteration switch
        {
            "file" => response with { FileId = Guid.NewGuid() },
            "descriptor" => response with { Sha256 = new string('A', 64) },
            "content" => response with { Content = BinaryBox(9) },
            "format" => response with { FileName = "part.obj" },
            _ => throw new InvalidOperationException(),
        };
        var service = new InstantQuotationAdmittedMeshService(new RecordingInputReader(response));

        var result = await service.ReadAsync(SessionId, Owner, PartId, FileId, digest, default);

        Assert.False(result.IsReady);
        Assert.Null(result.Mesh);
        Assert.Equal(
            alteration == "format" ? InstantQuotationAdmittedMeshFailure.UnsupportedFormat : InstantQuotationAdmittedMeshFailure.UploadChanged,
            result.Failure);
    }

    [Fact]
    public async Task UnavailableReaderAndMalformedMesh_FailClosed()
    {
        byte[] bytes = BinaryBox(8);
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        var unavailable = new InstantQuotationAdmittedMeshService(new RecordingInputReader(
            InstantQuotationPhysicalAnalysisInputResult.Unavailable(InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable)));
        var malformed = new InstantQuotationAdmittedMeshService(new RecordingInputReader(
            Ready(Encoding.UTF8.GetBytes("solid broken\nendsolid broken"), digest: null)));

        Assert.Equal(InstantQuotationAdmittedMeshFailure.UploadUnavailable,
            (await unavailable.ReadAsync(SessionId, Owner, PartId, FileId, digest, default)).Failure);
        byte[] malformedBytes = Encoding.UTF8.GetBytes("solid broken\nendsolid broken");
        string malformedDigest = Convert.ToHexString(SHA256.HashData(malformedBytes));
        Assert.Equal(InstantQuotationAdmittedMeshFailure.InvalidMesh,
            (await malformed.ReadAsync(SessionId, Owner, PartId, FileId, malformedDigest, default)).Failure);
    }

    [Fact]
    public async Task CancelledRequest_DoesNotReadCustomerBytes()
    {
        var reader = new RecordingInputReader(Ready(BinaryBox(8)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InstantQuotationAdmittedMeshService(reader).ReadAsync(
                SessionId, Owner, PartId, FileId, new string('A', 64), cancellation.Token));
        Assert.Null(reader.LastRequest);
    }

    [Fact]
    public void Parser_AcceptsAsciiAndRejectsOversizedOrPartialBinaryInput()
    {
        byte[] ascii = Encoding.UTF8.GetBytes(
            "solid part\n facet normal 0 0 1\n outer loop\n vertex 0 0 0\n vertex 1 0 0\n vertex 0 1 0\n endloop\n endfacet\nendsolid part\n");
        Assert.Single(AdmittedStlMeshReader.Read(ascii).Triangles);
        Assert.Throws<FormatException>(() => AdmittedStlMeshReader.Read(new byte[AdmittedStlMeshReader.MaximumBytes + 1]));
        Assert.Throws<FormatException>(() => AdmittedStlMeshReader.Read(BinaryBox(8)[..^1]));
        byte[] tooManyTriangles = new byte[84 + ((AdmittedStlMeshReader.MaximumTriangles + 1) * 50)];
        BitConverter.GetBytes((uint)(AdmittedStlMeshReader.MaximumTriangles + 1)).CopyTo(tooManyTriangles, 80);
        Assert.Throws<FormatException>(() => AdmittedStlMeshReader.Read(tooManyTriangles));
        Assert.Throws<FormatException>(() => AdmittedStlMeshReader.Read(
            Encoding.UTF8.GetBytes("solid fake\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendsolid fake")));
        Assert.Throws<FormatException>(() => AdmittedStlMeshReader.Read(
            Encoding.UTF8.GetBytes("solid fake\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nendloop\nendfacet\nendsolid fake")));
    }

    [Fact]
    public async Task CancellationAfterCleanRead_StopsMeshParsingBeforeNormalization()
    {
        byte[] bytes = BinaryBox(8);
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        using var cancellation = new CancellationTokenSource();
        var reader = new RecordingInputReader(Ready(bytes, digest), cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new InstantQuotationAdmittedMeshService(reader).ReadAsync(
                SessionId, Owner, PartId, FileId, digest, cancellation.Token));
        Assert.NotNull(reader.LastRequest);
    }

    private static InstantQuotationPhysicalAnalysisInputResult Ready(byte[] bytes, string? digest = null) =>
        new(bytes, InstantQuotationPhysicalAnalysisInputFailure.None, FileId, "part.stl",
            digest ?? Convert.ToHexString(SHA256.HashData(bytes)));

    private static byte[] BinaryBox(float size)
    {
        Vector3[] points =
        [
            new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0),
            new(0, 0, size), new(size, 0, size), new(size, size, size), new(0, size, size),
        ];
        int[][] faces =
        [
            [0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7],
            [0, 1, 5], [0, 5, 4], [1, 2, 6], [1, 6, 5],
            [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7],
        ];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write((uint)faces.Length);
        foreach (int[] face in faces)
        {
            writer.Write(new byte[12]);
            foreach (int vertex in face)
            {
                writer.Write(points[vertex].X);
                writer.Write(points[vertex].Y);
                writer.Write(points[vertex].Z);
            }

            writer.Write((ushort)0);
        }

        return stream.ToArray();
    }

    private sealed class RecordingInputReader(
        InstantQuotationPhysicalAnalysisInputResult response,
        Action? onRead = null)
        : IInstantQuotationPhysicalAnalysisInputReader
    {
        public (string SessionId, string? Owner, Guid PartId)? LastRequest { get; private set; }

        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            LastRequest = (sessionId, ownerIdentity, partId);
            onRead?.Invoke();
            return Task.FromResult(response);
        }
    }
}
