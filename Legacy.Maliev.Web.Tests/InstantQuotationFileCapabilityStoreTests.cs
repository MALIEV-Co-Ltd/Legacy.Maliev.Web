using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationFileCapabilityStoreTests
{
    private const string WebSessionId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Extensions = [".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf"];

    [Fact]
    public async Task PutGetRemove_ExactOwnerBinding_ReturnsProtectedCapabilitySnapshot()
    {
        var fixture = CreateFixture();
        var sourceExtensions = Extensions.ToArray();
        var capability = Capability(sourceExtensions);

        Assert.True(await fixture.Store.PutAsync(WebSessionId, "member-42", capability, default));
        sourceExtensions[0] = ".exe";
        var found = await fixture.Store.GetAsync(WebSessionId, "member-42", default);
        var removed = await fixture.Store.RemoveAsync(WebSessionId, "member-42", default);

        Assert.NotNull(found);
        Assert.Equal(capability.SessionId, found.SessionId);
        Assert.Equal(capability.SessionToken, found.SessionToken);
        Assert.Equal(Extensions, found.SupportedExtensions);
        Assert.True(removed);
        Assert.Null(await fixture.Store.GetAsync(WebSessionId, "member-42", default));
    }

    [Fact]
    public async Task Operations_OwnerMismatchOrAnonymousMismatch_FailClosedWithoutDisclosure()
    {
        var fixture = CreateFixture();
        Assert.True(await fixture.Store.PutAsync(WebSessionId, "member-42", Capability(), default));

        Assert.Null(await fixture.Store.GetAsync(WebSessionId, "member-99", default));
        Assert.Null(await fixture.Store.GetAsync(WebSessionId, null, default));
        Assert.False(await fixture.Store.RemoveAsync(WebSessionId, "member-99", default));
        Assert.NotNull(await fixture.Store.GetAsync(WebSessionId, "member-42", default));
    }

    [Fact]
    public async Task StoredPayload_IsVersionedProtectedAndContainsNoCapabilityOrOwnerMaterial()
    {
        var fixture = CreateFixture();
        var capability = Capability();
        Assert.True(await fixture.Store.PutAsync(WebSessionId, "member-sensitive", capability, default));

        var raw = await fixture.Cache.GetAsync(InstantQuotationFileCapabilityStore.CacheKeyPrefix + WebSessionId);

        Assert.NotNull(raw);
        var rawText = Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("member-sensitive", rawText, StringComparison.Ordinal);
        Assert.DoesNotContain(capability.SessionToken, rawText, StringComparison.Ordinal);
        Assert.DoesNotContain(capability.SessionId.ToString("D"), rawText, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(fixture.Protector.Unprotect(raw));
        Assert.Equal(1, document.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public async Task GetAsync_ExpiredCapability_RemovesPayload()
    {
        var fixture = CreateFixture();
        Assert.True(await fixture.Store.PutAsync(WebSessionId, null, Capability(), default));
        fixture.Time.Advance(TimeSpan.FromHours(1));

        Assert.Null(await fixture.Store.GetAsync(WebSessionId, null, default));
        Assert.Null(await fixture.Cache.GetAsync(InstantQuotationFileCapabilityStore.CacheKeyPrefix + WebSessionId));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(209_715_200, 0)]
    [InlineData(209_715_201, 100)]
    [InlineData(209_715_200, 101)]
    public async Task PutAsync_NonContractLimits_FailsClosedWithoutWriting(long maxUploadBytes, int maxFiles)
    {
        var fixture = CreateFixture();
        var capability = Capability() with { MaxUploadBytes = maxUploadBytes, MaxFilesPerSession = maxFiles };

        Assert.False(await fixture.Store.PutAsync(WebSessionId, null, capability, default));
        Assert.Null(await fixture.Cache.GetAsync(InstantQuotationFileCapabilityStore.CacheKeyPrefix + WebSessionId));
    }

    [Fact]
    public async Task PutAsync_UnknownExtensionOrInvalidIdentity_FailsClosedWithoutWriting()
    {
        var fixture = CreateFixture();

        Assert.False(await fixture.Store.PutAsync(WebSessionId, null, Capability([.. Extensions, ".zip"]), default));
        Assert.False(await fixture.Store.PutAsync(WebSessionId, null, Capability(Extensions.Reverse().ToArray()), default));
        Assert.False(await fixture.Store.PutAsync("not-a-protected-session", null, Capability(), default));
        Assert.Null(await fixture.Cache.GetAsync(InstantQuotationFileCapabilityStore.CacheKeyPrefix + WebSessionId));
    }

    [Fact]
    public async Task PutAsync_ExpiredOrMalformedCapability_FailsClosedWithoutWriting()
    {
        var fixture = CreateFixture();

        Assert.False(await fixture.Store.PutAsync(
            WebSessionId,
            null,
            Capability() with { SessionId = Guid.Empty },
            default));
        Assert.False(await fixture.Store.PutAsync(
            WebSessionId,
            null,
            Capability() with { SessionToken = "too-short" },
            default));
        Assert.False(await fixture.Store.PutAsync(
            WebSessionId,
            null,
            Capability() with { ExpiresAt = Now },
            default));
        Assert.False(await fixture.Store.PutAsync(
            WebSessionId,
            " ",
            Capability(),
            default));
        Assert.Null(await fixture.Cache.GetAsync(InstantQuotationFileCapabilityStore.CacheKeyPrefix + WebSessionId));
    }

    [Fact]
    public async Task GetAsync_CorruptProtectedPayload_RemovesIt()
    {
        var fixture = CreateFixture();
        await fixture.Cache.SetAsync(
            InstantQuotationFileCapabilityStore.CacheKeyPrefix + WebSessionId,
            fixture.Protector.Protect("not-json"u8.ToArray()));

        Assert.Null(await fixture.Store.GetAsync(WebSessionId, null, default));
        Assert.Null(await fixture.Cache.GetAsync(InstantQuotationFileCapabilityStore.CacheKeyPrefix + WebSessionId));
    }

    [Fact]
    public void CapabilityContract_IsInternalAndNeverPartOfPublicUploadResults()
    {
        Assert.False(typeof(InstantQuotationFileCapability).IsPublic);
        Assert.False(typeof(IInstantQuotationFileCapabilityStore).IsPublic);
        Assert.DoesNotContain(
            typeof(InstantQuotationUploadResult).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("SessionId", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Bucket", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Object", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Registration_AddsOnlyProtectedCapabilityStoreAndKeepsRuntimeUploadUnavailable()
    {
        var services = new ServiceCollection();
        services.AddLegacyServiceClients(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IInstantQuotationFileCapabilityStore)
                && descriptor.ImplementationType == typeof(InstantQuotationFileCapabilityStore)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IInstantQuotationUploadClient)
                && descriptor.ImplementationType == typeof(UnavailableInstantQuotationUploadClient));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(InstantQuotationFileServiceTransport));
    }

    private static InstantQuotationFileCapability Capability(IReadOnlyList<string>? extensions = null) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "opaque-capability-token-0000000000000000",
        Now.AddHours(1),
        209_715_200,
        100,
        extensions ?? Extensions);

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
        var store = new InstantQuotationFileCapabilityStore(
            cache,
            provider,
            time,
            NullLogger<InstantQuotationFileCapabilityStore>.Instance);
        return new Fixture(
            store,
            cache,
            time,
            provider.CreateProtector(InstantQuotationFileCapabilityStore.ProtectorPurpose));
    }

    private sealed record Fixture(
        InstantQuotationFileCapabilityStore Store,
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
