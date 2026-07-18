using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed record InstantQuotationSession(
    int Version,
    string SessionId,
    string? OwnerIdentity,
    string SubmissionId,
    InstantQuotationOrderState RequestState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal IReadOnlyList<InstantQuotationPart> Parts => RequestState.Parts;
}

internal interface IInstantQuotationSessionStore
{
    Task<InstantQuotationSession> CreateAsync(
        string? ownerIdentity,
        InstantQuotationOrderState requestState,
        CancellationToken cancellationToken);

    Task<InstantQuotationSession?> GetAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken);

    Task<bool> PutAsync(
        InstantQuotationSession session,
        string? ownerIdentity,
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken);
}

internal sealed class DistributedInstantQuotationSessionStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    ILogger<DistributedInstantQuotationSessionStore> logger) : IInstantQuotationSessionStore
{
    internal const int CurrentVersion = 1;
    internal const string CacheKeyPrefix = "legacy:web:instant-quotation-session:";
    internal const string ProtectorPurpose = "Legacy.Maliev.Web.InstantQuotationSession.v1";
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<InstantQuotationSession> CreateAsync(
        string? ownerIdentity,
        InstantQuotationOrderState requestState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestState);
        var now = timeProvider.GetUtcNow();
        var session = new InstantQuotationSession(
            CurrentVersion,
            RandomIdentifier(),
            ownerIdentity,
            RandomIdentifier(),
            Snapshot(requestState),
            now,
            now);
        await WriteAsync(session, cancellationToken);
        return session;
    }

    public async Task<InstantQuotationSession?> GetAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        var session = await ReadAsync(sessionId, cancellationToken);
        return session is not null && OwnerMatches(session.OwnerIdentity, ownerIdentity)
            ? session
            : null;
    }

    public async Task<bool> PutAsync(
        InstantQuotationSession session,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var existing = await ReadAsync(session.SessionId, cancellationToken);
        if (existing is null || !OwnerMatches(existing.OwnerIdentity, ownerIdentity))
        {
            return false;
        }

        var updated = existing with
        {
            RequestState = Snapshot(session.RequestState),
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        await WriteAsync(updated, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        var existing = await ReadAsync(sessionId, cancellationToken);
        if (existing is null || !OwnerMatches(existing.OwnerIdentity, ownerIdentity))
        {
            return false;
        }

        await cache.RemoveAsync(Key(sessionId), cancellationToken);
        return true;
    }

    private async Task<InstantQuotationSession?> ReadAsync(
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
            var session = JsonSerializer.Deserialize<InstantQuotationSession>(payload);
            if (session is null
                || session.Version != CurrentVersion
                || !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal)
                || session.CreatedAt.Add(SessionLifetime) <= timeProvider.GetUtcNow())
            {
                await cache.RemoveAsync(Key(sessionId), cancellationToken);
                return null;
            }

            return session with { RequestState = Snapshot(session.RequestState) };
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            logger.LogWarning(exception, "Rejected an unreadable instant quotation session.");
            await cache.RemoveAsync(Key(sessionId), cancellationToken);
            return null;
        }
    }

    private Task WriteAsync(InstantQuotationSession session, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(session);
        var protectedPayload = protector.Protect(payload);
        return cache.SetAsync(
            Key(session.SessionId),
            protectedPayload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session.CreatedAt.Add(SessionLifetime),
            },
            cancellationToken);
    }

    private static InstantQuotationOrderState Snapshot(InstantQuotationOrderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new InstantQuotationOrderState(state.Parts.ToArray());
    }

    private static bool OwnerMatches(string? actual, string? expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal);

    private static string RandomIdentifier() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string Key(string sessionId) => $"{CacheKeyPrefix}{sessionId}";
}
