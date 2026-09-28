using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationSessionStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddDays(1);

    [Fact]
    public async Task CreateAsync_RepeatedRequests_CreateRandomSessionAndStableSubmissionIdentities()
    {
        var fixture = CreateFixture();

        var first = await fixture.Store.CreateAsync("customer-42", State(), default);
        var second = await fixture.Store.CreateAsync("customer-42", State(), default);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.NotEqual(first.SubmissionId, second.SubmissionId);
        Assert.Single(first.Parts);
        Assert.Equal(first.SubmissionId, (await fixture.Store.GetAsync(first.SessionId, "customer-42", default))!.SubmissionId);
        Assert.Matches("^[0-9A-F]{64}$", first.SessionId);
        Assert.Matches("^[0-9A-F]{64}$", first.SubmissionId);
    }

    [Fact]
    public async Task CreateGetPutRemove_OwnerMatches_PersistsRequestStateAndTimestamps()
    {
        var fixture = CreateFixture();
        var created = await fixture.Store.CreateAsync("customer-42", State("PLA"), default);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var updatedState = State("PETG");

        var updated = await fixture.Store.PutAsync(created with { RequestState = updatedState }, "customer-42", default);
        var found = await fixture.Store.GetAsync(created.SessionId, "customer-42", default);
        var removed = await fixture.Store.RemoveAsync(created.SessionId, "customer-42", default);

        Assert.True(updated);
        Assert.NotNull(found);
        Assert.Equal("PETG", found.RequestState.Parts.Single().Configuration.MaterialKey);
        Assert.Equal(created.CreatedAt, found.CreatedAt);
        Assert.Equal(Now.AddMinutes(5), found.UpdatedAt);
        Assert.Equal(created.SubmissionId, found.SubmissionId);
        Assert.True(removed);
        Assert.Null(await fixture.Store.GetAsync(created.SessionId, "customer-42", default));
    }

    [Fact]
    public async Task PutAsync_ConcurrentSameRevision_OnlyOneAuthorizationCanWin()
    {
        var fixture = CreateFixture();
        var created = await fixture.Store.CreateAsync("customer-42", State(), default);
        var first = created with
        {
            QuoteAuthorization = new InstantQuotationQuoteAuthorization(["first"], "first-order"),
        };
        var second = created with
        {
            QuoteAuthorization = new InstantQuotationQuoteAuthorization(["second"], "second-order"),
        };

        bool[] writes = await Task.WhenAll(
            fixture.Store.PutAsync(first, "customer-42", default),
            fixture.Store.PutAsync(second, "customer-42", default));

        Assert.Single(writes, static accepted => accepted);
        var current = await fixture.Store.GetAsync(created.SessionId, "customer-42", default);
        Assert.NotNull(current?.QuoteAuthorization);
        Assert.Equal(writes[0] ? "first-order" : "second-order", current.QuoteAuthorization.OrderTicket);
    }

    [Fact]
    public async Task CreateGet_BuildPreference_PersistsPerPartInProtectedCache()
    {
        var fixture = CreateFixture();
        var state = new InstantQuotationOrderState(
            [Part(
                "PLA",
                Enumerable.Repeat(500.0, 64).ToArray(),
                Enumerable.Repeat(80.0, 64).ToArray(),
                BuildPreference.Strength)]);

        var created = await fixture.Store.CreateAsync("customer-42", state, default);
        var found = await fixture.Store.GetAsync(created.SessionId, "customer-42", default);

        Assert.Equal(BuildPreference.Strength, found!.Parts.Single().Configuration.BuildPreference);
    }

    [Fact]
    public async Task CreateGet_PhysicalAnalysisDescriptor_RoundTripsOnlyForOwner()
    {
        var fixture = CreateFixture();
        var fileId = Guid.NewGuid();
        var part = Part("PLA", Enumerable.Repeat(500.0, 64).ToArray(), Enumerable.Repeat(80.0, 64).ToArray());
        var descriptor = new InstantQuotationPhysicalAnalysisUpload(
            fileId, "part.stl", "model/stl", 128, part.Geometry.Sha256, "clean");
        var state = new InstantQuotationOrderState(
            [part with { UploadReference = new InstantQuotationUploadReference(fileId.ToString("D")), PhysicalAnalysisUpload = descriptor }]);

        var created = await fixture.Store.CreateAsync("customer-42", state, default);
        var found = await fixture.Store.GetAsync(created.SessionId, "customer-42", default);

        Assert.Equal(descriptor, found!.Parts.Single().PhysicalAnalysisUpload);
        Assert.Null(await fixture.Store.GetAsync(created.SessionId, "customer-99", default));
        var raw = await fixture.Cache.GetAsync(DistributedInstantQuotationSessionStore.CacheKeyPrefix + created.SessionId);
        Assert.NotNull(raw);
        Assert.DoesNotContain(descriptor.Sha256, Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public async Task ProtectedSession_UsesCurrentAdditiveQuantityBoundary(int quantity, bool accepted)
    {
        var fixture = CreateFixture();
        var part = Part("PLA", Enumerable.Repeat(500.0, 64).ToArray(),
            Enumerable.Repeat(80.0, 64).ToArray());
        var created = await fixture.Store.CreateAsync("customer-42",
            new InstantQuotationOrderState(
                [part with { Configuration = part.Configuration with { Quantity = quantity } }]), default);

        var restored = await fixture.Store.GetAsync(created.SessionId, "customer-42", default);

        Assert.Equal(accepted, restored is not null);
        if (accepted)
        {
            Assert.Equal(quantity, restored!.Parts.Single().Configuration.Quantity);
        }
    }

    [Fact]
    public async Task GetAsync_DescriptorDigestNotBoundToGeometry_RejectsProtectedSession()
    {
        var fixture = CreateFixture();
        var fileId = Guid.NewGuid();
        var part = Part("PLA", Enumerable.Repeat(500.0, 64).ToArray(), Enumerable.Repeat(80.0, 64).ToArray());
        var descriptor = new InstantQuotationPhysicalAnalysisUpload(
            fileId, "part.stl", "model/stl", 128, new string('b', 64), "clean");
        var state = new InstantQuotationOrderState(
            [part with { UploadReference = new InstantQuotationUploadReference(fileId.ToString("D")), PhysicalAnalysisUpload = descriptor }]);

        var created = await fixture.Store.CreateAsync("customer-42", state, default);

        Assert.Null(await fixture.Store.GetAsync(created.SessionId, "customer-42", default));
        Assert.Null(await fixture.Cache.GetAsync(DistributedInstantQuotationSessionStore.CacheKeyPrefix + created.SessionId));
    }

    [Fact]
    public async Task Operations_OwnerMismatch_RejectWithoutDisclosingOrMutatingSession()
    {
        var fixture = CreateFixture();
        var created = await fixture.Store.CreateAsync("customer-42", State("PLA"), default);

        Assert.Null(await fixture.Store.GetAsync(created.SessionId, "customer-99", default));
        Assert.False(await fixture.Store.PutAsync(created with { RequestState = State("PETG") }, "customer-99", default));
        Assert.False(await fixture.Store.RemoveAsync(created.SessionId, "customer-99", default));
        Assert.Equal(
            "PLA",
            (await fixture.Store.GetAsync(created.SessionId, "customer-42", default))!
                .RequestState.Parts.Single().Configuration.MaterialKey);
    }

    [Fact]
    public async Task GetAsync_AfterFixedExpiry_RemovesSession()
    {
        var fixture = CreateFixture();
        var created = await fixture.Store.CreateAsync(null, State(), default);

        fixture.Time.Advance(DistributedInstantQuotationSessionStore.SessionLifetime);

        Assert.Null(await fixture.Store.GetAsync(created.SessionId, null, default));
        Assert.Null(await fixture.Cache.GetAsync(DistributedInstantQuotationSessionStore.CacheKeyPrefix + created.SessionId));
    }

    [Fact]
    public async Task StoredPayload_IsProtectedVersionedAndContainsNoCredentialSurface()
    {
        var fixture = CreateFixture();
        var created = await fixture.Store.CreateAsync("owner-sensitive", State(), default);

        var raw = await fixture.Cache.GetAsync(DistributedInstantQuotationSessionStore.CacheKeyPrefix + created.SessionId);

        Assert.NotNull(raw);
        var rawText = Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("owner-sensitive", rawText, StringComparison.Ordinal);
        var payload = fixture.Protector.Unprotect(raw);
        Assert.NotEqual(payload, raw);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(1, document.RootElement.GetProperty("Version").GetInt32());
        var propertyNames = typeof(InstantQuotationSessionState).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateAsync_SourceCollectionsMutate_PersistedSessionRemainsUnchangedAndFdmIsUnpriced()
    {
        var fixture = CreateFixture();
        var areas = Enumerable.Repeat(500.0, 64).ToArray();
        var perimeters = Enumerable.Repeat(80.0, 64).ToArray();
        areas[1] = 501;
        perimeters[1] = 81;
        var originalPart = Part("PLA", areas, perimeters);
        var sourceParts = new[] { originalPart };
        var created = await fixture.Store.CreateAsync("customer-42", new InstantQuotationOrderState(sourceParts), default);
        var pricing = new InstantQuotationPricingService();
        Assert.Throws<InvalidOperationException>(() => pricing.Quote(created.RequestState));

        areas[0] = -1;
        perimeters[0] = -1;
        sourceParts[0] = Part("PETG", [1], [1]);

        var found = await fixture.Store.GetAsync(created.SessionId, "customer-42", default);
        Assert.NotNull(found);
        Assert.Throws<InvalidOperationException>(() => pricing.Quote(found.RequestState));
        Assert.NotSame(originalPart.Geometry, created.Parts.Single().Geometry);
        Assert.NotSame(created.Parts.Single().Geometry, found.Parts.Single().Geometry);
        Assert.Equal("PLA", found.Parts.Single().Configuration.MaterialKey);
        Assert.Equal([500.0, 501.0], found.Parts.Single().Geometry.AreaProfileMm2.Take(2));
        Assert.Equal([80.0, 81.0], found.Parts.Single().Geometry.PerimeterProfileMm.Take(2));
        Assert.Equal(20, found.Parts.Single().Geometry.DimensionXmm);
        Assert.Equal(25, found.Parts.Single().Geometry.DimensionYmm);
        Assert.Equal(2_200, found.Parts.Single().Geometry.SurfaceAreaMm2);
        Assert.Equal(0.8, found.Parts.Single().Geometry.MinThicknessMm);
        Assert.True(found.Parts.Single().Geometry.TopologyChecked);
        Assert.False(found.Parts is IList<InstantQuotationPart>);
    }

    [Fact]
    public async Task PutAsync_PersistsOwnerBoundPhysicalReceiptAndTicketsButRejectsStaleRevision()
    {
        var fixture = CreateFixture();
        var fileId = Guid.NewGuid();
        var original = State().Parts.Single();
        var part = original with
        {
            UploadReference = new InstantQuotationUploadReference(fileId.ToString("D")),
            PhysicalAnalysisUpload = new InstantQuotationPhysicalAnalysisUpload(
                fileId, "part.stl", "model/stl", 100, original.Geometry.Sha256, "clean"),
        };
        var created = await fixture.Store.CreateAsync("owner-1", new InstantQuotationOrderState([part]), default);
        var receipt = new InstantQuotationPhysicalAnalysisReceipt(
            created.SessionId, "owner-1", part.PartId, fileId, part.Geometry.Sha256,
            "PLA", "PLA", BuildPreference.Standard, 1, "profile-v1", new string('B', 64),
            "analysis-v1", new string('C', 64), 1000, 100, 3600, 2);
        var updated = created with
        {
            PhysicalReceipts = [receipt],
            QuoteAuthorization = new InstantQuotationQuoteAuthorization(["line-ticket"], "order-ticket"),
        };

        Assert.True(await fixture.Store.PutAsync(updated, "owner-1", default));
        Assert.False(await fixture.Store.PutAsync(updated, "owner-1", default));
        var restored = await fixture.Store.GetAsync(created.SessionId, "owner-1", default);
        Assert.NotNull(restored);
        Assert.Equal("owner-1", restored.OwnerIdentity);
        Assert.Equal(receipt, Assert.Single(restored.PhysicalReceipts!));
        Assert.Equal("line-ticket", Assert.Single(restored.QuoteAuthorization!.LineTickets));
        Assert.Equal("order-ticket", restored.QuoteAuthorization.OrderTicket);
        Assert.Null(await fixture.Store.GetAsync(created.SessionId, "other-owner", default));
    }

    [Fact]
    public async Task GetAsync_VersionOnePayloadMissingRequestState_RejectsAndRemovesPayload()
    {
        var fixture = CreateFixture();
        const string sessionId = "MISSING-STATE";
        var invalidPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            SessionId = sessionId,
            OwnerIdentity = "customer-42",
            SubmissionId = "submission",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await fixture.Cache.SetAsync(
            DistributedInstantQuotationSessionStore.CacheKeyPrefix + sessionId,
            fixture.Protector.Protect(invalidPayload));

        var result = await fixture.Store.GetAsync(sessionId, "customer-42", default);

        Assert.Null(result);
        Assert.Null(await fixture.Cache.GetAsync(DistributedInstantQuotationSessionStore.CacheKeyPrefix + sessionId));
    }

    [Fact]
    public async Task GetAsync_VersionOnePayloadWithOverflowingExpiry_RejectsAndRemovesPayload()
    {
        var fixture = CreateFixture();
        const string sessionId = "INVALID-EXPIRY";
        var invalidPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            SessionId = sessionId,
            OwnerIdentity = "customer-42",
            SubmissionId = "submission",
            RequestState = new { Parts = Array.Empty<object>() },
            CreatedAt = DateTimeOffset.MaxValue,
            UpdatedAt = DateTimeOffset.MaxValue,
        });
        await fixture.Cache.SetAsync(
            DistributedInstantQuotationSessionStore.CacheKeyPrefix + sessionId,
            fixture.Protector.Protect(invalidPayload));

        var result = await fixture.Store.GetAsync(sessionId, "customer-42", default);

        Assert.Null(result);
        Assert.Null(await fixture.Cache.GetAsync(DistributedInstantQuotationSessionStore.CacheKeyPrefix + sessionId));
    }

    [Fact]
    public void Registration_ExposesPublicApplicationWorkflowSessionAbstraction()
    {
        var applicationType = typeof(InstantQuotationOrderState).Assembly.GetType(
            "Legacy.Maliev.Web.Application.IInstantQuotationSessionStore");
        var services = new ServiceCollection();
        services.AddLegacyServiceClients(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        Assert.NotNull(applicationType);
        Assert.True(applicationType.IsPublic);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == applicationType
                && descriptor.ImplementationType == typeof(DistributedInstantQuotationSessionStore));
    }

    private static Fixture CreateFixture()
    {
        var services = new ServiceCollection()
            .AddDataProtection()
            .Services
            .AddDistributedMemoryCache()
            .BuildServiceProvider();
        var cache = services.GetRequiredService<IDistributedCache>();
        var provider = services.GetRequiredService<IDataProtectionProvider>();
        var time = new AdjustableTimeProvider(Now);
        var store = new DistributedInstantQuotationSessionStore(
            cache,
            provider,
            time,
            NullLogger<DistributedInstantQuotationSessionStore>.Instance);
        return new Fixture(
            store,
            cache,
            time,
            provider.CreateProtector(DistributedInstantQuotationSessionStore.ProtectorPurpose));
    }

    private static InstantQuotationOrderState State(string materialKey = "PLA")
    {
        return new InstantQuotationOrderState(
        [
            Part(materialKey, Enumerable.Repeat(500.0, 64).ToArray(), Enumerable.Repeat(80.0, 64).ToArray()),
        ]);
    }

    private static InstantQuotationPart Part(
        string materialKey,
        double[] areas,
        double[] perimeters,
        BuildPreference buildPreference = BuildPreference.Standard)
    {
        var claim = new InstantQuotationGeometryClaim(
            1,
            new string('a', 64),
            20,
            25,
            30,
            1_000,
            2_200,
            areas,
            perimeters,
            1_024,
            1,
            true,
            false,
            false,
            0.8);
        var upload = InstantQuotationUploadResult.Succeeded(
            "operation-1",
            new InstantQuotationUploadReference("opaque-1"),
            claim.Sha256);
        return new InstantQuotationPart(
            Guid.NewGuid(),
            "part.stl",
            upload.UploadReference!,
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!,
            new InstantQuotationPartConfiguration(materialKey, "Black", 1, buildPreference));
    }

    private sealed record Fixture(
        DistributedInstantQuotationSessionStore Store,
        IDistributedCache Cache,
        AdjustableTimeProvider Time,
        IDataProtector Protector);

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
