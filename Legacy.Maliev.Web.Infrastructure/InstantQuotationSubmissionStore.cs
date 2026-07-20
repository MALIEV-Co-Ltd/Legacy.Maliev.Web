using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationSubmissionStore : IInstantQuotationSubmissionStore
{
    internal const int CurrentVersion = 1;
    internal const string ProtectorPurpose = "Legacy.Maliev.Web.InstantQuotationSubmissionCheckpoint.v1";
    internal const string KeyPrefix = "legacy:web:instant-quotation-submission:";
    internal static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan CheckpointLifetime = DistributedInstantQuotationSessionStore.SessionLifetime;

    private readonly IInstantQuotationSubmissionAtomicStorage storage;
    private readonly IDataProtector protector;
    private readonly ILogger<InstantQuotationSubmissionStore> logger;

    public InstantQuotationSubmissionStore(
        IConnectionMultiplexer connectionMultiplexer,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<InstantQuotationSubmissionStore> logger)
        : this(
            new RedisInstantQuotationSubmissionAtomicStorage(connectionMultiplexer),
            dataProtectionProvider,
            logger)
    {
    }

    internal InstantQuotationSubmissionStore(
        IInstantQuotationSubmissionAtomicStorage storage,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<InstantQuotationSubmissionStore> logger)
    {
        this.storage = storage;
        protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        this.logger = logger;
    }

    public async Task<IInstantQuotationSubmissionLease?> TryAcquireAsync(
        string submissionId,
        string ownerIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(submissionId) || string.IsNullOrWhiteSpace(ownerIdentity))
        {
            return null;
        }

        var keyStem = KeyStem(ownerIdentity, submissionId);
        var leaseKey = $"{keyStem}:lease";
        var checkpointKey = $"{keyStem}:checkpoint";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            if (!await storage.TryAcquireAsync(leaseKey, token, LeaseLifetime, cancellationToken))
            {
                return null;
            }
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            logger.LogWarning(exception, "Instant quotation submission lease storage is unavailable.");
            return null;
        }

        return new Lease(
            storage,
            protector,
            logger,
            leaseKey,
            checkpointKey,
            token,
            submissionId);
    }

    private static string KeyStem(string ownerIdentity, string submissionId)
    {
        var material = Encoding.UTF8.GetBytes($"{ownerIdentity}\u001f{submissionId}");
        return $"{KeyPrefix}{Convert.ToHexString(SHA256.HashData(material))}";
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is RedisException or TimeoutException or ObjectDisposedException;

    private sealed class Lease(
        IInstantQuotationSubmissionAtomicStorage storage,
        IDataProtector protector,
        ILogger logger,
        string leaseKey,
        string checkpointKey,
        string token,
        string submissionId) : IInstantQuotationSubmissionLease
    {
        private int disposed;

        public async Task<bool> RenewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            try
            {
                return await storage.RenewAsync(
                    leaseKey,
                    token,
                    LeaseLifetime,
                    cancellationToken);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                logger.LogWarning(exception, "Instant quotation submission lease renewal failed.");
                return false;
            }
        }

        public async Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref disposed) != 0)
            {
                return new InstantQuotationSubmissionCheckpointRead(false, null);
            }

            InstantQuotationSubmissionAtomicRead stored;
            try
            {
                stored = await storage.ReadAsync(
                    leaseKey,
                    checkpointKey,
                    token,
                    cancellationToken);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                logger.LogWarning(exception, "Instant quotation submission checkpoint storage is unavailable.");
                return new InstantQuotationSubmissionCheckpointRead(false, null);
            }

            if (!stored.LeaseValid || stored.ProtectedPayload is null)
            {
                return new InstantQuotationSubmissionCheckpointRead(stored.LeaseValid, null);
            }

            try
            {
                var payload = protector.Unprotect(stored.ProtectedPayload);
                var persisted = JsonSerializer.Deserialize<PersistedCheckpoint>(payload);
                return IsValid(persisted)
                    ? new InstantQuotationSubmissionCheckpointRead(true, ToCheckpoint(persisted!))
                    : new InstantQuotationSubmissionCheckpointRead(false, null);
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or NotSupportedException)
            {
                logger.LogWarning(exception, "Rejected an unreadable instant quotation submission checkpoint.");
                return new InstantQuotationSubmissionCheckpointRead(false, null);
            }
        }

        public async Task<bool> TryPutAsync(
            InstantQuotationSubmissionCheckpoint checkpoint,
            InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(checkpoint);
            if (Volatile.Read(ref disposed) != 0
                || !IsValidTransition(expectedPriorStatus, checkpoint.Status)
                || !IsValid(checkpoint))
            {
                return false;
            }

            var persisted = new PersistedCheckpoint(
                CurrentVersion,
                checkpoint.SubmissionId,
                checkpoint.RequestReference,
                checkpoint.Status,
                checkpoint.SnapshotDigest);
            var payload = JsonSerializer.SerializeToUtf8Bytes(persisted);
            var protectedPayload = protector.Protect(payload);
            try
            {
                return await storage.TryPutAsync(
                    leaseKey,
                    checkpointKey,
                    token,
                    expectedPriorStatus?.ToString(),
                    checkpoint.Status.ToString(),
                    protectedPayload,
                    CheckpointLifetime,
                    cancellationToken);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                logger.LogWarning(exception, "Instant quotation submission checkpoint storage is unavailable.");
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await storage.ReleaseAsync(leaseKey, token, CancellationToken.None);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                logger.LogWarning(exception, "Instant quotation submission lease release failed.");
            }
        }

        private bool IsValid(PersistedCheckpoint? checkpoint) =>
            checkpoint is not null
            && checkpoint.Version == CurrentVersion
            && string.Equals(checkpoint.SubmissionId, submissionId, StringComparison.Ordinal)
            && checkpoint.RequestReference > 0
            && Enum.IsDefined(checkpoint.Status)
            && !string.IsNullOrWhiteSpace(checkpoint.SnapshotDigest);

        private bool IsValid(InstantQuotationSubmissionCheckpoint checkpoint) =>
            string.Equals(checkpoint.SubmissionId, submissionId, StringComparison.Ordinal)
            && checkpoint.RequestReference > 0
            && Enum.IsDefined(checkpoint.Status)
            && !string.IsNullOrWhiteSpace(checkpoint.SnapshotDigest);

        private static bool IsValidTransition(
            InstantQuotationSubmissionCheckpointStatus? expected,
            InstantQuotationSubmissionCheckpointStatus status) =>
            (expected is null && status == InstantQuotationSubmissionCheckpointStatus.Persisted)
            || (expected == InstantQuotationSubmissionCheckpointStatus.Persisted
                && status == InstantQuotationSubmissionCheckpointStatus.Completed);

        private static InstantQuotationSubmissionCheckpoint ToCheckpoint(PersistedCheckpoint persisted) => new(
            persisted.SubmissionId!,
            persisted.RequestReference,
            persisted.Status,
            persisted.SnapshotDigest!);
    }

    private sealed record PersistedCheckpoint(
        int Version,
        string? SubmissionId,
        int RequestReference,
        InstantQuotationSubmissionCheckpointStatus Status,
        string? SnapshotDigest);
}

internal sealed record InstantQuotationSubmissionAtomicRead(
    bool LeaseValid,
    byte[]? ProtectedPayload);

internal interface IInstantQuotationSubmissionAtomicStorage
{
    Task<bool> TryAcquireAsync(
        string leaseKey,
        string token,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    Task<bool> RenewAsync(
        string leaseKey,
        string token,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    Task<InstantQuotationSubmissionAtomicRead> ReadAsync(
        string leaseKey,
        string checkpointKey,
        string token,
        CancellationToken cancellationToken);

    Task<bool> TryPutAsync(
        string leaseKey,
        string checkpointKey,
        string token,
        string? expectedPriorStatus,
        string status,
        byte[] protectedPayload,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        string leaseKey,
        string token,
        CancellationToken cancellationToken);
}

internal sealed class RedisInstantQuotationSubmissionAtomicStorage(IConnectionMultiplexer connectionMultiplexer)
    : IInstantQuotationSubmissionAtomicStorage
{
    private const string ReadScript = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then
            return {0, ''}
        end
        local payload = redis.call('HGET', KEYS[2], 'payload')
        if not payload then
            return {1, ''}
        end
        return {1, payload}
        """;

    private const string PutScript = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then
            return 0
        end
        local exists = redis.call('EXISTS', KEYS[2])
        local current = redis.call('HGET', KEYS[2], 'status')
        if ARGV[2] == '' then
            if exists ~= 0 or ARGV[3] ~= 'Persisted' then
                return 0
            end
        else
            local ttl = redis.call('PTTL', KEYS[2])
            if ttl <= 0 or current ~= ARGV[2] or ARGV[2] ~= 'Persisted' or ARGV[3] ~= 'Completed' then
                return 0
            end
        end
        redis.call('HSET', KEYS[2], 'status', ARGV[3], 'payload', ARGV[4])
        if exists == 0 then
            redis.call('PEXPIRE', KEYS[2], ARGV[5])
        end
        return 1
        """;

    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly IDatabase database = connectionMultiplexer.GetDatabase();

    public async Task<bool> TryAcquireAsync(
        string leaseKey,
        string token,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var acquired = await database.LockTakeAsync(leaseKey, token, lifetime);
        return acquired;
    }

    public async Task<bool> RenewAsync(
        string leaseKey,
        string token,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await database.LockExtendAsync(leaseKey, token, lifetime);
    }

    public async Task<InstantQuotationSubmissionAtomicRead> ReadAsync(
        string leaseKey,
        string checkpointKey,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await database.ScriptEvaluateAsync(
            ReadScript,
            [leaseKey, checkpointKey],
            [token]);
        var values = (RedisResult[]?)result;
        if (values is null || values.Length != 2 || (long)values[0] != 1)
        {
            return new InstantQuotationSubmissionAtomicRead(false, null);
        }

        var payload = (byte[]?)values[1];
        return new InstantQuotationSubmissionAtomicRead(true, payload is { Length: > 0 } ? payload : null);
    }

    public async Task<bool> TryPutAsync(
        string leaseKey,
        string checkpointKey,
        string token,
        string? expectedPriorStatus,
        string status,
        byte[] protectedPayload,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await database.ScriptEvaluateAsync(
            PutScript,
            [leaseKey, checkpointKey],
            [
                token,
                expectedPriorStatus ?? string.Empty,
                status,
                protectedPayload,
                checked((long)lifetime.TotalMilliseconds),
            ]);
        return (long)result == 1;
    }

    public async Task ReleaseAsync(
        string leaseKey,
        string token,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.ScriptEvaluateAsync(ReleaseScript, [leaseKey], [token]);
    }
}
