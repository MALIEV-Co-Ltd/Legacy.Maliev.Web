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


    [Fact]
    public async Task ExactOriginalThreeMf_ProducesRealReceiptAndTicketAndRejectsOwnerAndRevisionReplay()
    {
        var seed = Fixture();
        byte[] bytes = Convert.FromBase64String(AdmittedThreeMfMeshReaderTests.OriginalFixture);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var original = Assert.Single(seed.Store.State.Parts);
        var part = original with
        {
            DisplayFileName = "original.3mf",
            Geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                InstantQuotationUploadResult.Succeeded("operation", original.UploadReference, digest),
                new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                    Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                    12, 1, true, false, false, 1))!,
            PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with
            {
                FileName = "original.3mf",
                ContentType = "model/3mf",
                SizeBytes = bytes.Length,
                Sha256 = digest,
            },
        };
        seed.Store.State = seed.Store.State with { RequestState = new InstantQuotationOrderState([part]) };
        var reader = new BatchInputReader([part], bytes);
        var profiles = FdmRuntimeProfileCatalog.LoadEmbedded();
        var analysis = new InstantQuotationBoundPhysicalAnalysisService(
            seed.Store, new InstantQuotationAdmittedMeshService(reader), profiles);
        var pricing = new InstantQuotationAuthoritativePricingService(analysis, profiles, new InstantQuotationPricingService());
        Assert.Null(await pricing.QuoteAsync(seed.Store.State, "another-member", false, default));
        Assert.Empty(reader.RequestedParts);
        var quote = await pricing.QuoteAsync(seed.Store.State, Owner, false, default);
        Assert.NotNull(quote);
        var line = Assert.Single(quote.Parts);
        var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
        Assert.Equal(part.PartId, receipt.PartId);
        Assert.Equal(part.PhysicalAnalysisUpload.FileId, receipt.FileId);
        Assert.Equal(digest, receipt.UploadSha256);
        Assert.Equal(Owner, receipt.OwnerIdentity);
        Assert.Equal(profiles.ProfileVersion, receipt.ProfileVersion);
        Assert.Equal(24, receipt.BoundingCm3PerUnit, 3);
        Assert.True(receipt.MotionSeconds > 0);
        Assert.True(receipt.DepositedMm3 > 0);
        Assert.True(line.UnitPrice > 0);
        var state = seed.Store.State with { PhysicalReceipts = [receipt] };
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var now = DateTimeOffset.UtcNow;
        var ticket = tickets.Issue(state, quote, now);
        Assert.True(tickets.Validate(state, quote, ticket, now.AddMinutes(1)));
        Assert.False(tickets.Validate(state with { OwnerIdentity = "another-member" }, quote, ticket, now));
        Assert.False(tickets.Validate(state with { PhysicalReceipts = [] }, quote, ticket, now));
        reader.OnRead = () => seed.Store.State = seed.Store.State with { UpdatedAt = seed.Store.State.UpdatedAt.AddTicks(1) };
        var binding = new InstantQuotationPhysicalAnalysisBinding(SessionId, Owner, part.PartId,
            part.PhysicalAnalysisUpload.FileId, digest, "PLA", BuildPreference.Standard, 1, profiles.ProfileVersion);
        var stale = await analysis.AnalyzeAsync(binding, default);
        Assert.False(stale.IsReady);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.StalePart, stale.Failure);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactSourceGltf_ProducesRealBoundReceiptAndTicketAndRejectsReplay(bool binary)
    {
        var seed = Fixture();
        byte[] bytes = binary ? Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb)
            : System.Text.Encoding.UTF8.GetBytes(AdmittedGltfMeshReaderTests.OriginalGltf);
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        byte[] gitObject = [.. header, .. bytes];
        Assert.Equal(binary ? "32bb65e5ed854a5eef1abc5a8c18b3cc2735206e" : "648d23fc2be788f2564ff62d0b24969fb4f8828b",
            Convert.ToHexStringLower(SHA1.HashData(gitObject)));
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var original = Assert.Single(seed.Store.State.Parts);
        var part = original with
        {
            DisplayFileName = binary ? "source.glb" : "source.gltf",
            Geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                InstantQuotationUploadResult.Succeeded("operation", original.UploadReference, digest),
                new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                    Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                    12, 1, true, false, false, 1))!,
            PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with
            {
                FileName = binary ? "source.glb" : "source.gltf",
                ContentType = binary ? "model/gltf-binary" : "model/gltf+json",
                SizeBytes = bytes.Length,
                Sha256 = digest,
            },
        };
        seed.Store.State = seed.Store.State with { RequestState = new InstantQuotationOrderState([part]) };
        var reader = new BatchInputReader([part], bytes);
        var profiles = FdmRuntimeProfileCatalog.LoadEmbedded();
        var analysis = new InstantQuotationBoundPhysicalAnalysisService(
            seed.Store, new InstantQuotationAdmittedMeshService(reader), profiles);
        var pricing = new InstantQuotationAuthoritativePricingService(analysis, profiles, new InstantQuotationPricingService());
        var binding = new InstantQuotationPhysicalAnalysisBinding(SessionId, Owner, part.PartId,
            part.PhysicalAnalysisUpload.FileId, digest, "PLA", BuildPreference.Standard, 1, profiles.ProfileVersion);
        InstantQuotationPhysicalAnalysisBinding[] invalid =
        [
            binding with { OwnerIdentity = "another-member" },
            binding with { SessionId = "another-session" },
            binding with { PartId = Guid.NewGuid() },
            binding with { FileId = Guid.NewGuid() },
            binding with { UploadSha256 = new string('A', 64) },
            binding with { MaterialKey = "PETG" },
            binding with { BuildPreference = BuildPreference.Strength },
            binding with { Quantity = 2 },
            binding with { ProfileVersion = "other-profile" },
        ];
        foreach (var rejected in invalid) Assert.False((await analysis.AnalyzeAsync(rejected, default)).IsReady);
        Assert.Empty(reader.RequestedParts);
        Assert.Null(await pricing.QuoteAsync(seed.Store.State, "another-member", false, default));
        Assert.Empty(reader.RequestedParts);
        var quote = await pricing.QuoteAsync(seed.Store.State, Owner, false, default);
        Assert.NotNull(quote);
        var line = Assert.Single(quote.Parts);
        var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
        Assert.Equal(part.PartId, receipt.PartId);
        Assert.Equal(part.PhysicalAnalysisUpload.FileId, receipt.FileId);
        Assert.Equal(digest, receipt.UploadSha256);
        Assert.Equal(Owner, receipt.OwnerIdentity);
        Assert.Equal(SessionId, receipt.SessionId);
        Assert.Equal("PLA", receipt.ConfiguredMaterialKey);
        Assert.Equal("PLA", receipt.MaterialKey);
        Assert.Equal(BuildPreference.Standard, receipt.BuildPreference);
        Assert.Equal(1, receipt.Quantity);
        Assert.Equal(profiles.ProfileVersion, receipt.ProfileVersion);
        Assert.Equal(1, receipt.BoundingCm3PerUnit, 3);
        Assert.Equal(1, line.BoundingCm3PerUnit, 3);
        Assert.True(receipt.MotionSeconds > 0);
        Assert.True(receipt.DepositedMm3 > 0);
        Assert.True(line.UnitPrice > 0);
        var state = seed.Store.State with { PhysicalReceipts = [receipt] };
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var now = DateTimeOffset.UtcNow;
        var ticket = tickets.Issue(state, quote, now);
        Assert.True(tickets.Validate(state, quote, ticket, now.AddMinutes(1)));
        Assert.False(tickets.Validate(state with { OwnerIdentity = "another-member" }, quote, ticket, now));
        Assert.False(tickets.Validate(state with { PhysicalReceipts = [] }, quote, ticket, now));
        Assert.False(tickets.Validate(state with { PhysicalReceipts = [receipt with { UploadSha256 = new string('A', 64) }] }, quote, ticket, now));
        reader.OnRead = () => seed.Store.State = seed.Store.State with { UpdatedAt = seed.Store.State.UpdatedAt.AddTicks(1) };
        var stale = await analysis.AnalyzeAsync(binding, default);
        Assert.False(stale.IsReady);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.StalePart, stale.Failure);
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

    [Theory]
    [InlineData(1, false, "stl")]
    [InlineData(2, false, "stl")]
    [InlineData(1, true, "stl")]
    [InlineData(1, false, "obj")]
    [InlineData(1, false, "3mf")]
    [InlineData(1, false, "gltf")]
    [InlineData(1, false, "glb")]
    public async Task RecoverableOriginalBytes_ProduceSignedCurrentReviewProvenanceWithoutBrowserAuthority(
        int quantity, bool collapsedSeam, string extension)
    {
        var seed = Fixture(quantity);
        byte[] bytes = RecoveryFixture(extension, collapsedSeam);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var original = Assert.Single(seed.Store.State.Parts);
        var part = original with
        {
            DisplayFileName = "recoverable." + extension,
            Geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                InstantQuotationUploadResult.Succeeded("operation", original.UploadReference, digest),
                new InstantQuotationGeometryClaim(1, digest, 10, 10, 10, 1000, 500,
                    Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(40d, 64).ToArray(),
                    collapsedSeam ? 11 : 10, 1, true, true, false, 0))!,
            PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with
            { FileName = "recoverable." + extension, SizeBytes = bytes.Length, Sha256 = digest },
        };
        seed.Store.State = seed.Store.State with { RequestState = new InstantQuotationOrderState([part]) };
        var reader = new BatchInputReader([part], bytes);
        var profiles = FdmRuntimeProfileCatalog.LoadEmbedded();
        var analysis = new InstantQuotationBoundPhysicalAnalysisService(
            seed.Store, new InstantQuotationAdmittedMeshService(reader), profiles);
        var pricing = new InstantQuotationAuthoritativePricingService(analysis, profiles, new InstantQuotationPricingService());
        var binding = new InstantQuotationPhysicalAnalysisBinding(SessionId, Owner, part.PartId,
            FileId, digest, "PLA", BuildPreference.Standard, quantity, profiles.ProfileVersion);
        InstantQuotationPhysicalAnalysisBinding[] invalid =
        [
            binding with { OwnerIdentity = "another-member" }, binding with { SessionId = "another-session" },
            binding with { PartId = Guid.NewGuid() }, binding with { FileId = Guid.NewGuid() },
            binding with { UploadSha256 = new string('a', 64) }, binding with { MaterialKey = "PETG" },
            binding with { BuildPreference = BuildPreference.Strength }, binding with { Quantity = quantity + 1 },
            binding with { ProfileVersion = "other-profile" },
        ];
        foreach (var rejected in invalid) Assert.False((await analysis.AnalyzeAsync(rejected, default)).IsReady);
        Assert.Empty(reader.RequestedParts);
        Assert.Null(await pricing.QuoteAsync(seed.Store.State, "another-member", false, default));
        Assert.Empty(reader.RequestedParts);
        var quote = await pricing.QuoteAsync(seed.Store.State, Owner, false, default);
        Assert.NotNull(quote);
        var line = Assert.Single(quote.Parts);
        var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
        Assert.Equal(digest, receipt.UploadSha256);
        Assert.Equal(FileId, receipt.FileId);
        Assert.Equal(PartId, receipt.PartId);
        Assert.Equal(Owner, receipt.OwnerIdentity);
        Assert.Equal(SessionId, receipt.SessionId);
        Assert.Equal(quantity, receipt.Quantity);
        Assert.Equal("PLA", receipt.MaterialKey);
        Assert.Equal(BuildPreference.Standard, receipt.BuildPreference);
        Assert.Equal(1, receipt.BoundingCm3PerUnit, 3);
        Assert.True(receipt.DepositedMm3 > 0);
        Assert.True(receipt.MotionSeconds > 0);
        Assert.True(line.UnitPrice > 0);
        Assert.Equal(line.UnitPrice * quantity, line.Subtotal);
        var reconstruction = Assert.IsType<Legacy.Maliev.Web.Application.Pricing.Simulation.VoxelReconstructionInfo>(
            receipt.GeometryReconstruction);
        Assert.Equal("voxel-majority-v1", reconstruction.Method);
        Assert.Equal(0.2, reconstruction.VoxelSizeMm);
        var state = seed.Store.State with { PhysicalReceipts = [receipt] };
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var now = DateTimeOffset.UtcNow;
        var authorization = tickets.Issue(state, quote, now);
        var payload = tickets.UnprotectLine(Assert.Single(authorization.LineTickets), now);
        Assert.Equal(reconstruction, payload.GeometryReconstruction);
        Assert.Equal("engineer_review_required", payload.ReviewState);
        Assert.Equal(digest, payload.ContentSha256);
        Assert.Equal(receipt.PhysicalSha256, payload.PhysicalSha256);
        Assert.True(tickets.Validate(state, quote, authorization, now));
        payload.GeometryReconstruction = null;
        Assert.False(tickets.Validate(state, quote,
            authorization with { LineTickets = [tickets.ProtectLine(payload)] }, now));
        Assert.False(tickets.Validate(state with { OwnerIdentity = "another-member" }, quote, authorization, now));
        Assert.False(tickets.Validate(state with { PhysicalReceipts = [receipt with { GeometryReconstruction = null }] },
            quote, authorization, now));
        reader.OnRead = () => seed.Store.State = seed.Store.State with
        { UpdatedAt = seed.Store.State.UpdatedAt.AddTicks(1) };
        var stale = await analysis.AnalyzeAsync(binding, default);
        Assert.False(stale.IsReady);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.StalePart, stale.Failure);
    }

    [Fact]
    public async Task RecoveryGridRefusal_ReportsComplexityRatherThanAnUnavailableSimulator()
    {
        var seed = Fixture();
        byte[] bytes = RecoveryFixture("stl", false, 300);
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var original = Assert.Single(seed.Store.State.Parts);
        var part = original with
        {
            Geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                InstantQuotationUploadResult.Succeeded("operation", original.UploadReference, digest),
                new InstantQuotationGeometryClaim(1, digest, 300, 300, 300, 27_000_000, 450_000,
                    Enumerable.Repeat(90_000d, 64).ToArray(), Enumerable.Repeat(1200d, 64).ToArray(),
                    10, 1, true, true, false, 0))!,
            PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with
            { SizeBytes = bytes.Length, Sha256 = digest },
        };
        seed.Store.State = seed.Store.State with { RequestState = new InstantQuotationOrderState([part]) };
        var profiles = FdmRuntimeProfileCatalog.LoadEmbedded();
        var service = new InstantQuotationBoundPhysicalAnalysisService(
            seed.Store, new InstantQuotationAdmittedMeshService(new BatchInputReader([part], bytes)), profiles);
        var binding = new InstantQuotationPhysicalAnalysisBinding(SessionId, Owner, part.PartId,
            FileId, digest, "PLA", BuildPreference.Standard, 1, profiles.ProfileVersion);

        var result = await service.AnalyzeAsync(binding, default);

        Assert.False(result.IsReady);
        Assert.Null(result.Physical);
        Assert.Equal(InstantQuotationBoundPhysicalAnalysisFailure.ComplexityExceeded, result.Failure);
    }

    // Independent open 10 mm box: the existing literal BinaryBox's two bottom triangles are absent.
    private static byte[] RecoveryFixture(string extension, bool collapsedSeam, float size = 10)
    {
        byte[] closed = BinaryBox(size);
        byte[] stl = [.. closed[..84], .. closed[184..]];
        if (collapsedSeam) stl = [.. stl, .. new byte[50]];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(stl.AsSpan(80, 4), collapsedSeam ? 11u : 10u);
        if (extension == "stl") return stl;
        var coordinates = new List<float>();
        for (int triangle = 0; triangle < 10; triangle++)
            for (int coordinate = 0; coordinate < 9; coordinate++)
                coordinates.Add(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                    stl.AsSpan(84 + triangle * 50 + 12 + coordinate * 4, 4)));
        string Number(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (extension == "obj")
        {
            var text = new System.Text.StringBuilder();
            for (int index = 0; index < coordinates.Count; index += 3)
                text.AppendLine($"v {Number(coordinates[index])} {Number(coordinates[index + 1])} {Number(coordinates[index + 2])}");
            for (int face = 0; face < 10; face++)
                text.AppendLine($"f {face * 3 + 1} {face * 3 + 2} {face * 3 + 3}");
            return System.Text.Encoding.UTF8.GetBytes(text.ToString());
        }
        if (extension == "3mf")
        {
            var model = new System.Text.StringBuilder(
                "<model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" unit=\"millimeter\"><resources><object id=\"1\" type=\"model\"><mesh><vertices>");
            for (int index = 0; index < coordinates.Count; index += 3)
                model.Append($"<vertex x=\"{Number(coordinates[index])}\" y=\"{Number(coordinates[index + 1])}\" z=\"{Number(coordinates[index + 2])}\"/>");
            model.Append("</vertices><triangles>");
            for (int face = 0; face < 10; face++)
                model.Append($"<triangle v1=\"{face * 3}\" v2=\"{face * 3 + 1}\" v3=\"{face * 3 + 2}\"/>");
            model.Append("</triangles></mesh></object></resources><build><item objectid=\"1\"/></build></model>");
            using var output = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(output,
                System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/></Types>");
                Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/3dmodel.model\" Id=\"r1\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");
                Add("3D/3dmodel.model", model.ToString());
                void Add(string name, string content)
                {
                    using var stream = zip.CreateEntry(name).Open();
                    using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                    writer.Write(content);
                }
            }
            return output.ToArray();
        }
        var binary = new byte[coordinates.Count * 4];
        for (int index = 0; index < coordinates.Count; index++)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(binary.AsSpan(index * 4, 4), coordinates[index]);
        object buffer = extension == "glb"
            ? new { byteLength = binary.Length }
            : (object)new { byteLength = binary.Length, uri = "data:application/octet-stream;base64," + Convert.ToBase64String(binary) };
        byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            asset = new { version = "2.0" }, scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0 } },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 }, mode = 4 } } } },
            buffers = new[] { buffer },
            bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = binary.Length } },
            accessors = new[] { new { bufferView = 0, byteOffset = 0, componentType = 5126, count = 30, type = "VEC3" } },
        });
        if (extension == "gltf") return json;
        using var glb = new MemoryStream();
        using var glbWriter = new BinaryWriter(glb);
        int paddedJson = (json.Length + 3) & ~3;
        glbWriter.Write(0x46546C67u); glbWriter.Write(2u);
        glbWriter.Write((uint)(12 + 8 + paddedJson + 8 + binary.Length));
        glbWriter.Write((uint)paddedJson); glbWriter.Write(0x4E4F534Au); glbWriter.Write(json);
        for (int index = json.Length; index < paddedJson; index++) glbWriter.Write((byte)0x20);
        glbWriter.Write((uint)binary.Length); glbWriter.Write(0x004E4942u); glbWriter.Write(binary);
        glbWriter.Flush();
        return glb.ToArray();
    }

    private sealed class BatchInputReader(IReadOnlyList<InstantQuotationPart> parts, byte[] bytes)
        : IInstantQuotationPhysicalAnalysisInputReader
    {
        internal List<Guid> RequestedParts { get; } = [];
        internal Action? OnRead { get; set; }

        public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
            string sessionId, string? ownerIdentity, Guid partId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(SessionId, sessionId);
            Assert.Equal(Owner, ownerIdentity);
            var part = Assert.Single(parts, candidate => candidate.PartId == partId);
            RequestedParts.Add(partId);
            OnRead?.Invoke();
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
