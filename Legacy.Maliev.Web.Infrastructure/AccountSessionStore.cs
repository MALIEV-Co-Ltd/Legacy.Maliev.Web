using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed record AccountSession(
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt);

internal interface IAccountSessionStore
{
    Task<AccountSession?> GetAsync(string sessionId, CancellationToken cancellationToken);
    Task SetAsync(string sessionId, AccountSession session, CancellationToken cancellationToken);
    Task RemoveAsync(string sessionId, CancellationToken cancellationToken);
    ValueTask<IAsyncDisposable?> AcquireRefreshLockAsync(string sessionId, CancellationToken cancellationToken);
}

internal sealed class DistributedAccountSessionStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILogger<DistributedAccountSessionStore> logger) : IAccountSessionStore
{
    internal const string CacheKeyPrefix = "legacy:web:session:";
    private static readonly TimeSpan LockLifetime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(
        "Legacy.Maliev.Web.AccountSession.v1");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> localLocks = new(StringComparer.Ordinal);

    public async Task<AccountSession?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var protectedPayload = await cache.GetAsync(Key(sessionId), cancellationToken);
        if (protectedPayload is null)
        {
            return null;
        }

        try
        {
            var payload = protector.Unprotect(protectedPayload);
            return JsonSerializer.Deserialize<AccountSession>(payload);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            logger.LogWarning(exception, "Rejected an unreadable customer session.");
            await RemoveAsync(sessionId, cancellationToken);
            return null;
        }
    }

    public Task SetAsync(
        string sessionId,
        AccountSession session,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(session);
        var protectedPayload = protector.Protect(payload);
        return cache.SetAsync(
            Key(sessionId),
            protectedPayload,
            new DistributedCacheEntryOptions { AbsoluteExpiration = session.RefreshExpiresAt },
            cancellationToken);
    }

    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(Key(sessionId), cancellationToken);

    public async ValueTask<IAsyncDisposable?> AcquireRefreshLockAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var multiplexer = services.GetService<IConnectionMultiplexer>();
        if (multiplexer is null)
        {
            var semaphore = localLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);
            return new LocalLock(semaphore);
        }

        var database = multiplexer.GetDatabase();
        var key = $"legacy:web:session-refresh-lock:{sessionId}";
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var deadline = timeProvider.GetUtcNow().Add(LockWait);
        while (timeProvider.GetUtcNow() < deadline)
        {
            if (await database.LockTakeAsync(key, value, LockLifetime))
            {
                return new RedisLock(database, key, value);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeProvider, cancellationToken);
        }

        logger.LogWarning("Timed out waiting for a customer session refresh lock.");
        return null;
    }

    private static string Key(string sessionId) => $"{CacheKeyPrefix}{sessionId}";

    private sealed class LocalLock(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RedisLock(IDatabase database, RedisKey key, RedisValue value) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await database.LockReleaseAsync(key, value);
    }
}
