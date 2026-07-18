using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationSessionStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

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
        Assert.DoesNotContain("PLA", rawText, StringComparison.Ordinal);
        var payload = fixture.Protector.Unprotect(raw);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(1, document.RootElement.GetProperty("Version").GetInt32());
        var propertyNames = typeof(InstantQuotationSessionState).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateAsync_SourceCollectionsMutate_PersistedSessionAndPricingRemainUnchanged()
    {
        var fixture = CreateFixture();
        var areas = new[] { 500.0, 501.0 };
        var perimeters = new[] { 80.0, 81.0 };
        var originalPart = Part("PLA", areas, perimeters);
        var sourceParts = new[] { originalPart };
        var created = await fixture.Store.CreateAsync("customer-42", new InstantQuotationOrderState(sourceParts), default);
        var pricing = new InstantQuotationPricingService();
        var before = pricing.Quote(created.RequestState);

        areas[0] = -1;
        perimeters[0] = -1;
        sourceParts[0] = Part("PETG", [1], [1]);

        var found = await fixture.Store.GetAsync(created.SessionId, "customer-42", default);
        var after = pricing.Quote(found!.RequestState);
        Assert.NotSame(originalPart.Geometry, created.Parts.Single().Geometry);
        Assert.NotSame(created.Parts.Single().Geometry, found.Parts.Single().Geometry);
        Assert.Equal("PLA", found.Parts.Single().Configuration.MaterialKey);
        Assert.Equal([500.0, 501.0], found.Parts.Single().Geometry.AreaProfileMm2);
        Assert.Equal([80.0, 81.0], found.Parts.Single().Geometry.PerimeterProfileMm);
        Assert.Equal(before.FinalOrderPrice, after.FinalOrderPrice);
        Assert.Equal(before.Parts.Single().PrintTimeMinutesPerUnit, after.Parts.Single().PrintTimeMinutesPerUnit);
        Assert.False(found.Parts is IList<InstantQuotationPart>);
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
            Part(materialKey, Enumerable.Repeat(500.0, 40).ToArray(), Enumerable.Repeat(80.0, 40).ToArray()),
        ]);
    }

    private static InstantQuotationPart Part(string materialKey, double[] areas, double[] perimeters)
    {
        var upload = InstantQuotationUploadResult.Succeeded(
            "operation-1",
            new InstantQuotationUploadReference("opaque-1"),
            new InstantQuotationGeometry(30, 20_000, 400, areas, perimeters, 1_024, 1, true));
        return new InstantQuotationPart(
            Guid.NewGuid(),
            "part.stl",
            upload.UploadReference!,
            upload.AuthoritativeGeometry!,
            new InstantQuotationPartConfiguration(materialKey, "Black", 1));
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
