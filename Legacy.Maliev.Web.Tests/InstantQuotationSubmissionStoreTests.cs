using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationSubmissionStoreTests
{
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task TryAcquireAsync_ConcurrentOwnerAndSubmission_AllowsOnlyOneLease()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);

        await using var first = await store.TryAcquireAsync("submission-a", "owner-a", default);
        await using var second = await store.TryAcquireAsync("submission-a", "owner-a", default);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Lease_AfterExpiryAndTakeover_RejectsStaleReadAndWrite()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        await using var stale = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(stale);
        storage.Advance(LeaseLifetime);
        await using var current = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(current);

        var staleRead = await stale.ReadAsync(default);
        var staleWrite = await stale.TryPutAsync(Persisted("submission-a"), null, default);

        Assert.False(staleRead.LeaseValid);
        Assert.Null(staleRead.Checkpoint);
        Assert.False(staleWrite);
        Assert.True(await current.TryPutAsync(Persisted("submission-a"), null, default));
    }

    [Fact]
    public async Task RenewAsync_ExtendsOnlyTheCurrentLeaseForTheConfiguredLifetime()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        await using var lease = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(lease);

        storage.Advance(LeaseLifetime - TimeSpan.FromSeconds(1));
        Assert.True(await lease.RenewAsync(default));
        storage.Advance(LeaseLifetime - TimeSpan.FromSeconds(1));

        Assert.True((await lease.ReadAsync(default)).LeaseValid);
        Assert.Equal(1, storage.RenewCount);
        Assert.Equal(LeaseLifetime, storage.LastRenewLifetime);
        Assert.Equal(storage.LastToken, storage.LastRenewToken);
    }

    [Fact]
    public async Task RenewAsync_StorageFailureOrStaleToken_FailsClosed()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        await using var stale = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(stale);

        storage.Advance(LeaseLifetime);
        await using var current = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(current);
        Assert.False(await stale.RenewAsync(default));

        storage.ThrowOnRenew = true;
        Assert.False(await current.RenewAsync(default));
    }

    [Fact]
    public async Task TryPutAsync_AllowsOnlyAbsentToPersistedToCompleted()
    {
        var store = CreateStore(new FakeAtomicStorage());
        await using var lease = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(lease);

        Assert.False(await lease.TryPutAsync(Completed("submission-a"), null, default));
        Assert.True(await lease.TryPutAsync(Persisted("submission-a"), null, default));
        Assert.False(await lease.TryPutAsync(Persisted("submission-a"), null, default));
        Assert.False(await lease.TryPutAsync(Persisted("submission-a"), InstantQuotationSubmissionCheckpointStatus.Persisted, default));
        Assert.True(await lease.TryPutAsync(Completed("submission-a"), InstantQuotationSubmissionCheckpointStatus.Persisted, default));
        Assert.False(await lease.TryPutAsync(Persisted("submission-a"), InstantQuotationSubmissionCheckpointStatus.Completed, default));
    }

    [Fact]
    public async Task TryPutAsync_CheckpointLostFixedExpiry_FailsClosed()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        await using var lease = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(lease);
        Assert.True(await lease.TryPutAsync(Persisted("submission-a"), null, default));
        storage.DropCheckpointExpiry();

        Assert.False(await lease.TryPutAsync(
            Completed("submission-a"),
            InstantQuotationSubmissionCheckpointStatus.Persisted,
            default));
    }

    [Fact]
    public async Task TryPutAsync_MismatchedSubmissionId_IsRejected()
    {
        var store = CreateStore(new FakeAtomicStorage());
        await using var lease = await store.TryAcquireAsync("submission-a", "owner-a", default);

        Assert.NotNull(lease);
        Assert.False(await lease.TryPutAsync(Persisted("submission-b"), null, default));
    }

    [Fact]
    public async Task Store_DerivesOpaqueOwnerAndSubmissionBoundKeys()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);

        await using var first = await store.TryAcquireAsync("submission-visible", "owner-visible", default);
        await using var otherOwner = await store.TryAcquireAsync("submission-visible", "other-owner", default);

        Assert.NotNull(first);
        Assert.NotNull(otherOwner);
        Assert.DoesNotContain("submission-visible", storage.AcquiredKeys[0], StringComparison.Ordinal);
        Assert.DoesNotContain("owner-visible", storage.AcquiredKeys[0], StringComparison.Ordinal);
        Assert.NotEqual(storage.AcquiredKeys[0], storage.AcquiredKeys[1]);
    }

    [Fact]
    public async Task CheckpointPayload_IsProtectedAndContainsNoRawCheckpointValues()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        await using var lease = await store.TryAcquireAsync("submission-secret", "owner-a", default);

        Assert.NotNull(lease);
        Assert.True(await lease.TryPutAsync(
            new InstantQuotationSubmissionCheckpoint(
                "submission-secret",
                42,
                InstantQuotationSubmissionCheckpointStatus.Persisted,
                "snapshot-secret"),
            null,
            default));
        var rawStoredBytes = Encoding.UTF8.GetString(storage.LastPayload!);

        Assert.DoesNotContain("submission-secret", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot-secret", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain(storage.LastToken!, rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Version\"", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RequestReference\"", rawStoredBytes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateChangingOperations_CancellationAtCommit_ReturnCommittedOutcome()
    {
        using var acquireCancellation = new CancellationTokenSource();
        var storage = new FakeAtomicStorage { AfterAcquireCommit = acquireCancellation.Cancel };
        var store = CreateStore(storage);
        var lease = await store.TryAcquireAsync(
            "submission-a",
            "owner-a",
            acquireCancellation.Token);
        Assert.NotNull(lease);

        using var putCancellation = new CancellationTokenSource();
        storage.AfterPutCommit = putCancellation.Cancel;
        Assert.True(await lease.TryPutAsync(
            Persisted("submission-a"),
            null,
            putCancellation.Token));

        using var releaseCancellation = new CancellationTokenSource();
        storage.AfterReleaseCommit = releaseCancellation.Cancel;
        await lease.DisposeAsync();
        await using var replacement = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(replacement);
    }

    [Fact]
    public void RedisAtomicStorage_StateChangingMethods_DoNotCheckCancellationAfterIo()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web.Infrastructure",
            "InstantQuotationSubmissionStore.cs"));

        AssertCancellationOnlyBeforeFirstAwait(source, "public async Task<bool> TryAcquireAsync(");
        AssertCancellationOnlyBeforeFirstAwait(source, "public async Task<bool> RenewAsync(");
        AssertCancellationOnlyBeforeFirstAwait(source, "public async Task<bool> TryPutAsync(");
        AssertCancellationOnlyBeforeFirstAwait(source, "public async Task ReleaseAsync(");
    }

    [Fact]
    public async Task ReadAsync_CorruptProtectedPayload_InvalidatesLeaseAndRejectsFurtherWrite()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        await using var lease = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(lease);
        storage.SeedCorruptCheckpoint();

        var result = await lease.ReadAsync(default);

        Assert.False(result.LeaseValid);
        Assert.Null(result.Checkpoint);
        Assert.False(await lease.TryPutAsync(Persisted("submission-a"), null, default));
    }

    [Fact]
    public async Task StorageFailures_FailClosedAndReleaseCannotReplaceResult()
    {
        var acquireFailure = new FakeAtomicStorage { ThrowOnAcquire = true };
        Assert.Null(await CreateStore(acquireFailure).TryAcquireAsync("submission-a", "owner-a", default));

        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        var lease = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(lease);
        storage.ThrowOnRead = true;
        Assert.False((await lease.ReadAsync(default)).LeaseValid);
        storage.ThrowOnRead = false;
        storage.ThrowOnPut = true;
        Assert.False(await lease.TryPutAsync(Persisted("submission-a"), null, default));
        storage.ThrowOnRelease = true;

        await lease.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_AfterTakeover_DoesNotReleaseCurrentLease()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        var stale = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(stale);
        storage.Advance(LeaseLifetime);
        await using var current = await store.TryAcquireAsync("submission-a", "owner-a", default);
        Assert.NotNull(current);

        await stale.DisposeAsync();
        await using var third = await store.TryAcquireAsync("submission-a", "owner-a", default);

        Assert.Null(third);
        Assert.True((await current.ReadAsync(default)).LeaseValid);
    }

    [Fact]
    public async Task TryAcquireAsync_PreCanceled_DoesNotTouchStorage()
    {
        var storage = new FakeAtomicStorage();
        var store = CreateStore(storage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.TryAcquireAsync("submission-a", "owner-a", cancellation.Token));
        Assert.Empty(storage.AcquiredKeys);
    }

    [Fact]
    public void AddLegacyServiceClients_RegistersSubmissionStoreAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddLegacyServiceClients(new ConfigurationBuilder().Build());

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IInstantQuotationSubmissionStore));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(InstantQuotationSubmissionStore), descriptor.ImplementationType);
    }

    private static InstantQuotationSubmissionStore CreateStore(FakeAtomicStorage storage) => new(
        storage,
        new EphemeralDataProtectionProvider(),
        NullLogger<InstantQuotationSubmissionStore>.Instance);

    private static InstantQuotationSubmissionCheckpoint Persisted(string submissionId) => new(
        submissionId,
        42,
        InstantQuotationSubmissionCheckpointStatus.Persisted,
        "snapshot-digest");

    private static InstantQuotationSubmissionCheckpoint Completed(string submissionId) =>
        Persisted(submissionId) with { Status = InstantQuotationSubmissionCheckpointStatus.Completed };

    private static void AssertCancellationOnlyBeforeFirstAwait(string source, string signature)
    {
        var start = source.LastIndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var nextMethod = source.IndexOf("\n    public async Task", start + signature.Length, StringComparison.Ordinal);
        var method = source[start..(nextMethod < 0 ? source.Length : nextMethod)];
        var firstAwait = method.IndexOf("await ", StringComparison.Ordinal);
        var firstCancellationCheck = method.IndexOf("cancellationToken.ThrowIfCancellationRequested();", StringComparison.Ordinal);

        Assert.True(firstCancellationCheck >= 0 && firstCancellationCheck < firstAwait);
        Assert.DoesNotContain(
            "cancellationToken.ThrowIfCancellationRequested();",
            method[(firstAwait + "await ".Length)..],
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class FakeAtomicStorage : IInstantQuotationSubmissionAtomicStorage
    {
        private readonly Dictionary<string, LeaseEntry> leases = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CheckpointEntry> checkpoints = new(StringComparer.Ordinal);
        private DateTimeOffset now = DateTimeOffset.Parse("2026-07-19T00:00:00Z");

        public List<string> AcquiredKeys { get; } = [];

        public byte[]? LastPayload { get; private set; }

        public string? LastToken { get; private set; }

        public string? LastRenewToken { get; private set; }

        public TimeSpan? LastRenewLifetime { get; private set; }

        public int RenewCount { get; private set; }

        public Action? AfterAcquireCommit { get; init; }

        public Action? AfterPutCommit { get; set; }

        public Action? AfterReleaseCommit { get; set; }

        public bool ThrowOnAcquire { get; init; }

        public bool ThrowOnRead { get; set; }

        public bool ThrowOnRenew { get; set; }

        public bool ThrowOnPut { get; set; }

        public bool ThrowOnRelease { get; set; }

        public void Advance(TimeSpan duration) => now = now.Add(duration);

        public void SeedCorruptCheckpoint()
        {
            var key = Assert.Single(leases).Key.Replace(":lease", ":checkpoint", StringComparison.Ordinal);
            checkpoints[key] = new CheckpointEntry("Persisted", [1, 2, 3], now.AddHours(24));
        }

        public void DropCheckpointExpiry()
        {
            var checkpoint = Assert.Single(checkpoints);
            checkpoints[checkpoint.Key] = checkpoint.Value with { ExpiresAt = null };
        }

        public Task<bool> TryAcquireAsync(
            string leaseKey,
            string token,
            TimeSpan lifetime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfUnavailable(ThrowOnAcquire);
            AcquiredKeys.Add(leaseKey);
            if (leases.TryGetValue(leaseKey, out var existing) && existing.ExpiresAt > now)
            {
                return Task.FromResult(false);
            }

            leases[leaseKey] = new LeaseEntry(token, now.Add(lifetime));
            LastToken = token;
            AfterAcquireCommit?.Invoke();
            return Task.FromResult(true);
        }

        public Task<InstantQuotationSubmissionAtomicRead> ReadAsync(
            string leaseKey,
            string checkpointKey,
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfUnavailable(ThrowOnRead);
            if (!Owns(leaseKey, token))
            {
                return Task.FromResult(new InstantQuotationSubmissionAtomicRead(false, null));
            }

            checkpoints.TryGetValue(checkpointKey, out var checkpoint);
            return Task.FromResult(new InstantQuotationSubmissionAtomicRead(true, checkpoint?.Payload));
        }

        public Task<bool> RenewAsync(
            string leaseKey,
            string token,
            TimeSpan lifetime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfUnavailable(ThrowOnRenew);
            RenewCount++;
            LastRenewToken = token;
            LastRenewLifetime = lifetime;
            if (!Owns(leaseKey, token))
            {
                return Task.FromResult(false);
            }

            leases[leaseKey] = new LeaseEntry(token, now.Add(lifetime));
            return Task.FromResult(true);
        }

        public Task<bool> TryPutAsync(
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
            ThrowIfUnavailable(ThrowOnPut);
            if (!Owns(leaseKey, token))
            {
                return Task.FromResult(false);
            }

            checkpoints.TryGetValue(checkpointKey, out var existing);
            if (!string.Equals(existing?.Status, expectedPriorStatus, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            if (existing is not null && existing.ExpiresAt is null)
            {
                return Task.FromResult(false);
            }

            if ((existing is null && status != "Persisted")
                || (existing?.Status == "Persisted" && status != "Completed")
                || existing?.Status == "Completed")
            {
                return Task.FromResult(false);
            }

            LastPayload = protectedPayload.ToArray();
            checkpoints[checkpointKey] = new CheckpointEntry(
                status,
                LastPayload,
                existing?.ExpiresAt ?? now.Add(lifetime));
            AfterPutCommit?.Invoke();
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(
            string leaseKey,
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfUnavailable(ThrowOnRelease);
            if (Owns(leaseKey, token))
            {
                leases.Remove(leaseKey);
            }

            AfterReleaseCommit?.Invoke();

            return Task.CompletedTask;
        }

        private bool Owns(string key, string token) =>
            leases.TryGetValue(key, out var lease)
            && lease.ExpiresAt > now
            && string.Equals(lease.Token, token, StringComparison.Ordinal);

        private static void ThrowIfUnavailable(bool unavailable)
        {
            if (unavailable)
            {
                throw new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "Test Redis unavailable.");
            }
        }

        private sealed record LeaseEntry(string Token, DateTimeOffset ExpiresAt);

        private sealed record CheckpointEntry(string Status, byte[] Payload, DateTimeOffset? ExpiresAt);
    }
}

public sealed class InstantQuotationSubmissionStoreRedisTests(RedisRuntimeFixture fixture)
    : IClassFixture<RedisRuntimeFixture>
{
    [Fact]
    public async Task RedisScripts_EnforceFenceCasAndProtectedFixedExpiryCheckpoint()
    {
        using var firstFactory = fixture.CreateFactory();
        using var secondFactory = fixture.CreateFactory();
        var firstStore = firstFactory.Services.GetRequiredService<IInstantQuotationSubmissionStore>();
        var secondStore = secondFactory.Services.GetRequiredService<IInstantQuotationSubmissionStore>();
        var multiplexer = firstFactory.Services.GetRequiredService<IConnectionMultiplexer>();
        var database = multiplexer.GetDatabase();
        var endpoint = Assert.Single(multiplexer.GetEndPoints());
        var server = multiplexer.GetServer(endpoint);
        var submissionId = $"submission-{Guid.NewGuid():N}";
        var ownerIdentity = $"owner-{Guid.NewGuid():N}";

        var first = await firstStore.TryAcquireAsync(submissionId, ownerIdentity, default);
        await using var blocked = await secondStore.TryAcquireAsync(submissionId, ownerIdentity, default);
        Assert.NotNull(first);
        Assert.Null(blocked);
        var leaseKey = Assert.Single(server.Keys(
            database.Database,
            $"{InstantQuotationSubmissionStore.KeyPrefix}*:lease"));
        Assert.True(await database.KeyDeleteAsync(leaseKey));
        await using var replacement = await secondStore.TryAcquireAsync(submissionId, ownerIdentity, default);
        Assert.NotNull(replacement);
        Assert.False((await first.ReadAsync(default)).LeaseValid);
        Assert.False(await first.TryPutAsync(
            new InstantQuotationSubmissionCheckpoint(
                submissionId,
                42,
                InstantQuotationSubmissionCheckpointStatus.Persisted,
                "stale"),
            null,
            default));
        await first.DisposeAsync();
        Assert.True((await replacement.ReadAsync(default)).LeaseValid);
        Assert.True(await replacement.TryPutAsync(
            new InstantQuotationSubmissionCheckpoint(
                submissionId,
                42,
                InstantQuotationSubmissionCheckpointStatus.Persisted,
                "snapshot-secret"),
            null,
            default));
        Assert.False(await replacement.TryPutAsync(
            new InstantQuotationSubmissionCheckpoint(
                submissionId,
                43,
                InstantQuotationSubmissionCheckpointStatus.Persisted,
                "rollback"),
            InstantQuotationSubmissionCheckpointStatus.Persisted,
            default));

        var checkpointKey = Assert.Single(server.Keys(
            database.Database,
            $"{InstantQuotationSubmissionStore.KeyPrefix}*:checkpoint"));
        var expiryBeforeCompletion = await database.KeyTimeToLiveAsync(checkpointKey);
        var rawPayload = (byte[]?)await database.HashGetAsync(checkpointKey, "payload");
        Assert.NotNull(rawPayload);
        var rawStoredBytes = Encoding.UTF8.GetString(rawPayload);
        Assert.DoesNotContain(submissionId, rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot-secret", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("Persisted", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SubmissionId\"", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RequestReference\"", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Status\"", rawStoredBytes, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SnapshotDigest\"", rawStoredBytes, StringComparison.Ordinal);
        Assert.True(await database.KeyPersistAsync(checkpointKey));
        Assert.False(await replacement.TryPutAsync(
            new InstantQuotationSubmissionCheckpoint(
                submissionId,
                42,
                InstantQuotationSubmissionCheckpointStatus.Completed,
                "snapshot-secret"),
            InstantQuotationSubmissionCheckpointStatus.Persisted,
            default));
        Assert.True(await database.KeyExpireAsync(checkpointKey, expiryBeforeCompletion));
        Assert.True(await replacement.TryPutAsync(
            new InstantQuotationSubmissionCheckpoint(
                submissionId,
                42,
                InstantQuotationSubmissionCheckpointStatus.Completed,
                "snapshot-secret"),
            InstantQuotationSubmissionCheckpointStatus.Persisted,
            default));
        var expiryAfterCompletion = await database.KeyTimeToLiveAsync(checkpointKey);

        Assert.NotNull(expiryBeforeCompletion);
        Assert.NotNull(expiryAfterCompletion);
        Assert.True(expiryAfterCompletion <= expiryBeforeCompletion);
        Assert.Equal(
            InstantQuotationSubmissionCheckpointStatus.Completed,
            (await replacement.ReadAsync(default)).Checkpoint?.Status);
    }
}
