using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Legacy.Maliev.Web.Tests;

public sealed class RedisRuntimeIntegrationTests(RedisRuntimeFixture fixture)
    : IClassFixture<RedisRuntimeFixture>
{
    [Fact]
    public async Task NonTestingStartup_ReadinessUsesCompatibilityProtocol()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/web/readiness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var multiplexer = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var endpoint = Assert.Single(multiplexer.GetEndPoints());
        Assert.Equal(RedisProtocol.Resp2, multiplexer.GetServer(endpoint).Protocol);
    }

    [Fact]
    public async Task DistributedCache_UsesPrefixAndSupportsSetGetRemove()
    {
        using var factory = fixture.CreateFactory();
        var cache = factory.Services.GetRequiredService<IDistributedCache>();
        var multiplexer = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var database = multiplexer.GetDatabase();
        var key = $"integration:{Guid.NewGuid():N}";
        var redisKey = $"legacy:web:{key}";
        var value = Encoding.UTF8.GetBytes("redis-8.4-cache-value");

        await cache.SetAsync(
            key,
            value,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
            });

        Assert.True(await database.KeyExistsAsync(redisKey));
        Assert.Equal(value, await cache.GetAsync(key));

        await cache.RemoveAsync(key);

        Assert.False(await database.KeyExistsAsync(redisKey));
    }

    [Fact]
    public async Task EncryptedSessionAndDataProtectionKeyReloadAcrossInstances()
    {
        var sessionId = $"integration-{Guid.NewGuid():N}";
        var session = new AccountSession(
            "redis-integration@example.com",
            42,
            "pre-upgrade-access-token",
            "pre-upgrade-refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(1));

        using (var firstFactory = fixture.CreateFactory())
        {
            var firstStore = firstFactory.Services.GetRequiredService<IAccountSessionStore>();
            await firstStore.SetAsync(sessionId, session, default);

            var database = firstFactory.Services
                .GetRequiredService<IConnectionMultiplexer>()
                .GetDatabase();
            var redisSessionKey =
                $"legacy:web:{DistributedAccountSessionStore.CacheKeyPrefix}{sessionId}";
            var rawFields = await database.HashGetAllAsync(redisSessionKey);
            var keyRingExists = await database.KeyExistsAsync("legacy:web:data-protection-keys");
            var rawText = string.Join(
                '\n',
                rawFields.Select(field => Encoding.UTF8.GetString((byte[])field.Value!)));

            Assert.NotEmpty(rawFields);
            Assert.DoesNotContain(
                "pre-upgrade-access-token",
                rawText,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "pre-upgrade-refresh-token",
                rawText,
                StringComparison.Ordinal);
            Assert.True(keyRingExists);
        }

        using var secondFactory = fixture.CreateFactory();
        var secondStore = secondFactory.Services.GetRequiredService<IAccountSessionStore>();

        Assert.Equal(session, await secondStore.GetAsync(sessionId, default));
    }

    [Fact]
    public async Task RefreshLockContendsAndReleasesAcrossMultiplexers()
    {
        using var firstFactory = fixture.CreateFactory();
        using var secondFactory = fixture.CreateFactory();
        var firstMultiplexer = firstFactory.Services.GetRequiredService<IConnectionMultiplexer>();
        var secondMultiplexer = secondFactory.Services.GetRequiredService<IConnectionMultiplexer>();
        var firstStore = firstFactory.Services.GetRequiredService<IAccountSessionStore>();
        var secondStore = secondFactory.Services.GetRequiredService<IAccountSessionStore>();
        var sessionId = $"lock-{Guid.NewGuid():N}";

        Assert.NotSame(firstMultiplexer, secondMultiplexer);
        var firstLock = await firstStore.AcquireRefreshLockAsync(sessionId, default);
        Assert.NotNull(firstLock);

        var blockedLock = await secondStore.AcquireRefreshLockAsync(sessionId, default);
        Assert.Null(blockedLock);

        await firstLock.DisposeAsync();
        var releasedLock = await secondStore.AcquireRefreshLockAsync(sessionId, default);
        Assert.NotNull(releasedLock);
        await releasedLock.DisposeAsync();
    }
}

public sealed class RedisRuntimeFixture : IAsyncLifetime
{
    private const string CertificatePassword = "redis-integration-test";
    private readonly RedisContainer redis = new RedisBuilder("redis:8.4-alpine").Build();
    private readonly string certificatePfxBase64 = CreateCertificatePfxBase64();

    public Task InitializeAsync() => redis.StartAsync();

    public Task DisposeAsync() => redis.DisposeAsync().AsTask();

    public WebApplicationFactory<Program> CreateFactory() =>
        new RedisWebApplicationFactory(
            redis.GetConnectionString(),
            certificatePfxBase64,
            CertificatePassword);

    private static string CreateCertificatePfxBase64()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Legacy.Maliev.Web Redis Integration",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, CertificatePassword));
    }

    private sealed class RedisWebApplicationFactory(
        string redisConnectionString,
        string pfxBase64,
        string pfxPassword) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder
                .UseEnvironment("RedisIntegration")
                .UseSetting("ConnectionStrings:redis", redisConnectionString)
                .UseSetting("DataProtection:CertificatePfxBase64", pfxBase64)
                .UseSetting("DataProtection:CertificatePassword", pfxPassword)
                .UseSetting("ServiceAuthentication:ClientSecret", "redis-integration-secret");
        }
    }
}
