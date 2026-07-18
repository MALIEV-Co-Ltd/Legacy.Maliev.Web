using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

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

    public async Task<InstantQuotationSessionState> CreateAsync(
        string? ownerIdentity,
        InstantQuotationOrderState requestState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestState);
        var now = timeProvider.GetUtcNow();
        var session = new InstantQuotationSessionState(
            RandomIdentifier(),
            RandomIdentifier(),
            Snapshot(requestState),
            now,
            now);
        await WriteAsync(session, ownerIdentity, cancellationToken);
        return session;
    }

    public async Task<InstantQuotationSessionState?> GetAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        var persisted = await ReadAsync(sessionId, cancellationToken);
        return persisted is not null && OwnerMatches(persisted.OwnerIdentity, ownerIdentity)
            ? ToSessionState(persisted)
            : null;
    }

    public async Task<bool> PutAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var existing = await ReadAsync(session.SessionId, cancellationToken);
        if (existing is null || !OwnerMatches(existing.OwnerIdentity, ownerIdentity))
        {
            return false;
        }

        var updated = session with
        {
            SessionId = existing.SessionId!,
            SubmissionId = existing.SubmissionId!,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = timeProvider.GetUtcNow(),
            RequestState = Snapshot(session.RequestState),
        };
        await WriteAsync(updated, existing.OwnerIdentity, cancellationToken);
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

    private async Task<PersistedSession?> ReadAsync(
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
            var persisted = JsonSerializer.Deserialize<PersistedSession>(payload);
            if (!IsValid(persisted, sessionId))
            {
                await cache.RemoveAsync(Key(sessionId), cancellationToken);
                return null;
            }

            return persisted;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "Rejected an unreadable instant quotation session.");
            await cache.RemoveAsync(Key(sessionId), cancellationToken);
            return null;
        }
    }

    private Task WriteAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(ToPersisted(session, ownerIdentity));
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

    private bool IsValid(PersistedSession? session, string expectedSessionId)
    {
        if (session is null
            || session.Version != CurrentVersion
            || string.IsNullOrWhiteSpace(session.SessionId)
            || !string.Equals(session.SessionId, expectedSessionId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(session.SubmissionId)
            || session.CreatedAt == default
            || session.UpdatedAt < session.CreatedAt
            || session.CreatedAt > DateTimeOffset.MaxValue.Subtract(SessionLifetime)
            || session.CreatedAt.Add(SessionLifetime) <= timeProvider.GetUtcNow()
            || session.RequestState?.Parts is null)
        {
            return false;
        }

        return session.RequestState.Parts.All(IsValid);
    }

    private static bool IsValid(PersistedPart? part)
    {
        var geometry = part?.Geometry;
        var configuration = part?.Configuration;
        return part is not null
            && part.PartId != Guid.Empty
            && !string.IsNullOrWhiteSpace(part.DisplayFileName)
            && !string.IsNullOrWhiteSpace(part.UploadReference)
            && geometry is not null
            && double.IsFinite(geometry.HeightMm)
            && double.IsFinite(geometry.VolumeMm3)
            && double.IsFinite(geometry.FootprintMm2)
            && geometry.AreaProfileMm2 is not null
            && geometry.AreaProfileMm2.All(double.IsFinite)
            && geometry.PerimeterProfileMm is not null
            && geometry.PerimeterProfileMm.All(double.IsFinite)
            && geometry.FacetCount >= 0
            && geometry.BodyCount >= 1
            && configuration is not null
            && !string.IsNullOrWhiteSpace(configuration.MaterialKey)
            && !string.IsNullOrWhiteSpace(configuration.Color)
            && configuration.Quantity is >= 1 and <= 1_000;
    }

    private static PersistedSession ToPersisted(
        InstantQuotationSessionState session,
        string? ownerIdentity) => new(
            CurrentVersion,
            session.SessionId,
            ownerIdentity,
            session.SubmissionId,
            new PersistedOrderState(session.Parts.Select(ToPersisted).ToArray()),
            session.CreatedAt,
            session.UpdatedAt);

    private static PersistedPart ToPersisted(InstantQuotationPart part)
    {
        var geometry = part.Geometry;
        return new PersistedPart(
            part.PartId,
            part.DisplayFileName,
            part.UploadReference.Value,
            new PersistedGeometry(
                geometry.HeightMm,
                geometry.VolumeMm3,
                geometry.FootprintMm2,
                geometry.AreaProfileMm2.ToArray(),
                geometry.PerimeterProfileMm.ToArray(),
                geometry.FacetCount,
                geometry.BodyCount,
                geometry.IsManifold),
            new PersistedConfiguration(
                part.Configuration.MaterialKey,
                part.Configuration.Color,
                part.Configuration.Quantity));
    }

    private static InstantQuotationSessionState ToSessionState(PersistedSession persisted) => new(
        persisted.SessionId!,
        persisted.SubmissionId!,
        new InstantQuotationOrderState(
            new SnapshotList<InstantQuotationPart>(persisted.RequestState!.Parts!.Select(part => ToPart(part!)))),
        persisted.CreatedAt,
        persisted.UpdatedAt);

    private static InstantQuotationPart ToPart(PersistedPart persisted)
    {
        var geometry = persisted.Geometry!;
        var configuration = persisted.Configuration!;
        return new InstantQuotationPart(
            persisted.PartId,
            persisted.DisplayFileName!,
            new InstantQuotationUploadReference(persisted.UploadReference!),
            AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
                geometry.HeightMm,
                geometry.VolumeMm3,
                geometry.FootprintMm2,
                geometry.AreaProfileMm2!,
                geometry.PerimeterProfileMm!,
                geometry.FacetCount,
                geometry.BodyCount,
                geometry.IsManifold),
            new InstantQuotationPartConfiguration(
                configuration.MaterialKey!,
                configuration.Color!,
                configuration.Quantity));
    }

    private static InstantQuotationOrderState Snapshot(InstantQuotationOrderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Parts);
        return new InstantQuotationOrderState(
            new SnapshotList<InstantQuotationPart>(state.Parts.Select(ClonePart)));
    }

    private static InstantQuotationPart ClonePart(InstantQuotationPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var geometry = part.Geometry;
        return new InstantQuotationPart(
            part.PartId,
            part.DisplayFileName,
            new InstantQuotationUploadReference(part.UploadReference.Value),
            AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
                geometry.HeightMm,
                geometry.VolumeMm3,
                geometry.FootprintMm2,
                geometry.AreaProfileMm2,
                geometry.PerimeterProfileMm,
                geometry.FacetCount,
                geometry.BodyCount,
                geometry.IsManifold),
            part.Configuration with { });
    }

    private static bool OwnerMatches(string? actual, string? expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal);

    private static string RandomIdentifier() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string Key(string sessionId) => $"{CacheKeyPrefix}{sessionId}";

    private sealed record PersistedSession(
        int Version,
        string? SessionId,
        string? OwnerIdentity,
        string? SubmissionId,
        PersistedOrderState? RequestState,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record PersistedOrderState(IReadOnlyList<PersistedPart?>? Parts);

    private sealed record PersistedPart(
        Guid PartId,
        string? DisplayFileName,
        string? UploadReference,
        PersistedGeometry? Geometry,
        PersistedConfiguration? Configuration);

    private sealed record PersistedGeometry(
        double HeightMm,
        double VolumeMm3,
        double FootprintMm2,
        IReadOnlyList<double>? AreaProfileMm2,
        IReadOnlyList<double>? PerimeterProfileMm,
        int FacetCount,
        int BodyCount,
        bool IsManifold);

    private sealed record PersistedConfiguration(
        string? MaterialKey,
        string? Color,
        int Quantity);

    private sealed class SnapshotList<T>(IEnumerable<T> source) : IReadOnlyList<T>
    {
        private readonly T[] values = source.ToArray();

        public int Count => values.Length;

        public T this[int index] => values[index];

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => values.GetEnumerator();
    }
}
