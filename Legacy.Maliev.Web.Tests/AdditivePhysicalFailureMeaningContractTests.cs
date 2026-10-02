using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real server parser/engine over controlled bytes; not FileService admission proof.</summary>
public sealed class AdditivePhysicalFailureMeaningContractTests
{
    private static readonly Guid PartId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Session = "synthetic-failure-session";
    private const string Owner = "synthetic-failure-owner";

    [Theory]
    [InlineData("open", "GeometryReview")]
    [InlineData("layers", "ComplexityExceeded")]
    [InlineData("malformed", "SimulationUnavailable")]
    public async Task ActualInputFailureRetainsFixedActionableMeaningWithoutPhysicalEvidence(string input, string expected)
    {
        var bytes = input switch
        {
            "open" => Box(8, omitLastFace: true),
            "layers" => Box(1000), // 1000mm / qualified PLA layer height exceeds the fixed 2000-layer budget.
            _ => Encoding.ASCII.GetBytes("solid incomplete\nfacet normal 0 0 1\n"),
        };
        var fixture = Create(bytes);

        var result = await fixture.Service.AnalyzeAsync(fixture.Binding, default);

        Assert.False(result.IsReady);
        Assert.Null(result.Physical);
        Assert.Null(result.Binding);
        // Fixed semantic contract over an actual result, not source text or a missing symbol.
        // Existing enums currently compile; runtime approval may add these precise categories.
        Assert.Equal(expected, result.Failure.ToString());
        Assert.Equal(1, fixture.Reader.Reads);
        Assert.Equal((Session, Owner, PartId), fixture.Reader.LastRequest);
        Assert.Null(fixture.Store.State.QuoteAuthorization);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task OrdinaryClosedBoxStillProducesBoundPhysicalEvidence()
    {
        var fixture = Create(Box(8));
        var result = await fixture.Service.AnalyzeAsync(fixture.Binding, default);
        Assert.True(result.IsReady);
        Assert.NotNull(result.Physical);
        Assert.Equal(fixture.Binding, result.Binding);
        Assert.Null(fixture.Store.State.QuoteAuthorization);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task StalePartRemainsStaleRatherThanBeingMisclassifiedAsGeometryFailure()
    {
        var fixture = Create(Box(8, omitLastFace: true));
        var result = await fixture.Service.AnalyzeAsync(fixture.Binding with { PartId = Guid.NewGuid() }, default);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.StalePart, result.Failure);
        Assert.False(result.IsReady);
        Assert.Null(result.Physical);
        Assert.Equal(0, fixture.Reader.Reads);
        Assert.Equal(0, fixture.Store.Writes);
    }

    [Fact]
    public async Task CallerCancellationWinsBeforeMalformedByteRead()
    {
        var fixture = Create(Encoding.ASCII.GetBytes("not an STL"));
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.AnalyzeAsync(fixture.Binding, caller.Token));
        Assert.Equal(caller.Token, canceled.CancellationToken);
        Assert.Equal(0, fixture.Reader.Reads);
        Assert.Equal(0, fixture.Store.Writes);
    }

    private static (InstantQuotationBoundPhysicalAnalysisService Service,
        InstantQuotationPhysicalAnalysisBinding Binding, ReadOnlyStore Store, ControlledByteReader Reader) Create(byte[] bytes)
    {
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var reference = new InstantQuotationUploadReference(FileId.ToString("D"));
        // Synthetic historic envelope only; actual parser/engine consumes bytes, never this claim as physics.
        var claim = new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
            Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
            12, 1, true, false, false, 1);
        var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
            InstantQuotationUploadResult.Succeeded("synthetic-operation", reference, digest), claim)!;
        var part = new InstantQuotationPart(PartId, "synthetic.stl", reference, geometry,
            new InstantQuotationPartConfiguration("PLA", "Black", 1),
            new InstantQuotationPhysicalAnalysisUpload(FileId, "synthetic.stl", "model/stl", bytes.Length, digest, "clean"));
        var now = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var store = new ReadOnlyStore(new InstantQuotationSessionState(Session, "synthetic-submission",
            new InstantQuotationOrderState([part]), now, now, OwnerIdentity: Owner));
        var reader = new ControlledByteReader(bytes, digest);
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();
        return (new InstantQuotationBoundPhysicalAnalysisService(store,
                new InstantQuotationAdmittedMeshService(reader), catalog),
            new InstantQuotationPhysicalAnalysisBinding(Session, Owner, PartId, FileId, digest,
                "PLA", BuildPreference.Standard, 1, catalog.ProfileVersion), store, reader);
    }

    private static byte[] Box(float height, bool omitLastFace = false)
    {
        Vector3[] vertices = [new(0, 0, 0), new(8, 0, 0), new(8, 8, 0), new(0, 8, 0),
            new(0, 0, height), new(8, 0, height), new(8, 8, height), new(0, 8, height)];
        int[][] faces = [[0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7], [0, 1, 5], [0, 5, 4],
            [1, 2, 6], [1, 6, 5], [2, 3, 7], [2, 7, 6], [3, 0, 4], [3, 4, 7]];
        var count = faces.Length - (omitLastFace ? 1 : 0);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write((uint)count);
        foreach (var face in faces.Take(count))
        {
            writer.Write(new byte[12]);
            foreach (var index in face)
            {
                writer.Write(vertices[index].X); writer.Write(vertices[index].Y); writer.Write(vertices[index].Z);
            }
            writer.Write((ushort)0);
        }
        return stream.ToArray();
    }

    private sealed class ReadOnlyStore(InstantQuotationSessionState state) : IInstantQuotationSessionStore
    {
        internal InstantQuotationSessionState State => state;
        internal int Writes { get; private set; }
        public Task<InstantQuotationSessionState?> GetAsync(string id, string? owner, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<InstantQuotationSessionState?>(id == Session && owner == Owner ? state : null);
        }
        public Task<bool> PutAsync(InstantQuotationSessionState value, string? owner, CancellationToken token)
        { Writes++; throw new InvalidOperationException("Physical analysis must not publish protected authority."); }
        public Task<InstantQuotationSessionState> CreateAsync(string? owner, InstantQuotationOrderState request, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<bool> RemoveAsync(string id, string? owner, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class ControlledByteReader(byte[] bytes, string digest) : IInstantQuotationPhysicalAnalysisInputReader
    {
        internal int Reads { get; private set; }
        internal (string, string?, Guid)? LastRequest { get; private set; }
        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(string id, string? owner, Guid part, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++; LastRequest = (id, owner, part);
            return Task.FromResult(new InstantQuotationPhysicalAnalysisInputResult(bytes,
                InstantQuotationPhysicalAnalysisInputFailure.None, FileId, "synthetic.stl", digest));
        }
    }
}
