using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Owns the disposable billing graph; an unresolved child never releases its dependencies.</summary>
internal sealed class BillingProofLifetime : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<Guid, BillingProofLifetime> Retained = new();
    private readonly Guid run = Guid.NewGuid();
    private readonly List<BillingHostLease> hosts = [];
    private readonly List<BillingChildLease> children = [];
    private readonly List<BillingHostLease> dependencies = [];
    private readonly List<BillingBackendLease> backends = [];
    private readonly string? proofCulture;
    private readonly List<Task> operations = [];
    private readonly object resourceGate = new();
    private readonly SemaphoreSlim cleanup = new(1, 1);
    private readonly CancellationTokenSource expiryCancellation = new();
    private readonly Task expiry;
    private Task? body;
    private string? expiryFailure;
    private bool expiryDisposed;
    private bool expired;
    private volatile bool closing;
    private bool released;
    private bool readerFailureRejected;
    private readonly DateTimeOffset expiresUtc = DateTimeOffset.UtcNow.AddMinutes(30);
    internal bool IsRetained => Retained.ContainsKey(run);

    internal BillingProofLifetime(TimeSpan? lifetime = null, Task? expirySignal = null, string? proofCulture = null)
    {
        if (proofCulture is not null and not "en" and not "th") throw new ArgumentException("Bounded billing proof culture required.", nameof(proofCulture));
        this.proofCulture = proofCulture;
        Retained.TryAdd(run, this);
        expiry = ExpireAsync(lifetime ?? TimeSpan.FromSeconds(1800), expirySignal);
    }

    internal async Task RunAsync(Func<Task> work)
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (resourceGate)
        {
            EnsureOpen();
            if (body is not null) throw new InvalidOperationException("Billing case work is already admitted.");
            body = admitted.Task; // Publish before invoking even the synchronous callback prefix.
        }
        try { await work(); }
        catch (Exception failure) { admitted.TrySetException(failure); throw; }
        finally { admitted.TrySetResult(); }
    }

    internal Task ExpirySettled => expiry;

    internal void StartHost(BillingHostLease host, Action start)
    {
        TaskCompletionSource reservation;
        lock (resourceGate)
        {
            EnsureOpen();
            reservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operations.Add(reservation.Task);
            host.Starting(); // Startup state and its completion barrier are one admission decision.
        }
        // This activation is admitted before closing; an in-flight startup stays pending/fenced.
        try { start(); host.Started(); }
        finally { reservation.TrySetResult(); }
    }

    private async Task ExpireAsync(TimeSpan lifetime, Task? expirySignal)
    {
        try { await (expirySignal is null ? Task.Delay(lifetime, expiryCancellation.Token) : expirySignal.WaitAsync(expiryCancellation.Token)); }
        catch (OperationCanceledException) when (expiryCancellation.IsCancellationRequested) { return; }
        expired = true;
        try { await CloseCoreAsync(); }
        catch (Exception failure) { expiryFailure = failure.GetType().Name; }
        finally { DisposeExpirySource(); }
        // This exact task owns expiry cleanup; unresolved graph stays registered/fenced.
    }

    internal PostgreSqlContainer Postgres(string database)
    {
        var lease = AcquireDependency(() =>
        {
            var allocated = new BillingBackendLease(new BillingDockerBackend(database, run.ToString("N")));
            lock (resourceGate) backends.Add(allocated); // Published before allocation reservation settles.
            return allocated;
        }, value => value.CloseAsync());
        return lease.Postgres;
    }

    internal Task StartPostgresAsync(PostgreSqlContainer container, CancellationToken token)
    {
        BillingBackendLease lease;
        lock (resourceGate)
        {
            EnsureOpen();
            lease = backends.Single(value => ReferenceEquals(value.Postgres, container));
        }
        return lease.StartAsync(token);
    }

    internal BillingHostLease OwnHost(Func<ValueTask> dispose, bool startupRequired = false, TimeSpan? closeTimeout = null)
    {
        lock (resourceGate)
        {
            EnsureOpen();
            var host = new BillingHostLease(dispose, startupRequired, closeTimeout);
            hosts.Add(host);
            return host;
        }
    }

    internal void OwnDependency(Func<ValueTask> dispose) { lock (resourceGate) { EnsureOpen(); dependencies.Add(new(dispose, false)); } }
    internal T AcquireHost<T>(Func<T> allocate, Func<T, ValueTask> dispose) where T : class => AcquireHost(allocate, dispose, out _);
    internal T AcquireHost<T>(Func<T> allocate, Func<T, ValueTask> dispose, out BillingHostLease lease, bool startupRequired = false) where T : class
        => Acquire(allocate, dispose, hosts, out lease, startupRequired);
    internal T AcquireDependency<T>(Func<T> allocate, Func<T, ValueTask> dispose) where T : class
        => Acquire(allocate, dispose, dependencies, out _, false);
    private T Acquire<T>(Func<T> allocate, Func<T, ValueTask> dispose, List<BillingHostLease> destination, out BillingHostLease lease, bool startupRequired) where T : class
    {
        var reservation = ReserveAllocation();
        T? resource = null;
        lease = new BillingHostLease(() => resource is null ? ValueTask.CompletedTask : dispose(resource), startupRequired);
        try
        {
            resource = allocate();
            lock (resourceGate)
            {
                // This admitted reservation must publish even after closing; cleanup joins it first.
                destination.Add(lease);
                EnsureOpen(); // Never hand a late resource back for use.
            }
            return resource;
        }
        finally { reservation.TrySetResult(); }
    }
    private TaskCompletionSource ReserveAllocation()
    {
        lock (resourceGate)
        {
            EnsureOpen();
            var reservation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            operations.Add(reservation.Task);
            return reservation;
        }
    }
    internal T OwnOperation<T>(Func<T> start) where T : Task
    {
        lock (resourceGate) { EnsureOpen(); var operation = start(); operations.Add(operation); return operation; }
    }
    internal async Task<T> AcquireHostAsync<T>(Func<Task<T>> acquire, Func<T, ValueTask> dispose)
    {
        var reservation = ReserveAllocation();
        try
        {
            var resource = await acquire();
            lock (resourceGate)
            {
                hosts.Add(new(() => dispose(resource), false));
                EnsureOpen(); // Late completion is retained, not handed back for new use.
            }
            return resource;
        }
        finally { reservation.TrySetResult(); }
    }

    internal BillingChildLease StartChild(IBillingChildProcess process)
    {
        lock (resourceGate)
        {
            EnsureOpen();
            var child = new BillingChildLease(process);
            children.Add(child); // Before instance Start, metadata access, or either reader acquisition.
            child.Start();
            return child;
        }
    }
    internal BillingChildLease StartChild(Func<IBillingChildProcess> allocate)
    {
        lock (resourceGate) { EnsureOpen(); return StartChild(allocate()); }
    }

    private void EnsureOpen()
    {
        if (closing) throw new InvalidOperationException("Billing resource owner is closing; new resources refused.");
    }

    public async ValueTask DisposeAsync()
    {
        try { await CloseCoreAsync(); }
        finally
        {
            try
            {
                if (released)
                {
                    lock (resourceGate) if (!expiryDisposed) expiryCancellation.Cancel();
                    try { await expiry.WaitAsync(TimeSpan.FromSeconds(5)); }
                    finally { DisposeExpirySource(); }
                }
            }
            finally { await PersistBackendProofAsync(); }
        }
        Console.WriteLine("Billing lifetime receipt: " + OwnershipReceipt());
        if (expired) throw new InvalidOperationException("Billing graph expired; cleanup is not business proof acceptance.");
    }

    private async Task PersistBackendProofAsync()
    {
        if (proofCulture is null) return;
        var candidate = Environment.GetEnvironmentVariable("MALIEV_BILLING_CANDIDATE_HEAD");
        var complete = released && !expired && !readerFailureRejected && expiry.IsCompletedSuccessfully
            && backends.Count == 1 && backends.All(value => value.AbsenceVerified && value.CaptureVerified && value.SdkReleased);
        var proof = new
        {
            schema = 1,
            surface = "member-billing",
            culture = proofCulture,
            candidateHead = candidate,
            owner = "web-billing-proof",
            run = run.ToString("N"),
            complete,
            graphReleased = released,
            expired,
            readerFailureRejected,
            expiryJoined = expiry.IsCompletedSuccessfully,
            sharedAuthorityExcluded = true,
            resources = backends.Select(value => value.Receipt).ToArray(),
        };
        var evidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "billing-persistence");
        Directory.CreateDirectory(evidence);
        var file = Path.Combine(evidence, proofCulture + ".json");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var business = File.Exists(file) ? JsonNode.Parse(await File.ReadAllTextAsync(file, deadline.Token))!.AsObject() : new JsonObject();
        business["billingBackendCleanup"] = JsonSerializer.SerializeToNode(proof);
        var serialized = business.ToJsonString();
        if (serialized.Length > 16384) throw new InvalidOperationException("Bounded synthetic billing receipt exceeded.");
        await File.WriteAllTextAsync(file, serialized, deadline.Token);
        if (released && (!complete || !Regex.IsMatch(candidate ?? "", "\\A[a-f0-9]{40}\\z")))
            throw new InvalidOperationException("Released graph lacks complete original billing backend evidence; proof refused.");
    }

    private void DisposeExpirySource()
    {
        lock (resourceGate)
        {
            if (expiryDisposed) return;
            expiryDisposed = true;
            expiryCancellation.Dispose();
        }
    }

    private async Task CloseCoreAsync()
    {
        await cleanup.WaitAsync();
        try
        {
            if (released)
            {
                RejectReaderFailure();
                return;
            }
            lock (resourceGate) closing = true;
            var failures = new List<string>();
            // Attempt every frontend. Never tear down a database while a host/client may still use it.
            BillingHostLease[] hostSnapshot;
            Task[] operationSnapshot;
            lock (resourceGate) { hostSnapshot = hosts.ToArray(); operationSnapshot = operations.ToArray(); }
            foreach (var host in hostSnapshot.Reverse())
                try { await host.CloseAsync(); } catch (Exception failure) { failures.Add("host:" + failure.GetType().Name); }
            foreach (var operation in operationSnapshot)
            {
                try { await operation.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception failure) when (operation.IsCompleted) { _ = failure; _ = operation.Exception; }
                catch (Exception failure) { failures.Add("pending-operation:" + failure.GetType().Name); }
            }
            if (body is not null)
            {
                try { await body.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception failure) when (body.IsCompleted) { _ = failure; _ = body.Exception; }
                catch (Exception failure) { failures.Add("pending-body:" + failure.GetType().Name); }
            }
            // A completed acquisition may have registered a frontend after the first snapshot.
            // Body/acquisition settlement precedes this final barrier; admission cannot reopen.
            BillingHostLease[] lateHosts;
            lock (resourceGate) lateHosts = hosts.Except(hostSnapshot).ToArray();
            foreach (var host in lateHosts.Reverse())
                try { await host.CloseAsync(); } catch (Exception failure) { failures.Add("final-host:" + failure.GetType().Name); }
            if (failures.Count != 0) throw new BillingProofQuarantineException(failures) { Receipt = OwnershipReceipt() };
            foreach (var child in children.AsEnumerable().Reverse())
                try { await child.CloseAsync(); } catch (Exception failure) { failures.Add("child:" + failure.GetType().Name); }
            if (failures.Count != 0) throw new BillingProofQuarantineException(failures) { Receipt = OwnershipReceipt() };
            // Remove an action only after successful completion. Failed release stays owned for retry.
            while (dependencies.Count != 0)
            {
                try { await dependencies[^1].CloseAsync(); }
                catch (Exception failure) { throw new BillingProofQuarantineException(["dependency:" + failure.GetType().Name]) { Receipt = OwnershipReceipt() }; }
                dependencies.RemoveAt(dependencies.Count - 1);
            }
            released = true;
            Retained.TryRemove(run, out _);
            readerFailureRejected = children.Any(child => child.ReaderFailed);
            RejectReaderFailure();
        }
        finally { cleanup.Release(); }
    }

    private void RejectReaderFailure()
    {
        if (readerFailureRejected)
            throw new InvalidOperationException("Owned child output drain failed; resources were released but proof is not accepted.");
    }

    internal string OwnershipReceipt()
    {
        lock (resourceGate)
        {
            var receipt = JsonSerializer.Serialize(new
            {
                owner = "web-billing-proof",
                run,
                expiresUtc,
                released,
                readerFailureRejected,
                retained = IsRetained,
                expired,
                expiryFailure,
                policy = "Same-owner 1800-second expiry task closes admission, frontends and actual case work before dependent cleanup; unresolved ownership is quarantined, never accepted as release.",
                hosts = hosts.Select(host => host.Receipt).ToArray(),
                children = children.Select(child => child.Receipt).ToArray(),
                backends = backends.Select(value => value.Receipt).ToArray(),
            });
            if (receipt.Length > 16384) throw new InvalidOperationException("Owned lifecycle receipt exceeds its bound; no raw output retained.");
            return receipt;
        }
    }
}

internal sealed class BillingProofQuarantineException(IEnumerable<string> stages)
    : Exception("Billing resources remain owned; dependent teardown refused: " + string.Join(",", stages))
{
    internal string? Receipt { get; init; }
    public override string ToString() => Message + Environment.NewLine + Receipt;
}

// Only synthetic identity/envelope metadata is captured; database credentials and inspect Env never leave the adapter.
internal sealed record BillingBackendIdentity(string Id, DateTime CreatedUtc, string ImageId, string Name,
    string Owner, string Run, string ExpiresUtc, string ConfiguredImage, bool EnvelopeValid, string Signature);

internal interface IBillingBackend : IDisposable
{
    string Name { get; }
    string Database { get; }
    string Run { get; }
    string ExpiresUtc { get; }
    DateTime IntentUtc { get; }
    string? SdkId { get; }
    PostgreSqlContainer? Postgres { get; }
    Task<string> DaemonAsync(CancellationToken token);
    Task<BillingBackendIdentity?> InspectAsync(string identity, CancellationToken token);
    Task StartAsync(CancellationToken token);
    Task StopAsync(string originalId, CancellationToken token);
    Task RemoveAsync(string originalId, CancellationToken token);
    ValueTask DisposeSdkAsync();
}

internal sealed class BillingBackendLease(IBillingBackend backend)
{
    private string? daemon;
    private string? originalId;
    private BillingBackendIdentity? original;
    private bool baselineAbsent;
    private bool startDispatched;
    private bool startupSettled;
    private bool closeStarted;
    internal bool AbsenceVerified { get; private set; }
    internal bool CaptureVerified => original is not null;
    internal bool SdkReleased { get; private set; }
    internal PostgreSqlContainer Postgres => backend.Postgres ?? throw new InvalidOperationException("Native billing backend required.");
    internal object Receipt => new
    {
        backend = "postgres",
        localEndpoint = "unix:///var/run/docker.sock",
        databaseSynthetic = true,
        name = backend.Name,
        database = backend.Database,
        owner = "web-billing-proof",
        run = backend.Run,
        expiresUtc = backend.ExpiresUtc,
        daemon,
        originalId,
        baselineAbsent,
        startDispatched,
        startupSettled,
        captureVerified = CaptureVerified,
        absenceVerified = AbsenceVerified,
        sdkReleased = SdkReleased,
        original = original is null ? null : new
        {
            id = original.Id,
            createdUtc = original.CreatedUtc,
            imageId = original.ImageId,
            name = original.Name,
            owner = original.Owner,
            run = original.Run,
            expiresUtc = original.ExpiresUtc,
            configuredImage = original.ConfiguredImage,
            envelopeValid = original.EnvelopeValid,
            envelopeSignatureSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original.Signature))).ToLowerInvariant(),
            memoryBytes = 536870912,
            swapBytes = 536870912,
            nanoCpus = 1000000000,
            tmpfs = "/var/lib/postgresql:rw,size=268435456",
            loopbackPorts = true,
            persistentData = false,
        },
    };

    internal async Task StartAsync(CancellationToken token)
    {
        if (startDispatched || closeStarted) throw new InvalidOperationException("Backend activation already dispatched or closing.");
        daemon = await backend.DaemonAsync(token);
        if (string.IsNullOrWhiteSpace(daemon)) throw new InvalidOperationException("Local daemon identity unavailable.");
        if (await InspectAsync(backend.Name, token) is not null) throw new InvalidOperationException("Backend name collision; pre-existing resource preserved.");
        baselineAbsent = true;
        startDispatched = true; // Intent/owner are registered before SDK create/start dispatch.
        Exception? failure = null;
        try { await backend.StartAsync(token); }
        catch (Exception startup) { failure = startup; }
        finally { startupSettled = true; }
        using var capture = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sdkId = backend.SdkId;
        if (!Regex.IsMatch(sdkId ?? "", "\\A[a-f0-9]{64}\\z"))
            throw new InvalidOperationException("Original SDK creation ID unavailable; name-based adoption refused.", failure);
        originalId = sdkId;
        var byId = await InspectAsync(originalId!, capture.Token);
        var byName = await InspectAsync(backend.Name, capture.Token);
        if (byId is null || byName is null || byId.Id != originalId || byName.Id != originalId)
            throw new InvalidOperationException("Original SDK allocation lacks exact generation evidence.", failure);
        Validate(byId); Validate(byName);
        if (byId.Signature != byName.Signature) throw new InvalidOperationException("Name/ID creation envelopes differ.");
        original = byId;
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task<BillingBackendIdentity?> InspectAsync(string identity, CancellationToken token)
    {
        if (await backend.DaemonAsync(token) != daemon) throw new InvalidOperationException("Daemon changed; original allocation preserved.");
        return await backend.InspectAsync(identity, token); // Only native HTTP404 is absence, never arbitrary failure.
    }

    private void Validate(BillingBackendIdentity observed)
    {
        if (!baselineAbsent || observed.Id != originalId || observed.Name != "/" + backend.Name
            || observed.CreatedUtc.Kind != DateTimeKind.Utc || observed.CreatedUtc < backend.IntentUtc || observed.CreatedUtc > DateTime.UtcNow
            || !Regex.IsMatch(observed.ImageId, "\\Asha256:[a-f0-9]{64}\\z") || observed.ConfiguredImage != "postgres:18-alpine"
            || observed.Owner != "web-billing-proof" || observed.Run != backend.Run || observed.ExpiresUtc != backend.ExpiresUtc
            || !observed.EnvelopeValid || string.IsNullOrEmpty(observed.Signature) || observed.Signature.Length > 8192)
            throw new InvalidOperationException("Original billing backend generation/envelope mismatch; cleanup refused.");
    }

    private async Task<bool> ReobserveAbsentAsync(CancellationToken token, bool requireSdkIdentity = true)
    {
        if (original is null || originalId is null || requireSdkIdentity && backend.SdkId != originalId)
            throw new InvalidOperationException("Uncaptured original backend cannot authorize cleanup.");
        var byId = await InspectAsync(originalId, token);
        var byName = await InspectAsync(backend.Name, token);
        if (byId is null && byName is null) return true;
        if (byId is null || byName is null || byName.Id != originalId || byId.Id != originalId)
            throw new InvalidOperationException("Backend name/ID generation changed; foreign resource preserved.");
        Validate(byId); Validate(byName);
        if (byId != original || byName != original)
            throw new InvalidOperationException("Original creation envelope changed; cleanup refused.");
        return false;
    }

    internal async ValueTask CloseAsync()
    {
        if (SdkReleased) return;
        closeStarted = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        if (!startDispatched)
        {
            // SDK wrapper never dispatched a create/start; do not touch a colliding daemon name.
            await backend.DisposeSdkAsync().AsTask().WaitAsync(deadline.Token);
            backend.Dispose(); SdkReleased = true;
            return;
        }
        if (!startupSettled) throw new InvalidOperationException("Backend startup remains unsettled; ownership retained.");
        if (!await ReobserveAbsentAsync(deadline.Token))
        {
            await backend.StopAsync(originalId!, deadline.Token); // Only validated original full CID.
            if (!await ReobserveAbsentAsync(deadline.Token))
            {
                await backend.RemoveAsync(originalId!, deadline.Token);
                if (!await ReobserveAbsentAsync(deadline.Token)) throw new InvalidOperationException("Exact backend absence unverified.");
            }
        }
        await backend.DisposeSdkAsync().AsTask().WaitAsync(deadline.Token);
        // SDK disposal intentionally clears its container wrapper after HTTP404; the sealed original CID remains authoritative.
        if (!await ReobserveAbsentAsync(deadline.Token, requireSdkIdentity: false)) throw new InvalidOperationException("Backend reappeared after SDK disposal.");
        AbsenceVerified = true;
        backend.Dispose(); SdkReleased = true;
    }
}

internal sealed class BillingDockerBackend : IBillingBackend
{
    private readonly DockerClient docker;
    public string Run { get; }
    public string Name { get; }
    public string Database { get; }
    public string ExpiresUtc { get; } = DateTimeOffset.UtcNow.AddMinutes(50).ToString("O");
    public DateTime IntentUtc { get; } = DateTime.UtcNow;
    public PostgreSqlContainer Postgres { get; }
    public string? SdkId { get { try { return Postgres.Id; } catch (InvalidOperationException) { return null; } } }
    private const string Command = "exec timeout -k 10 3000 docker-entrypoint.sh postgres";
    internal BillingDockerBackend(string database, string run)
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_CONTEXT")))
            throw new InvalidOperationException("Actual billing PostgreSQL requires the disposable hosted local socket.");
        if (!Regex.IsMatch(database, "\\Aprofile_contract_[a-f0-9]{32}\\z")) throw new InvalidOperationException("Synthetic disposable billing database required.");
        Database = database; Run = run; Name = "billing-proof-" + run + "-postgres";
        var endpoint = new Uri("unix:///var/run/docker.sock");
        docker = new DockerClientBuilder().WithEndpoint(endpoint).WithTimeout(TimeSpan.FromSeconds(15)).Build();
        try
        {
            Postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase(database).WithDockerEndpoint(endpoint)
                .WithCleanUp(false).WithName(Name).WithLabel("maliev.codex.owner", "web-billing-proof")
                .WithLabel("maliev.codex.run", Run).WithLabel("maliev.codex.persistent-data", "false").WithLabel("maliev.codex.expires-utc", ExpiresUtc)
                .WithCreateParameterModifier(parameters =>
                {
                    parameters.Entrypoint = ["sh", "-c"]; parameters.Cmd = [Command];
                    var host = parameters.HostConfig ?? throw new InvalidOperationException("Billing create host configuration unavailable.");
                    var ports = host.PortBindings ?? throw new InvalidOperationException("Billing create port bindings unavailable.");
                    host.Memory = 536870912; host.MemorySwap = 536870912;
                    host.NanoCPUs = 1000000000;
                    host.Tmpfs = new Dictionary<string, string> { ["/var/lib/postgresql"] = "rw,size=268435456" };
                    foreach (var bindings in ports.Values)
                    {
                        if (bindings is null) throw new InvalidOperationException("Billing create port binding list unavailable.");
                        foreach (var binding in bindings)
                        {
                            if (binding is null) throw new InvalidOperationException("Billing create port binding unavailable.");
                            binding.HostIP = "127.0.0.1";
                        }
                    }
                }).Build();
        }
        catch { docker.Dispose(); throw; }
    }
    public async Task<string> DaemonAsync(CancellationToken token) => (await docker.System.GetSystemInfoAsync(token)).ID;
    public Task StartAsync(CancellationToken token) => Postgres.StartAsync(token);
    public async Task<BillingBackendIdentity?> InspectAsync(string identity, CancellationToken token)
    {
        ContainerInspectResponse actual;
        try { actual = await docker.Containers.InspectContainerAsync(identity, token); }
        catch (DockerApiException failure) when (failure.StatusCode == HttpStatusCode.NotFound) { return null; }
        var config = actual.Config ?? throw new InvalidOperationException("Original backend configuration unavailable.");
        var host = actual.HostConfig ?? throw new InvalidOperationException("Original backend host configuration unavailable.");
        var labels = config.Labels ?? throw new InvalidOperationException("Original backend labels unavailable.");
        var entrypoint = config.Entrypoint ?? throw new InvalidOperationException("Original backend entrypoint unavailable.");
        var command = config.Cmd ?? throw new InvalidOperationException("Original backend command unavailable.");
        var tmpfs = host.Tmpfs ?? throw new InvalidOperationException("Original backend tmpfs configuration unavailable.");
        var ports = host.PortBindings ?? throw new InvalidOperationException("Original backend port bindings unavailable.");
        var mounts = actual.Mounts ?? throw new InvalidOperationException("Original backend mounts unavailable.");
        if (ports.Values.Any(value => value is null || value.Any(binding => binding is null)) || mounts.Any(value => value is null))
            throw new InvalidOperationException("Original backend envelope contains unavailable binding or mount entries.");
        string Label(string key) => labels.TryGetValue(key, out var value) ? value : "";
        var valid = host.Memory == 536870912 && host.MemorySwap == 536870912 && host.NanoCPUs == 1000000000
            && entrypoint.SequenceEqual(new[] { "sh", "-c" }) && command.SequenceEqual(new[] { Command })
            && tmpfs.Count == 1 && tmpfs.TryGetValue("/var/lib/postgresql", out var mount) && mount == "rw,size=268435456"
            && host.Binds is not { Count: > 0 } && host.Mounts is not { Count: > 0 }
            && mounts.All(value => value.Type == "tmpfs" && value.Destination == "/var/lib/postgresql" && value.RW)
            && ports.Count > 0 && ports.Values.SelectMany(value => value).All(value => value.HostIP == "127.0.0.1")
            && Label("maliev.codex.persistent-data") == "false";
        var signature = JsonSerializer.Serialize(new
        {
            actual.ID,
            actual.Created,
            actual.Image,
            actual.Name,
            configuredImage = config.Image,
            owner = Label("maliev.codex.owner"),
            run = Label("maliev.codex.run"),
            expiresUtc = Label("maliev.codex.expires-utc"),
            persistentData = Label("maliev.codex.persistent-data"),
            host.Memory,
            host.MemorySwap,
            host.NanoCPUs,
            host.NetworkMode,
            cmd = command,
            entrypoint,
            tmpfs = tmpfs.OrderBy(value => value.Key).ToArray(),
            ports = ports.OrderBy(value => value.Key).ToArray(),
            mounts = mounts.Select(value => new { value.Type, value.Source, value.Destination, value.RW }).OrderBy(value => value.Destination).ToArray(),
        });
        return new(actual.ID, actual.Created, actual.Image, actual.Name, Label("maliev.codex.owner"), Label("maliev.codex.run"),
            Label("maliev.codex.expires-utc"), config.Image, valid, signature);
    }
    public async Task StopAsync(string originalId, CancellationToken token)
    {
        await docker.Containers.StopContainerAsync(originalId, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, token);
    }
    public async Task RemoveAsync(string originalId, CancellationToken token)
    {
        var stopped = await docker.Containers.InspectContainerAsync(originalId, token);
        var state = stopped.State ?? throw new InvalidOperationException("Original backend stop state unavailable; removal refused.");
        if (stopped.ID != originalId || state.Running) throw new InvalidOperationException("Original backend did not stop; removal refused.");
        await docker.Containers.RemoveContainerAsync(originalId, new ContainerRemoveParameters { Force = false, RemoveVolumes = false }, token);
    }
    public ValueTask DisposeSdkAsync() => Postgres.DisposeAsync();
    public void Dispose() => docker.Dispose();
}

internal sealed class BillingHostLease(Func<ValueTask> dispose, bool startupRequired, TimeSpan? closeTimeout = null)
{
    private readonly object disposalGate = new();
    private Task? disposal;
    private bool startupComplete = !startupRequired;
    internal object Receipt { get { lock (disposalGate) return new { startupComplete, disposal = disposal?.Status.ToString() }; } }
    internal void Starting() { lock (disposalGate) startupComplete = false; }
    internal void Started() { lock (disposalGate) startupComplete = true; }
    internal async Task CloseAsync()
    {
        Task attempt;
        TaskCompletionSource? reservation = null;
        lock (disposalGate)
        {
            if (!startupComplete) throw new InvalidOperationException("Interrupted host startup is unjoined; retain graph until hosted worker expiry.");
            if (disposal is null)
            {
                // Publish the sticky attempt before any arbitrary callback can execute.
                reservation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = reservation.Task;
            }
            attempt = disposal;
        }
        if (reservation is not null)
        {
            var published = reservation;
            // A synchronous Playwright/WAF disposal prefix must not defeat the wait bound.
            _ = Task.Run(async () =>
            {
                try { await dispose(); published.TrySetResult(); }
                catch (Exception failure) { published.TrySetException(failure); }
            });
        }
        // Timeout retains the same pending task; failure remains sticky across retries.
        await attempt.WaitAsync(closeTimeout ?? TimeSpan.FromSeconds(30));
    }
}

internal interface IBillingChildProcess
{
    object Receipt { get; }
    bool ReaderFailed { get; }
    bool StartRefused { get; }
    void Start();
    void CaptureIdentity();
    void StartReaders();
    bool VerifyNoLiveChild();
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync();
    void RequestGracefulStop();
    void KillExact();
    Task JoinReadersAsync();
    void CloseReaders();
    void CloseHandle();
}

internal sealed class BillingChildLease(IBillingChildProcess process)
{
    private readonly SemaphoreSlim cleanup = new(1, 1);
    private bool dispatched;
    private bool identityCaptured;
    private bool exitVerified;
    private bool noChildVerified;
    private bool readersClosed;
    private bool released;
    private Task? readerJoin;
    internal object Receipt => new { dispatched, identityCaptured, exitVerified, noChildVerified, readersClosed, released, process = process.Receipt };
    internal bool HasExited => process.HasExited;
    internal int ExitCode => process.ExitCode;
    internal bool ReaderFailed => process.ReaderFailed;
    internal Task WaitForExitAsync() => process.WaitForExitAsync();
    internal void Start()
    {
        dispatched = true;
        process.Start();
        process.CaptureIdentity();
        identityCaptured = true;
        process.StartReaders();
    }
    internal async Task CloseAsync()
    {
        await cleanup.WaitAsync();
        try
        {
            if (released) return;
            if (dispatched && !identityCaptured)
            {
                // Metadata acquisition may have failed after successful Start; recover only the original handle.
                if (process.VerifyNoLiveChild()) { noChildVerified = process.StartRefused; exitVerified = !noChildVerified; }
                else { process.CaptureIdentity(); identityCaptured = true; }
            }
            if (!exitVerified && !noChildVerified)
            {
                if (!process.HasExited)
                {
                    process.RequestGracefulStop();
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                    catch (TimeoutException) { /* Exact retained process is still owned. */ }
                    if (!process.HasExited) process.KillExact();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                if (!process.HasExited) throw new InvalidOperationException("Owned process exit remains unverified.");
                exitVerified = true;
            }
            if (readerJoin is null || readerJoin.IsFaulted || readerJoin.IsCanceled)
                readerJoin = process.JoinReadersAsync();
            await readerJoin.WaitAsync(TimeSpan.FromSeconds(5));
            if (!readersClosed) { process.CloseReaders(); readersClosed = true; }
            process.CloseHandle();
            released = true;
        }
        finally { cleanup.Release(); }
    }
}

internal sealed class BillingSystemChild(ProcessStartInfo start) : IBillingChildProcess
{
    // This original object is allocated before registration/Start; it is never replaced by a PID lookup.
    private readonly Process process = new() { StartInfo = start };
    private int pid;
    private DateTime birth;
    private string? executable;
    internal DateTimeOffset ExpiresUtc { get; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public object Receipt => new { pid, birth, executable, startRefused, ExpiresUtc, stdout = stdout?.Status.ToString(), stderr = stderr?.Status.ToString(), stdoutClosed, stderrClosed };
    public bool StartRefused => startRefused;
    public bool ReaderFailed => stdout?.IsFaulted == true || stderr?.IsFaulted == true;
    private Task? stdout;
    private Task? stderr;
    private Task? exitWait;
    private bool stdoutClosed;
    private bool stderrClosed;
    private bool startRefused;
    public bool HasExited => process.HasExited;
    public int ExitCode => process.ExitCode;
    public void Start()
    {
        if (!process.Start()) { startRefused = true; throw new InvalidOperationException("Owned process start was refused."); }
        // A thrown Start is deliberately not relabelled as proven absence.
    }
    public bool VerifyNoLiveChild() => startRefused || process.HasExited; // Original wrapper/handle only, never a PID-name lookup.
    public void CaptureIdentity()
    {
        _ = process.SafeHandle; // Retain exact process handle before later metadata/reader failures.
        var actualPid = process.Id;
        var actualBirth = process.StartTime.ToUniversalTime();
        var actualExecutable = process.MainModule?.FileName ?? throw new InvalidOperationException("Owned executable identity unavailable.");
        if (pid != 0 && (pid != actualPid || birth != actualBirth || !string.Equals(executable, actualExecutable, StringComparison.Ordinal)))
            throw new InvalidOperationException("Owned process identity changed; mutation refused.");
        if (!process.HasExited)
        {
            using var current = Process.GetProcessById(actualPid);
            if (current.StartTime.ToUniversalTime() != actualBirth
                || !string.Equals(current.MainModule?.FileName, actualExecutable, StringComparison.Ordinal))
                throw new InvalidOperationException("Current PID birth/executable differs from original owned process; mutation refused.");
        }
        pid = actualPid; birth = actualBirth; executable = actualExecutable;
    }
    public void StartReaders()
    {
        stdout = DrainAsync(process.StandardOutput);
        stderr = DrainAsync(process.StandardError);
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        long count = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0) return;
            count += read;
            if (count > 16L * 1024 * 1024) throw new InvalidOperationException("Owned child output quota exceeded; raw output suppressed.");
        }
    }
    public Task WaitForExitAsync() => exitWait ??= process.WaitForExitAsync();
    public void RequestGracefulStop() { CaptureIdentity(); process.CloseMainWindow(); }
    public void KillExact() { CaptureIdentity(); process.Kill(); }
    public async Task JoinReadersAsync()
    {
        foreach (var reader in new[] { stdout, stderr }.OfType<Task>())
        {
            try { await reader; }
            catch when (reader.IsCompleted) { _ = reader.Exception; } // Faulted but settled; never an active reader.
        }
    }
    public void CloseReaders()
    {
        if (startRefused) { stdoutClosed = true; stderrClosed = true; return; } // No redirected child streams were allocated.
        var failures = new List<string>();
        try { if (!stdoutClosed) { process.StandardOutput.Dispose(); stdoutClosed = true; } }
        catch (Exception failure) { failures.Add("stdout:" + failure.GetType().Name); }
        try { if (!stderrClosed) { process.StandardError.Dispose(); stderrClosed = true; } }
        catch (Exception failure) { failures.Add("stderr:" + failure.GetType().Name); }
        if (failures.Count != 0) throw new IOException("Owned reader close unresolved: " + string.Join(",", failures));
    }
    public void CloseHandle() => process.Dispose();
}
