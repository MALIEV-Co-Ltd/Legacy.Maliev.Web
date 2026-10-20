using System.Numerics;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.AspNetCore.DataProtection;

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
        Assert.Equal(0.512, line.BoundingCm3PerUnit, 3);
        Assert.Equal(line.BoundingCm3PerUnit, line.PhysicalReceipt.BoundingCm3PerUnit);
        Assert.Null(line.MaterialPrices.Single(price => price.MaterialKey == "PETG").UnitPrice);
        Assert.Throws<InvalidOperationException>(() =>
            new InstantQuotationPricingService().Quote(fixture.Store.State.RequestState));
    }

    [Fact]
    public async Task BrowserGeometryClaim_CannotChangePhysicalFdmShippingWeightOrBounding()
    {
        var fixture = Fixture();
        var evidence = await fixture.Service.AnalyzeAsync(fixture.Binding, default);
        var original = Assert.Single(fixture.Store.State.Parts);
        var browserClaim = new InstantQuotationGeometryClaim(
            1, Digest, 6, 6, 6, 150, 200,
            Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
            12, 1, true, false, false, 1);
        Assert.True(browserClaim.IsValid());
        var altered = original with
        {
            Geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                InstantQuotationUploadResult.Succeeded("operation", original.UploadReference, Digest),
                browserClaim)!,
        };
        var pricing = new InstantQuotationPricingService();
        var evidenceByMaterial = new Dictionary<(Guid, string), InstantQuotationBoundPhysicalAnalysisResult>
        {
            [(original.PartId, "PLA")] = evidence,
        };

        var actual = pricing.QuoteWithPhysical(new InstantQuotationOrderState([altered]), evidenceByMaterial);
        var expected = pricing.QuoteWithPhysical(fixture.Store.State.RequestState, evidenceByMaterial);

        Assert.Equal(expected.Parts.Single().BoundingCm3PerUnit, actual.Parts.Single().BoundingCm3PerUnit);
        Assert.Equal(expected.Parts.Single().WeightGramsPerUnit, actual.Parts.Single().WeightGramsPerUnit);
        Assert.Equal(expected.FinalOrderPrice, actual.FinalOrderPrice);
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

    /// <summary>Controlled clean server bytes feed the real mesh, simulation, receipt and ticket services.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SevenAdmittedMeshParts_ProduceRealPhysicalReceiptsAndExactProtectedBatchAuthorization(bool obj)
    {
        var seed = Fixture();
        byte[] bytes = obj ? System.Text.Encoding.UTF8.GetBytes(ObjCube) : Bytes;
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string extension = obj ? "obj" : "stl";
        var original = Assert.Single(seed.Store.State.Parts);
        var parts = Enumerable.Range(0, 7).Select(index =>
        {
            var fileId = Guid.NewGuid();
            return original with
            {
                PartId = Guid.NewGuid(),
                DisplayFileName = $"part-{index}.{extension}",
                Geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("operation",
                        new InstantQuotationUploadReference(fileId.ToString("D")), digest),
                    new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                        12, 1, true, false, false, 1))!,
                UploadReference = new InstantQuotationUploadReference(fileId.ToString("D")),
                PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with
                {
                    FileId = fileId,
                    FileName = $"part-{index}.{extension}",
                    ContentType = obj ? "model/obj" : "model/stl",
                    SizeBytes = bytes.Length,
                    Sha256 = digest,
                },
            };
        }).ToArray();
        seed.Store.State = seed.Store.State with { RequestState = new InstantQuotationOrderState(parts) };
        var reader = new BatchInputReader(parts, bytes);
        var profiles = FdmRuntimeProfileCatalog.LoadEmbedded();
        var analysis = new InstantQuotationBoundPhysicalAnalysisService(
            seed.Store, new InstantQuotationAdmittedMeshService(reader), profiles);
        var pricing = new InstantQuotationAuthoritativePricingService(
            analysis, profiles, new InstantQuotationPricingService());

        Assert.Null(await pricing.QuoteAsync(seed.Store.State, "another-member", includeComparisons: false, default));
        Assert.Empty(reader.RequestedParts);
        var quote = await pricing.QuoteAsync(seed.Store.State, Owner, includeComparisons: false, default);

        Assert.NotNull(quote);
        Assert.Equal(parts.Select(part => part.PartId), quote.Parts.Select(part => part.PartId));
        Assert.Equal(7, reader.RequestedParts.Count);
        Assert.Equal(parts.Select(part => part.PartId), reader.RequestedParts);
        Assert.Equal(quote.Parts.Sum(part => part.Subtotal), quote.ItemsSubtotal, 2);
        Assert.True(quote.FinalOrderPrice > quote.ItemsSubtotal);
        var receipts = quote.Parts.Select((line, index) =>
        {
            var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
            Assert.Equal(parts[index].PartId, receipt.PartId);
            Assert.Equal(parts[index].PhysicalAnalysisUpload!.FileId, receipt.FileId);
            Assert.Equal(digest, receipt.UploadSha256);
            Assert.Equal(Owner, receipt.OwnerIdentity);
            Assert.Equal(profiles.ProfileVersion, receipt.ProfileVersion);
            Assert.True(receipt.MotionSeconds > 0);
            Assert.True(receipt.DepositedMm3 > 0);
            Assert.Equal(obj ? 1 : 0.512, receipt.BoundingCm3PerUnit, 3);
            Assert.True(line.UnitPrice > 0);
            Assert.Equal(line.UnitPrice * line.Quantity, line.Subtotal, 2);
            return receipt;
        }).ToArray();
        var state = seed.Store.State with { PhysicalReceipts = receipts };
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var now = DateTimeOffset.UtcNow;
        var authorization = tickets.Issue(state, quote, now);

        Assert.Equal(7, authorization.LineTickets.Count);
        Assert.False(string.IsNullOrWhiteSpace(authorization.OrderTicket));
        Assert.True(tickets.Validate(state, quote, authorization, now.AddMinutes(1)));
        Assert.False(tickets.Validate(
            state with { PhysicalReceipts = receipts[..^1] }, quote, authorization, now.AddMinutes(1)));
        Assert.False(tickets.Validate(
            state with { OwnerIdentity = "another-member" }, quote, authorization, now.AddMinutes(1)));
        Assert.False(tickets.Validate(state, quote,
            authorization with { LineTickets = authorization.LineTickets.Take(6).ToArray() },
            now.AddMinutes(1)));
        Assert.False(tickets.Validate(state, quote,
            authorization with { LineTickets = authorization.LineTickets.Reverse().ToArray() },
            now.AddMinutes(1)));
    }

    // Exact unchanged source fixture 36ffdc2c4029f75cd86818bba6ba6c80e8593a66.
    private const string ObjCube = """
        # https://github.com/mikedh/trimesh
        v -5.00000000 -5.00000000 -5.00000000
        v -5.00000000 -5.00000000 5.00000000
        v -5.00000000 5.00000000 -5.00000000
        v -5.00000000 5.00000000 5.00000000
        v 5.00000000 -5.00000000 -5.00000000
        v 5.00000000 -5.00000000 5.00000000
        v 5.00000000 5.00000000 -5.00000000
        v 5.00000000 5.00000000 5.00000000
        f 2 4 1
        f 5 2 1
        f 1 4 3
        f 3 5 1
        f 2 8 4
        f 6 2 5
        f 6 8 2
        f 4 8 3
        f 7 5 3
        f 3 8 7
        f 7 6 5
        f 8 6 7
        
        """;

    private sealed class BatchInputReader(IReadOnlyList<InstantQuotationPart> parts, byte[] bytes)
        : IInstantQuotationPhysicalAnalysisInputReader
    {
        internal List<Guid> RequestedParts { get; } = [];

        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(SessionId, sessionId);
            Assert.Equal(Owner, ownerIdentity);
            var part = Assert.Single(parts, candidate => candidate.PartId == partId);
            RequestedParts.Add(partId);
            var upload = part.PhysicalAnalysisUpload!;
            return Task.FromResult(new InstantQuotationPhysicalAnalysisInputResult(
                bytes, InstantQuotationPhysicalAnalysisInputFailure.None,
                upload.FileId, upload.FileName, upload.Sha256));
        }
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
