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
        var propertyNames = typeof(InstantQuotationSession).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase));
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
        var upload = InstantQuotationUploadResult.Succeeded(
            "operation-1",
            new InstantQuotationUploadReference("opaque-1"),
            Geometry());
        return new InstantQuotationOrderState(
        [
            new InstantQuotationPart(
                Guid.NewGuid(),
                "part.stl",
                upload.UploadReference!,
                upload.AuthoritativeGeometry!,
                new InstantQuotationPartConfiguration(materialKey, "Black", 1)),
        ]);
    }

    private static InstantQuotationGeometry Geometry() => new(
        30,
        20_000,
        400,
        Enumerable.Repeat(500.0, 40).ToArray(),
        Enumerable.Repeat(80.0, 40).ToArray(),
        1_024,
        1,
        true);

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
