using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class DistributedInstantQuotationSessionStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider,
    ILogger<DistributedInstantQuotationSessionStore> logger,
    IServiceProvider? services = null) : IInstantQuotationSessionStore
{
    internal const int CurrentVersion = 1;
    internal const string CacheKeyPrefix = "legacy:web:instant-quotation-session:";
    internal const string ProtectorPurpose = "Legacy.Maliev.Web.InstantQuotationSession.v1";
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    private readonly object localLockRegistrySync = new();
    private readonly Dictionary<string, LocalLockEntry> localLocks = new(StringComparer.Ordinal);

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
            now,
            OwnerIdentity: ownerIdentity);
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
        await using var mutationLock = await AcquireMutationLockAsync(session.SessionId, cancellationToken);
        if (mutationLock is null)
        {
            return false;
        }

        var existing = await ReadAsync(session.SessionId, cancellationToken);
        if (existing is null || !OwnerMatches(existing.OwnerIdentity, ownerIdentity)
            || session.UpdatedAt != existing.UpdatedAt)
        {
            return false;
        }

        var updated = session with
        {
            SessionId = existing.SessionId!,
            SubmissionId = existing.SubmissionId!,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = timeProvider.GetUtcNow() > existing.UpdatedAt
                ? timeProvider.GetUtcNow()
                : existing.UpdatedAt.AddTicks(1),
            RequestState = Snapshot(session.RequestState),
            OwnerIdentity = existing.OwnerIdentity,
            PhysicalReceipts = session.PhysicalReceipts?.ToArray(),
        };
        await WriteAsync(updated, existing.OwnerIdentity, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        await using var mutationLock = await AcquireMutationLockAsync(sessionId, cancellationToken);
        if (mutationLock is null)
        {
            return false;
        }

        var existing = await ReadAsync(sessionId, cancellationToken);
        if (existing is null || !OwnerMatches(existing.OwnerIdentity, ownerIdentity))
        {
            return false;
        }

        await cache.RemoveAsync(Key(sessionId), cancellationToken);
        return true;
    }

    private async ValueTask<IAsyncDisposable?> AcquireMutationLockAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var multiplexer = services?.GetService<IConnectionMultiplexer>();
        if (multiplexer is null)
        {
            LocalLockEntry entry;
            lock (localLockRegistrySync)
            {
                if (!localLocks.TryGetValue(sessionId, out entry!))
                {
                    entry = new LocalLockEntry();
                    localLocks.Add(sessionId, entry);
                }

                entry.ReferenceCount++;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new LocalLock(this, sessionId, entry);
            }
            catch
            {
                ReleaseLocalLockEntry(sessionId, entry);
                throw;
            }
        }

        var database = multiplexer.GetDatabase();
        var key = $"legacy:web:instant-quotation-session-mutation-lock:{sessionId}";
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var deadline = timeProvider.GetUtcNow().AddSeconds(10);
        while (timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await database.LockTakeAsync(key, value, TimeSpan.FromMinutes(1)))
            {
                return new RedisLock(database, key, value);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeProvider, cancellationToken);
        }

        logger.LogWarning("Timed out waiting for a protected quotation session mutation lock.");
        return null;
    }

    private void ReleaseLocalLockEntry(string sessionId, LocalLockEntry entry)
    {
        lock (localLockRegistrySync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0
                && localLocks.Remove(sessionId, out var removed)
                && ReferenceEquals(removed, entry))
            {
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class LocalLockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class LocalLock(
        DistributedInstantQuotationSessionStore owner,
        string sessionId,
        LocalLockEntry entry) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            entry.Semaphore.Release();
            owner.ReleaseLocalLockEntry(sessionId, entry);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RedisLock(IDatabase database, RedisKey key, RedisValue value) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await database.LockReleaseAsync(key, value);
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

        return session.RequestState.Parts.All(IsValid)
            && (session.PhysicalReceipts is null || session.PhysicalReceipts.All(receipt =>
                receipt is not null
                && string.Equals(receipt.SessionId, session.SessionId, StringComparison.Ordinal)
                && string.Equals(receipt.OwnerIdentity, session.OwnerIdentity, StringComparison.Ordinal)
                && receipt.PartId != Guid.Empty && receipt.FileId != Guid.Empty
                && receipt.UploadSha256 is { Length: 64 }
                && receipt.PhysicalSha256 is { Length: 64 }
                && receipt.ProfileSha256 is { Length: 64 }
                && receipt.Quantity is >= 1 and <= PricingCatalog.MaximumAdditiveQuantity
                && double.IsFinite(receipt.DepositedMm3) && receipt.DepositedMm3 > 0
                && double.IsFinite(receipt.MotionSeconds) && receipt.MotionSeconds > 0
                && double.IsFinite(receipt.BoundingCm3PerUnit) && receipt.BoundingCm3PerUnit > 0
                && session.RequestState.Parts.Count(part => part is not null
                    && part.PartId == receipt.PartId
                    && part.PhysicalAnalysisUpload is { } upload
                    && upload.FileId == receipt.FileId
                    && string.Equals(upload.Sha256, receipt.UploadSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(part.Configuration?.MaterialKey,
                        receipt.ConfiguredMaterialKey, StringComparison.Ordinal)
                    && part.Configuration?.Quantity == receipt.Quantity
                    && string.Equals(part.Configuration?.BuildPreference,
                        receipt.BuildPreference.ToString(), StringComparison.Ordinal)) == 1));
    }

    private static bool IsValid(PersistedPart? part)
    {
        var geometry = part?.Geometry;
        var configuration = part?.Configuration;
        var claim = geometry is null
            ? null
            : new InstantQuotationGeometryClaim(
                geometry.ClaimVersion,
                geometry.Sha256 ?? string.Empty,
                geometry.DimensionXmm,
                geometry.DimensionYmm,
                geometry.DimensionZmm,
                geometry.VolumeMm3,
                geometry.SurfaceAreaMm2,
                geometry.AreaProfileMm2 is { Count: 0 } ? null : geometry.AreaProfileMm2,
                geometry.PerimeterProfileMm is { Count: 0 } ? null : geometry.PerimeterProfileMm,
                geometry.FacetCount,
                geometry.BodyCount,
                geometry.TopologyChecked,
                geometry.NonWatertight,
                geometry.NonManifold,
                geometry.MinThicknessMm);
        return part is not null
            && part.PartId != Guid.Empty
            && !string.IsNullOrWhiteSpace(part.DisplayFileName)
            && !string.IsNullOrWhiteSpace(part.UploadReference)
            && claim?.IsValid() is true
            && (part.PhysicalAnalysisUpload is null
                || IsValidPhysicalAnalysisUpload(part.PhysicalAnalysisUpload, part.UploadReference, geometry!.Sha256!))
            && configuration is not null
            && !string.IsNullOrWhiteSpace(configuration.MaterialKey)
            && !string.IsNullOrWhiteSpace(configuration.Color)
            && configuration.Quantity is >= 1 and <= PricingCatalog.MaximumAdditiveQuantity;
    }

    private static bool IsValidPhysicalAnalysisUpload(
        InstantQuotationPhysicalAnalysisUpload upload,
        string uploadReference,
        string geometrySha256) =>
        upload.FileId != Guid.Empty
        && string.Equals(upload.FileId.ToString("D"), uploadReference, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(upload.FileName)
        && !string.IsNullOrWhiteSpace(upload.ContentType)
        && upload.SizeBytes > 0
        && string.Equals(upload.Sha256, geometrySha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(upload.Status, "clean", StringComparison.Ordinal);

    private static PersistedSession ToPersisted(
        InstantQuotationSessionState session,
        string? ownerIdentity) => new(
            CurrentVersion,
            session.SessionId,
            ownerIdentity,
            session.SubmissionId,
            new PersistedOrderState(session.Parts.Select(ToPersisted).ToArray()),
            session.CreatedAt,
            session.UpdatedAt,
            session.QuoteAuthorization,
            session.PhysicalReceipts?.ToArray());

    private static PersistedPart ToPersisted(InstantQuotationPart part)
    {
        var geometry = part.Geometry;
        return new PersistedPart(
            part.PartId,
            part.DisplayFileName,
            part.UploadReference.Value,
            new PersistedGeometry(
                geometry.ClaimVersion,
                geometry.Sha256,
                geometry.DimensionXmm,
                geometry.DimensionYmm,
                geometry.DimensionZmm,
                geometry.VolumeMm3,
                geometry.SurfaceAreaMm2,
                geometry.AreaProfileMm2.ToArray(),
                geometry.PerimeterProfileMm.ToArray(),
                geometry.FacetCount,
                geometry.BodyCount,
                geometry.TopologyChecked,
                geometry.NonWatertight,
                geometry.NonManifold,
                geometry.MinThicknessMm),
            new PersistedConfiguration(
                part.Configuration.MaterialKey,
                part.Configuration.Color,
                part.Configuration.Quantity,
                part.Configuration.BuildPreference.ToString()),
            part.PhysicalAnalysisUpload);
    }

    private static InstantQuotationSessionState ToSessionState(PersistedSession persisted) => new(
        persisted.SessionId!,
        persisted.SubmissionId!,
        new InstantQuotationOrderState(
            new SnapshotList<InstantQuotationPart>(persisted.RequestState!.Parts!.Select(part => ToPart(part!)))),
        persisted.CreatedAt,
        persisted.UpdatedAt,
        persisted.QuoteAuthorization is null ? null : new InstantQuotationQuoteAuthorization(
            persisted.QuoteAuthorization.LineTickets.ToArray(), persisted.QuoteAuthorization.OrderTicket),
        persisted.OwnerIdentity,
        persisted.PhysicalReceipts?.ToArray());

    private static InstantQuotationPart ToPart(PersistedPart persisted)
    {
        var geometry = persisted.Geometry!;
        var configuration = persisted.Configuration!;
        return new InstantQuotationPart(
            persisted.PartId,
            persisted.DisplayFileName!,
            new InstantQuotationUploadReference(persisted.UploadReference!),
            AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
                geometry.ClaimVersion,
                geometry.Sha256!,
                geometry.DimensionXmm,
                geometry.DimensionYmm,
                geometry.DimensionZmm,
                geometry.VolumeMm3,
                geometry.SurfaceAreaMm2,
                geometry.AreaProfileMm2!,
                geometry.PerimeterProfileMm!,
                geometry.FacetCount,
                geometry.BodyCount,
                geometry.TopologyChecked,
                geometry.NonWatertight,
                geometry.NonManifold,
                geometry.MinThicknessMm),
            new InstantQuotationPartConfiguration(
                configuration.MaterialKey!,
                configuration.Color!,
                configuration.Quantity,
                PricingCatalog.ResolveBuildPreference(configuration.BuildPreference)),
            persisted.PhysicalAnalysisUpload);
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
                geometry.ClaimVersion,
                geometry.Sha256,
                geometry.DimensionXmm,
                geometry.DimensionYmm,
                geometry.DimensionZmm,
                geometry.VolumeMm3,
                geometry.SurfaceAreaMm2,
                geometry.AreaProfileMm2,
                geometry.PerimeterProfileMm,
                geometry.FacetCount,
                geometry.BodyCount,
                geometry.TopologyChecked,
                geometry.NonWatertight,
                geometry.NonManifold,
                geometry.MinThicknessMm),
            part.Configuration with { },
            part.PhysicalAnalysisUpload is null ? null : part.PhysicalAnalysisUpload with { });
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
        DateTimeOffset UpdatedAt,
        InstantQuotationQuoteAuthorization? QuoteAuthorization = null,
        IReadOnlyList<InstantQuotationPhysicalAnalysisReceipt>? PhysicalReceipts = null);

    private sealed record PersistedOrderState(IReadOnlyList<PersistedPart?>? Parts);

    private sealed record PersistedPart(
        Guid PartId,
        string? DisplayFileName,
        string? UploadReference,
        PersistedGeometry? Geometry,
        PersistedConfiguration? Configuration,
        InstantQuotationPhysicalAnalysisUpload? PhysicalAnalysisUpload = null);

    private sealed record PersistedGeometry(
        int ClaimVersion,
        string? Sha256,
        double DimensionXmm,
        double DimensionYmm,
        double DimensionZmm,
        double VolumeMm3,
        double SurfaceAreaMm2,
        IReadOnlyList<double>? AreaProfileMm2,
        IReadOnlyList<double>? PerimeterProfileMm,
        int FacetCount,
        int BodyCount,
        bool TopologyChecked,
        bool NonWatertight,
        bool NonManifold,
        double MinThicknessMm);

    private sealed record PersistedConfiguration(
        string? MaterialKey,
        string? Color,
        int Quantity,
        string? BuildPreference = null);

    private sealed class SnapshotList<T>(IEnumerable<T> source) : IReadOnlyList<T>
    {
        private readonly T[] values = source.ToArray();

        public int Count => values.Length;

        public T this[int index] => values[index];

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => values.GetEnumerator();
    }
}
