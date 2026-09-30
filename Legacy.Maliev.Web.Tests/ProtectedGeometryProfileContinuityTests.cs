using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Continuity of caller-supplied sampled geometry, not manufacturing authority.</summary>
public sealed class ProtectedGeometryProfileContinuityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Owner = "customer-423";

    [Theory]
    [InlineData(64)]
    [InlineData(24)]
    public async Task CreateAsync_UnsupportedSamples_SnapshotRetainsExactValues(int count)
    {
        var fixture = new Fixture();
        var input = Samples(count);
        var part = Part(input, count);
        var created = await fixture.Store.CreateAsync(Owner, new([part]), default);
        input[1] = 99;

        var retained = Assert.Single(created.Parts).Geometry.UnsupportedAreaProfileMm2;
        Assert.Equal(count, retained.Count);
        Assert.Equal(10, retained[1]);
        Assert.Equal(11, retained[^1]);
        Assert.False(retained is double[]);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(24)]
    public async Task GetAsync_ProtectedSamples_RetainsExactOrderedProfile(int count)
    {
        var fixture = new Fixture();
        var created = await fixture.Store.CreateAsync(Owner, new([Part(null, count)]), default);
        await fixture.ReplaceProfileAsync(created.SessionId, ProfileNode(Samples(count)));

        var restored = await fixture.Store.GetAsync(created.SessionId, Owner, default);

        Assert.NotNull(restored);
        var retained = Assert.Single(restored.Parts).Geometry.UnsupportedAreaProfileMm2;
        Assert.Equal(count, retained.Count);
        Assert.Equal(10, retained[1]);
        Assert.Equal(11, retained[^1]);
    }

    [Fact]
    public async Task PutAsync_ProfileChange_PreservesValuesAndRejectsStaleRevisionWithoutExtendingExpiry()
    {
        var fixture = new Fixture();
        var created = await fixture.Store.CreateAsync(Owner, new([Part(null)]), default);
        var replacement = created with { RequestState = new([Part(Samples(64)) with { PartId = created.Parts[0].PartId }]) };
        fixture.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await fixture.Store.PutAsync(replacement, Owner, default));
        Assert.False(await fixture.Store.PutAsync(replacement, Owner, default));
        var restored = await fixture.Store.GetAsync(created.SessionId, Owner, default);
        Assert.NotNull(restored);
        Assert.Equal(created.SessionId, restored.SessionId);
        Assert.Equal(created.SubmissionId, restored.SubmissionId);
        Assert.Equal(created.CreatedAt, restored.CreatedAt);
        Assert.Equal(Now.AddMinutes(1), restored.UpdatedAt);
        Assert.Equal(Now.AddHours(3), fixture.Cache.LastExpiration);
        Assert.Equal(10, Assert.Single(restored.Parts).Geometry.UnsupportedAreaProfileMm2.ElementAtOrDefault(1));
        using var payload = JsonDocument.Parse(fixture.Payload(created.SessionId));
        Assert.Equal(1, payload.RootElement.GetProperty("Version").GetInt32());
        fixture.Time.Advance(TimeSpan.FromHours(3));
        Assert.Null(await fixture.Store.GetAsync(created.SessionId, Owner, default));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("short-high-facet")]
    [InlineData("negative")]
    [InlineData("nan")]
    [InlineData("infinity")]
    public async Task GetAsync_PresentMalformedUnsupportedProfile_RejectsAndRemovesPayload(string malformed)
    {
        var fixture = new Fixture();
        var count = malformed == "short-high-facet" ? 24 : 64;
        var created = await fixture.Store.CreateAsync(Owner, new([Part(null, count)]), default);
        var profile = ProfileNode(Samples(malformed.StartsWith("short", StringComparison.Ordinal) ? count - 1 : count));
        if (malformed == "negative") profile[1] = -1;
        if (malformed == "nan") profile[1] = "NaN";
        if (malformed == "infinity") profile[1] = "Infinity";
        await fixture.ReplaceProfileAsync(created.SessionId, profile);

        Assert.Null(await fixture.Store.GetAsync(created.SessionId, Owner, default));
        Assert.Null(await fixture.Cache.GetAsync(Fixture.Key(created.SessionId)));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task GetAsync_OldVersionOneUnavailableProfile_DoesNotInventZeroSamplesOrRewrite(string shape)
    {
        var fixture = new Fixture();
        var created = await fixture.Store.CreateAsync(Owner, new([Part(null)]), default);
        await fixture.ReplaceProfileAsync(created.SessionId, shape == "empty" ? new JsonArray() : null, shape == "absent");
        var before = fixture.Cache.Get(Fixture.Key(created.SessionId));
        var writes = fixture.Cache.Writes;

        var restored = await fixture.Store.GetAsync(created.SessionId, Owner, default);

        Assert.NotNull(restored);
        Assert.Empty(Assert.Single(restored.Parts).Geometry.UnsupportedAreaProfileMm2);
        Assert.Equal(created.CreatedAt, restored.CreatedAt);
        Assert.Equal(created.UpdatedAt, restored.UpdatedAt);
        Assert.Equal(writes, fixture.Cache.Writes);
        Assert.Equal(before, fixture.Cache.Get(Fixture.Key(created.SessionId)));
        Assert.Null(await fixture.Store.GetAsync(created.SessionId, "other-owner", default));
        Assert.True(await fixture.Store.PutAsync(restored, Owner, default));
        Assert.Equal(Now.AddHours(3), fixture.Cache.LastExpiration);
    }

    [Fact]
    public async Task GetAsync_ExplicitZeroProfile_RemainsDistinctFromUnavailable()
    {
        var fixture = new Fixture();
        var created = await fixture.Store.CreateAsync(Owner, new([Part(null)]), default);
        await fixture.ReplaceProfileAsync(created.SessionId, ProfileNode(new double[64]));

        var restored = await fixture.Store.GetAsync(created.SessionId, Owner, default);

        Assert.NotNull(restored);
        Assert.Equal(64, Assert.Single(restored.Parts).Geometry.UnsupportedAreaProfileMm2.Count);
        Assert.All(restored.Parts[0].Geometry.UnsupportedAreaProfileMm2, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData("area")]
    [InlineData("perimeter")]
    [InlineData("unsupported")]
    public void Validate_ChangedSampleButIdenticalScalarGeometryAndMoney_RejectsReplay(string profile)
    {
        var fixture = new Fixture();
        var original = Session(Part(Samples(64)));
        var pricing = new InstantQuotationPricingService();
        var quote = pricing.Quote(original.RequestState);
        var authorization = fixture.Tickets.Issue(original, quote, Now);
        Assert.True(fixture.Tickets.Validate(original, quote, authorization, Now));
        var changedPart = Part(Samples(64), changedProfile: profile) with { PartId = original.Parts[0].PartId, UploadReference = original.Parts[0].UploadReference };
        var changed = original with { RequestState = new([changedPart]) };
        var unchangedMoney = pricing.Quote(changed.RequestState);
        Assert.Equal(quote.ItemsSubtotal, unchangedMoney.ItemsSubtotal);
        Assert.Equal(quote.Parts[0].UnitPrice, unchangedMoney.Parts[0].UnitPrice);

        Assert.False(fixture.Tickets.Validate(changed, unchangedMoney, authorization, Now));
    }

    [Fact]
    public void GeometryDigest_ProfileValues_AreInvariantAcrossCultureAndUnavailableIsNotZero()
    {
        var fixture = new Fixture();
        var previous = CultureInfo.CurrentCulture;
        var fractionalSamples = Samples(64);
        fractionalSamples[1] = 10.25;
        fractionalSamples[^1] = 11.5;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var original = Digest(fixture, Session(Part(fractionalSamples)));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(original, Digest(fixture, Session(Part(fractionalSamples))));
            Assert.NotEqual(Digest(fixture, Session(Part(null))), Digest(fixture, Session(Part(new double[64]))));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GeometryDigest_OrderedNumericSampleBoundaries_DoNotCollide()
    {
        var fixture = new Fixture();
        var first = Samples(64);
        first[1] = 1;
        first[2] = 23;
        var second = Samples(64);
        second[1] = 12;
        second[2] = 3;

        Assert.NotEqual(Digest(fixture, Session(Part(first))), Digest(fixture, Session(Part(second))));
    }

    [Fact]
    public void UnprotectLine_OldVersionThreePurposeAndSchema_RequiresFreshAuthorization()
    {
        var fixture = new Fixture();
        var session = Session(Part(null));
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var fresh = fixture.Tickets.Issue(session, quote, Now);
        var currentPayload = fixture.Tickets.UnprotectLine(fresh.LineTickets[0], Now);
        currentPayload.SchemaVersion = "additive-line-quote.v3";
        var old = fixture.Provider.CreateProtector("Maliev.Web.AdditiveLineQuote.v3")
            .Protect(JsonSerializer.Serialize(currentPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        Assert.Throws<AdditiveQuoteTicketException>(() => fixture.Tickets.UnprotectLine(old, Now));
        Assert.False(fixture.Tickets.Validate(session, quote, fresh with { LineTickets = [old] }, Now));
        Assert.True(fixture.Tickets.Validate(session, quote, fresh, Now));
    }

    [Fact]
    public void Issue_FreshLine_UsesVersionFourPurposeWhileOrderRemainsVersionTwo()
    {
        var fixture = new Fixture();
        var session = Session(Part(Samples(64)));
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var issued = fixture.Tickets.Issue(session, quote, Now);

        Assert.Equal("additive-line-quote.v4", fixture.Tickets.UnprotectLine(issued.LineTickets[0], Now).SchemaVersion);
        var lineJson = fixture.Provider.CreateProtector("Maliev.Web.AdditiveLineQuote.v4").Unprotect(issued.LineTickets[0]);
        using var document = JsonDocument.Parse(lineJson);
        Assert.Equal("additive-line-quote.v4", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("additive-order-quote.v2", fixture.Tickets.UnprotectOrder(issued.OrderTicket, Now).SchemaVersion);
        Assert.True(fixture.Tickets.Validate(session, quote, issued, Now));
    }

    [Fact]
    public void Validate_VersionTwoOrderBoundToOldVersionThreeLine_RejectsReplay()
    {
        var fixture = new Fixture();
        var session = Session(Part(null));
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var fresh = fixture.Tickets.Issue(session, quote, Now);
        var line = fixture.Tickets.UnprotectLine(fresh.LineTickets[0], Now);
        line.SchemaVersion = "additive-line-quote.v3";
        var oldLine = fixture.Provider.CreateProtector("Maliev.Web.AdditiveLineQuote.v3")
            .Protect(JsonSerializer.Serialize(line, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var oldOrder = fixture.Tickets.UnprotectOrder(fresh.OrderTicket, Now);
        oldOrder.LineTicketDigests = [AdditiveQuoteTicketService.DigestTicket(oldLine)];
        var correctlyBoundOrder = fixture.Tickets.ProtectOrder(oldOrder);
        Assert.True(fixture.Tickets.MatchesLineTickets(fixture.Tickets.UnprotectOrder(correctlyBoundOrder, Now), [oldLine]));

        Assert.False(fixture.Tickets.Validate(session, quote, new([oldLine], correctlyBoundOrder), Now));
    }

    [Fact]
    public async Task Coordinator_ActualProtectedPutGet_RetainsProfilesThroughPricingAndTicketIssuance()
    {
        var fixture = new Fixture();
        var created = await fixture.Store.CreateAsync(Owner, new([Part(null)]), default);
        await fixture.ReplaceProfileAsync(created.SessionId, ProfileNode(Samples(64)));
        var pricingBoundary = new ObservedPricing();
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            fixture.Store, new UnusedUploadClient(), new InstantQuotationPricingService(), Owner,
            quoteTicketService: fixture.Tickets, authoritativePricingService: pricingBoundary);

        await coordinator.InitializeAsync(created.SessionId, default);

        Assert.NotNull(coordinator.OrderQuote);
        var restored = await fixture.Store.GetAsync(created.SessionId, Owner, default);
        Assert.NotNull(restored);
        Assert.NotNull(restored.QuoteAuthorization);
        Assert.True(fixture.Tickets.Validate(restored, coordinator.OrderQuote, restored.QuoteAuthorization, DateTimeOffset.UtcNow));
        Assert.Equal(64, pricingBoundary.UnsupportedSamples.Count);
        Assert.Equal(10, pricingBoundary.UnsupportedSamples.ElementAtOrDefault(1));
        Assert.Equal(10, restored.Parts[0].Geometry.UnsupportedAreaProfileMm2.ElementAtOrDefault(1));
    }

    private static string Digest(Fixture fixture, InstantQuotationSessionState session)
    {
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var authorization = fixture.Tickets.Issue(session, quote, Now);
        return fixture.Tickets.UnprotectLine(authorization.LineTickets[0], Now).GeometryDigest;
    }

    private static InstantQuotationSessionState Session(InstantQuotationPart part) => new(
        "session-423", "submission-423", new([part]), Now, Now, OwnerIdentity: Owner);

    private static double[] Samples(int count)
    {
        var values = new double[count];
        values[1] = 10;
        values[^1] = 11;
        return values;
    }

    private static JsonArray ProfileNode(double[] values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static InstantQuotationPart Part(double[]? unsupported, int count = 64, string? changedProfile = null)
    {
        var areas = Enumerable.Repeat(200d, count).ToArray();
        var perimeters = Enumerable.Repeat(80d, count).ToArray();
        if (changedProfile == "area") areas[1] = 201;
        if (changedProfile == "perimeter") perimeters[1] = 81;
        if (changedProfile == "unsupported") unsupported![1] = 12;
        var claim = new InstantQuotationGeometryClaim(1, new string('a', 64), 20, 20, 10, 2000, 1200,
            areas, perimeters, count == 24 ? 300_000 : 1024, 1, count != 24, false, false, .8, unsupported);
        Assert.True(claim.IsValid());
        var reference = new InstantQuotationUploadReference("00000000-0000-0000-0000-000000000423");
        var upload = InstantQuotationUploadResult.Succeeded("operation-423", reference, claim.Sha256);
        var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim);
        Assert.NotNull(geometry);
        return new(Guid.NewGuid(), "part.stl", reference, geometry, new("M68", "Gray", 2));
    }

    private sealed class Fixture
    {
        internal EphemeralDataProtectionProvider Provider { get; } = new();
        internal ControlledCache Cache { get; } = new();
        internal AdjustableTime Time { get; } = new();
        internal DistributedInstantQuotationSessionStore Store { get; }
        internal AdditiveQuoteTicketService Tickets { get; }
        private IDataProtector Protector => Provider.CreateProtector(DistributedInstantQuotationSessionStore.ProtectorPurpose);

        internal Fixture()
        {
            Store = new(Cache, Provider, Time, NullLogger<DistributedInstantQuotationSessionStore>.Instance);
            Tickets = new(Provider);
        }

        internal static string Key(string id) => DistributedInstantQuotationSessionStore.CacheKeyPrefix + id;
        internal byte[] Payload(string id) => Protector.Unprotect(Cache.Get(Key(id))!);

        internal async Task ReplaceProfileAsync(string id, JsonArray? profile, bool absent = false)
        {
            var document = JsonNode.Parse(Payload(id))!;
            var geometry = document["RequestState"]!["Parts"]![0]!["Geometry"]!.AsObject();
            if (absent) geometry.Remove("UnsupportedAreaProfileMm2");
            else geometry["UnsupportedAreaProfileMm2"] = profile;
            await Cache.SetAsync(Key(id), Protector.Protect(JsonSerializer.SerializeToUtf8Bytes(document)), new(), default);
        }
    }

    // A controlled byte-cache boundary: these are serialization/CAS unit tests, not Redis integration proof.
    private sealed class ControlledCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);
        internal int Writes { get; private set; }
        internal DateTimeOffset? LastExpiration { get; private set; }
        public byte[]? Get(string key) => values.TryGetValue(key, out var value) ? value.ToArray() : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            values[key] = value.ToArray();
            Writes++;
            LastExpiration = options.AbsoluteExpiration;
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => values.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class AdjustableTime : TimeProvider
    {
        private DateTimeOffset now = Now;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class ObservedPricing : IInstantQuotationAuthoritativePricingService
    {
        internal IReadOnlyList<double> UnsupportedSamples { get; private set; } = [];
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? ownerIdentity, bool includeComparisons, CancellationToken cancellationToken)
        {
            UnsupportedSamples = session.Parts[0].Geometry.UnsupportedAreaProfileMm2.ToArray();
            return Task.FromResult<InstantQuotationOrderQuote?>(new InstantQuotationPricingService().Quote(session.RequestState));
        }
    }

    private sealed class UnusedUploadClient : IInstantQuotationUploadClient
    {
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content, string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No upload is expected during protected-session restoration.");
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity, InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No removal is expected during protected-session restoration.");
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity, int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No finalization is expected during protected-session restoration.");
    }
}
