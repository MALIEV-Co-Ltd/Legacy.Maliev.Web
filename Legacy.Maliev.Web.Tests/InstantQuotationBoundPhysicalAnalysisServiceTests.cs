using System.Numerics;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationBoundPhysicalAnalysisServiceTests
{
    private const string SessionId = "session-1";
    private const string Owner = "member-1";
    private static readonly Guid PartId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly byte[] Bytes = BinaryBox(8);
    private static readonly string Digest = Convert.ToHexStringLower(SHA256.HashData(Bytes));

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public async Task CurrentOwnerAndConfiguration_ProducesBoundPhysicalEvidenceWithoutPrice(int quantity)
    {
        var fixture = Fixture(quantity);

        InstantQuotationBoundPhysicalAnalysisResult result = await fixture.Service.AnalyzeAsync(
            fixture.Binding, default);

        Assert.True(result.IsReady);
        Assert.Equal(fixture.Binding, result.Binding);
        Assert.Equal(Digest, result.Binding!.UploadSha256);
        Assert.Equal(FdmRuntimeProfileCatalog.LoadEmbedded().ProfileVersion, result.Binding.ProfileVersion);
        Assert.True(result.Physical!.Motion.TotalSeconds > 0);
        Assert.Equal(1, fixture.Reader.ReadCount);
        Assert.Equal(2, fixture.Store.ReadCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public async Task CurrentServerPhysicalEvidence_PricesSelectedStlAndBindsReceipt(int quantity)
    {
        var fixture = Fixture(quantity);
        var evidence = await fixture.Service.AnalyzeAsync(fixture.Binding, default);
        var part = Assert.Single(fixture.Store.State.Parts);
        var quote = new InstantQuotationPricingService().QuoteWithPhysical(
            fixture.Store.State.RequestState,
            new Dictionary<(Guid, string), InstantQuotationBoundPhysicalAnalysisResult>
            {
                [(part.PartId, "PLA")] = evidence,
            });

        var line = Assert.Single(quote.Parts);
        Assert.True(line.UnitPrice > 0);
        Assert.Equal(line.UnitPrice * quantity, line.Subtotal);
        Assert.Equal(Digest, line.PhysicalReceipt!.UploadSha256);
        Assert.Equal(Owner, line.PhysicalReceipt.OwnerIdentity);
        Assert.Equal(evidence.Physical!.ProfileSha256, line.PhysicalReceipt.ProfileSha256);
        Assert.Null(line.MaterialPrices.Single(price => price.MaterialKey == "PETG").UnitPrice);
        Assert.Throws<InvalidOperationException>(() =>
            new InstantQuotationPricingService().Quote(fixture.Store.State.RequestState));
    }

    [Fact]
    public async Task ReplayedEvidenceForAnotherPart_IsRejectedBeforePricing()
    {
        var fixture = Fixture();
        var evidence = await fixture.Service.AnalyzeAsync(fixture.Binding, default);
        var changed = fixture.Store.State.Parts.Single() with { PartId = Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(() => new InstantQuotationPricingService().QuoteWithPhysical(
            new InstantQuotationOrderState([changed]),
            new Dictionary<(Guid, string), InstantQuotationBoundPhysicalAnalysisResult>
            {
                [(changed.PartId, "PLA")] = evidence,
            }));
    }

    [Fact]
    public async Task WrongOwnerSessionPartOrUpload_NeverReadsBytes()
    {
        var fixture = Fixture();
        InstantQuotationPhysicalAnalysisBinding[] invalid =
        [
            fixture.Binding with { OwnerIdentity = "another-member" },
            fixture.Binding with { SessionId = "another-session" },
            fixture.Binding with { PartId = Guid.NewGuid() },
            fixture.Binding with { FileId = Guid.NewGuid() },
            fixture.Binding with { UploadSha256 = new string('A', 64) },
        ];

        foreach (var binding in invalid)
        {
            Assert.False((await fixture.Service.AnalyzeAsync(binding, default)).IsReady);
        }

        Assert.Equal(0, fixture.Reader.ReadCount);
    }

    [Fact]
    public async Task StaleMaterialPreferenceQuantityOrProfile_NeverReadsBytes()
    {
        var fixture = Fixture();
        InstantQuotationPhysicalAnalysisBinding[] invalid =
        [
            fixture.Binding with { MaterialKey = "ABS" },
            fixture.Binding with { BuildPreference = BuildPreference.Quality },
            fixture.Binding with { Quantity = 2 },
            fixture.Binding with { Quantity = 10_001 },
            fixture.Binding with { ProfileVersion = "old-profile" },
        ];

        foreach (var binding in invalid)
        {
            Assert.False((await fixture.Service.AnalyzeAsync(binding, default)).IsReady);
        }

        Assert.Equal(0, fixture.Reader.ReadCount);
    }

    [Fact]
    public async Task RevisionChangedDuringRead_QuarantinesCompletedAnalysis()
    {
        var fixture = Fixture();
        fixture.Reader.OnRead = () => fixture.Store.State = fixture.Store.State with
        {
            UpdatedAt = fixture.Store.State.UpdatedAt.AddTicks(1),
        };

        var result = await fixture.Service.AnalyzeAsync(fixture.Binding, default);

        Assert.False(result.IsReady);
        Assert.Null(result.Physical);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.StalePart, result.Failure);
    }

    [Fact]
    public async Task CrossPartSubstitutionDuringRead_QuarantinesCompletedAnalysis()
    {
        var fixture = Fixture();
        fixture.Reader.OnRead = () => fixture.Store.State = fixture.Store.State with
        {
            RequestState = new InstantQuotationOrderState(
                [fixture.Store.State.Parts.Single() with { PartId = Guid.NewGuid() }]),
        };

        var result = await fixture.Service.AnalyzeAsync(fixture.Binding, default);

        Assert.False(result.IsReady);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.StalePart, result.Failure);
    }

    [Fact]
    public async Task UnsupportedProfile_FailsBeforeReadingBytes()
    {
        var fixture = Fixture();
        fixture.Store.State = fixture.Store.State with
        {
            RequestState = new InstantQuotationOrderState(
                [fixture.Store.State.Parts.Single() with
                {
                    Configuration = new InstantQuotationPartConfiguration("UNKNOWN", "Black", 1),
                }]),
        };

        var result = await fixture.Service.AnalyzeAsync(
            fixture.Binding with { MaterialKey = "UNKNOWN" }, default);

        Assert.False(result.IsReady);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.ProfileUnavailable, result.Failure);
        Assert.Equal(0, fixture.Reader.ReadCount);
    }

    [Fact]
    public async Task Cancellation_DoesNotReadCustomerBytes()
    {
        var fixture = Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.AnalyzeAsync(fixture.Binding, cancellation.Token));
        Assert.Equal(0, fixture.Reader.ReadCount);
    }

    private static (InstantQuotationBoundPhysicalAnalysisService Service,
        InstantQuotationPhysicalAnalysisBinding Binding, SessionStore Store, InputReader Reader) Fixture(int quantity = 1)
    {
        var reference = new InstantQuotationUploadReference(FileId.ToString("D"));
        var claim = new InstantQuotationGeometryClaim(
            1, Digest, 8, 8, 8, 512, 384,
            Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
            12, 1, true, false, false, 1);
        var upload = InstantQuotationUploadResult.Succeeded("operation", reference, Digest);
        var part = new InstantQuotationPart(
            PartId, "part.stl", reference,
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!,
            new InstantQuotationPartConfiguration("PLA", "Black", quantity),
            new InstantQuotationPhysicalAnalysisUpload(FileId, "part.stl", "model/stl", Bytes.Length, Digest, "clean"));
        var now = DateTimeOffset.UtcNow;
        var store = new SessionStore(new InstantQuotationSessionState(
            SessionId, "submission", new InstantQuotationOrderState([part]), now, now,
            OwnerIdentity: Owner));
        var reader = new InputReader();
        var service = new InstantQuotationBoundPhysicalAnalysisService(
            store, new InstantQuotationAdmittedMeshService(reader), FdmRuntimeProfileCatalog.LoadEmbedded());
        var binding = new InstantQuotationPhysicalAnalysisBinding(
            SessionId, Owner, PartId, FileId, Digest, "PLA", BuildPreference.Standard,
            quantity, FdmRuntimeProfileCatalog.LoadEmbedded().ProfileVersion);
        return (service, binding, store, reader);
    }

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

    private sealed class SessionStore(InstantQuotationSessionState state) : IInstantQuotationSessionStore
    {
        public InstantQuotationSessionState State { get; set; } = state;

        public int ReadCount { get; private set; }

        public Task<InstantQuotationSessionState?> GetAsync(
            string sessionId, string? ownerIdentity, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<InstantQuotationSessionState?>(
                sessionId == State.SessionId && ownerIdentity == Owner ? State : null);
        }

        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity,
            InstantQuotationOrderState requestState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InputReader : IInstantQuotationPhysicalAnalysisInputReader
    {
        public int ReadCount { get; private set; }

        public Action? OnRead { get; set; }

        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            ReadCount++;
            OnRead?.Invoke();
            return Task.FromResult(new InstantQuotationPhysicalAnalysisInputResult(
                Bytes, InstantQuotationPhysicalAnalysisInputFailure.None, FileId, "part.stl", Digest));
        }
    }
}
