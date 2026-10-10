using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Docker.DotNet;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Finite new CNC fixture resources; no output content, credentials or persistent data in receipts.</summary>
internal sealed class CncOwnedResourceScope
{
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Pid, DateTime Start), OwnedChild> RetainedCustody = new();
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IContainer> RetainedProviders = new();
    internal string RunId { get; } = Guid.NewGuid().ToString("N");
    internal DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddMinutes(3);
    private readonly List<OwnedChild> children = [];
    private readonly List<(IContainer Container, long Memory, long Cpu)> containers = [];
    internal List<object> Receipts { get; } = [];
    internal Func<IDockerClient> DockerFactory { get; set; } = () => new DockerClientBuilder()
        .WithEndpoint(TestcontainersSettings.OS.DockerEndpointAuthConfig.Endpoint)
        .WithTimeout(TimeSpan.FromSeconds(15)).Build();
    internal bool AcquisitionRejectionControl { get; set; }
    internal static async Task ExecuteAsync(Func<CncOwnedResourceScope, Task> body, Action<IReadOnlyList<object>>? inspect = null)
    {
        var scope = new CncOwnedResourceScope();
        Exception? primary = null;
        var cleanup = new List<string>();
        try { await body(scope); }
        catch (Exception exception) { primary = exception; }
        finally
        {
            foreach (var child in scope.children.AsEnumerable().Reverse())
            {
                try { await child.CloseAsync(); }
                catch (Exception exception) { cleanup.Add(exception.GetType().Name); }
            }
            IDockerClient? docker = null;
            // Acquisition is itself a cleanup phase; process-only controls need no Docker client.
            if (scope.containers.Count != 0 || scope.AcquisitionRejectionControl)
            {
                try { docker = scope.DockerFactory(); }
                catch (Exception exception)
                {
                    cleanup.Add("docker-acquisition:" + exception.GetType().Name);
                    scope.Receipts.Add(new { kind = "cleanup-fault", phase = "docker-acquisition", scope.RunId, scope.Expires, containerCount = scope.containers.Count });
                }
            }
            foreach (var item in scope.containers.AsEnumerable().Reverse())
            {
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var id = item.Container.Id;
                    if (docker is null)
                    {
                        RetainedProviders[scope.RunId + ":" + scope.containers.IndexOf(item)] = item.Container;
                        scope.Receipts.Add(new { kind = "unresolved-container", id, scope.RunId, scope.Expires, purpose = "disposable CNC provider", ownershipReobservationRequired = true, handleRetained = true });
                        continue;
                    }
                    if (!string.IsNullOrEmpty(id))
                    {
                        var actual = await docker.Containers.InspectContainerAsync(id, deadline.Token);
                        var labels = actual.Config?.Labels ?? throw new InvalidOperationException("Disposable provider labels unavailable.");
                        var host = actual.HostConfig ?? throw new InvalidOperationException("Disposable provider host configuration unavailable.");
                        var mounts = actual.Mounts ?? throw new InvalidOperationException("Disposable provider mounts unavailable.");
                        if (actual.ID != id || labels["maliev.cnc.run"] != scope.RunId
                            || labels["maliev.cnc.expires"] != scope.Expires.ToString("o")
                            || host.Memory != item.Memory || host.NanoCPUs != item.Cpu
                            || mounts.Any(mount => mount is null || mount.Type == "bind"))
                            throw new InvalidOperationException("Container ownership or disposable mounts mismatch; preserved.");
                        await item.Container.DisposeAsync().AsTask().WaitAsync(deadline.Token);
                        var remaining = await docker.Containers.ListContainersAsync(new() { All = true }, deadline.Token);
                        if (remaining.Any(container => container.ID == id)) throw new InvalidOperationException("Owned container remains.");
                        scope.Receipts.Add(new { kind = "container", id, scope.RunId, scope.Expires, actual.Created, memory = host.Memory, cpu = host.NanoCPUs, persistentData = false, absent = true });
                    }
                    else await item.Container.DisposeAsync().AsTask().WaitAsync(deadline.Token);
                }
                catch (Exception exception)
                {
                    cleanup.Add(exception.GetType().Name);
                    RetainedProviders[scope.RunId + ":" + scope.containers.IndexOf(item)] = item.Container;
                    string? id = null;
                    try { id = item.Container.Id; }
                    catch { }
                    scope.Receipts.Add(new { kind = "unresolved-container", id, scope.RunId, scope.Expires, purpose = "disposable CNC provider cleanup recovery", handleRetained = true, cleanupFailure = exception.GetType().Name, continuationLease = scope.Expires, ownershipReobservationRequired = true });
                }
            }
            try { docker?.Dispose(); }
            catch (Exception exception) { cleanup.Add("docker-disposal:" + exception.GetType().Name); }
            try { inspect?.Invoke(scope.Receipts); }
            catch (Exception exception) { cleanup.Add(exception.GetType().Name); }
            try { Console.WriteLine(JsonSerializer.Serialize(new { scope.RunId, scope.Expires, resources = scope.Receipts, cleanupFailures = cleanup, primaryFailure = primary?.GetType().Name })); }
            catch (Exception exception) { cleanup.Add("receipt-output:" + exception.GetType().Name); }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        if (cleanup.Count != 0) throw new InvalidOperationException("Owned fixture cleanup failed; receipt retained.");
    }
    internal T OwnContainer<T>(T container, long memory, long cpu) where T : IContainer
    {
        containers.Add((container, memory, cpu));
        return container;
    }
    internal async Task StartContainerAsync(IContainer container, CancellationToken token)
    {
        await container.StartAsync(token);
        using var docker = DockerFactory();
        var actual = await docker.Containers.InspectContainerAsync(container.Id, token);
        var expected = containers.Single(item => ReferenceEquals(item.Container, container));
        var labels = actual.Config?.Labels ?? throw new InvalidOperationException("Disposable provider labels unavailable.");
        var host = actual.HostConfig ?? throw new InvalidOperationException("Disposable provider host configuration unavailable.");
        var mounts = actual.Mounts ?? throw new InvalidOperationException("Disposable provider mounts unavailable.");
        if (actual.ID != container.Id || labels["maliev.cnc.run"] != RunId
            || labels["maliev.cnc.expires"] != Expires.ToString("o")
            || host.Memory != expected.Memory || host.NanoCPUs != expected.Cpu
            || mounts.Any(mount => mount is null || mount.Type == "bind"))
            throw new InvalidOperationException("Disposable provider ownership/caps inspection failed.");
    }
    internal OwnedChild StartChild(string dll, Dictionary<string, string> environment, Action? afterStart = null)
    {
        var child = new OwnedChild(this);
        children.Add(child); // Register before any start/identity/reader step can fail.
        child.Start(dll, environment, afterStart);
        return child;
    }
    internal sealed class OwnedChild(CncOwnedResourceScope scope)
    {
        // Internal rejection seams are confined to this new test helper, never production code.
        internal bool RejectIdentity { get; set; }
        internal bool RejectStop { get; set; }
        internal TaskCompletionSource<bool>? ReaderRelease { get; set; }
        internal Process Process { get; } = new();
        private readonly CancellationTokenSource readerStop = new();
        private readonly SemaphoreSlim stopping = new(1);
        private Task stdout = Task.CompletedTask, stderr = Task.CompletedTask, monitor = Task.CompletedTask;
        private DateTime start;
        private string? executable;
        private int pid;
        private bool started, closed;
        private long stdoutBytes, stderrBytes;
        private string? budgetFailure;
        internal void Start(string dll, Dictionary<string, string> environment, Action? afterStart)
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(dll);
            foreach (var entry in environment) info.Environment[entry.Key] = entry.Value;
            info.Environment["DOTNET_GCHeapHardLimit"] = "0x10000000";
            Process.StartInfo = info;
            if (!Process.Start()) throw new InvalidOperationException("Owned child failed to start.");
            started = true;
            pid = Process.Id;
            start = Process.StartTime.ToUniversalTime();
            executable = Process.MainModule?.FileName ?? throw new InvalidOperationException("Executable identity unavailable.");
            _ = Process.SafeHandle; // Retain handle through terminal observation and reader settlement.
            stdout = ReadAndSettle(Process.StandardOutput.BaseStream, true);
            stderr = ReadBounded(Process.StandardError.BaseStream, false);
            monitor = Monitor();
            afterStart?.Invoke();
        }
        private async Task ReadAndSettle(Stream stream, bool output)
        {
            await ReadBounded(stream, output);
            if (ReaderRelease is not null) await ReaderRelease.Task;
        }
        private async Task ReadBounded(Stream stream, bool output)
        {
            var buffer = new byte[4096]; // No line parser, ReadToEnd or output-content accumulation.
            try
            {
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, readerStop.Token);
                    if (count == 0) return;
                    var bytes = output ? Interlocked.Add(ref stdoutBytes, count) : Interlocked.Add(ref stderrBytes, count);
                    if (bytes > 65536)
                    {
                        budgetFailure = "output-quota";
                        await StopExact();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (readerStop.IsCancellationRequested) { }
            catch (IOException) when (readerStop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (readerStop.IsCancellationRequested) { }
        }
        private async Task Monitor()
        {
            while (!Process.HasExited)
            {
                Process.Refresh();
                if (DateTimeOffset.UtcNow >= scope.Expires || Process.WorkingSet64 > 536870912
                    || Process.TotalProcessorTime > TimeSpan.FromSeconds(60))
                {
                    budgetFailure = "finite-process-budget";
                    await StopExact();
                    return;
                }
                await Task.Delay(50);
            }
        }
        internal async Task CompleteAsync(TimeSpan timeout, CancellationToken token)
        {
            try
            {
                await Process.WaitForExitAsync(token).WaitAsync(timeout, token);
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5), token);
                if (budgetFailure is not null) throw new InvalidDataException(budgetFailure);
            }
            catch (TimeoutException)
            {
                budgetFailure = "phase-timeout";
                await StopExact();
                throw;
            }
        }
        private void VerifyIdentity()
        {
            Process.Refresh();
            if (RejectIdentity || Process.Id != pid || Process.StartTime.ToUniversalTime() != start
                || Process.MainModule?.FileName != executable)
                throw new InvalidOperationException("Owned PID/birth/executable identity changed; preserve process.");
        }
        private async Task StopExact()
        {
            await stopping.WaitAsync();
            try
            {
                if (Process.HasExited) return;
                if (RejectStop) throw new InvalidOperationException("Controlled stop refusal.");
                VerifyIdentity();
                if (OperatingSystem.IsLinux()) _ = SendSignal(pid, 15);
                else Process.CloseMainWindow();
                if (!Process.WaitForExit(250))
                {
                    VerifyIdentity();
                    Process.Kill(); // Exact retained child, never name/tree selection.
                }
                await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { stopping.Release(); }
        }
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int SendSignal(int pid, int signal);
        internal async Task CloseAsync()
        {
            if (closed) return;
            var settled = false;
            try
            {
                if (started)
                {
                    await StopExact();
                    readerStop.Cancel();
                    Process.StandardOutput.BaseStream.Dispose();
                    Process.StandardError.BaseStream.Dispose();
                    await Task.WhenAll(stdout, stderr, monitor).WaitAsync(TimeSpan.FromSeconds(5));
                    if (!Process.HasExited) throw new InvalidOperationException("Owned child remains.");
                    scope.Receipts.Add(new { kind = "process", pid, start, executable, scope.RunId, scope.Expires, stdoutBytes, stderrBytes, budgetFailure, terminal = true, readersSettled = true, exit = Process.ExitCode, persistentData = false, workingSetCap = 536870912, cpuSecondsCap = 60, memoryEnforcement = "50ms observed working set plus256MiB GC hard limit" });
                    RetainedCustody.TryRemove((pid, start), out _);
                }
                settled = true;
            }
            catch (Exception exception)
            {
                bool? terminal = null;
                try { terminal = started ? Process.HasExited : true; }
                catch { }
                RetainedCustody[(pid, start)] = this;
                scope.Receipts.Add(new { kind = "unresolved-process", pid, start, executable, scope.RunId, scope.Expires, purpose = "CNC child cleanup recovery", terminal, readersSettled = stdout.IsCompleted && stderr.IsCompleted && monitor.IsCompleted, handleRetained = true, closed = false, cleanupFailure = exception.GetType().Name, continuationLease = scope.Expires, reobserveBeforeAnySignal = true });
                throw;
            }
            finally
            {
                // Never erase custody or destroy handles still used by active reader/monitor tasks.
                if (settled)
                {
                    closed = true;
                    Process.Dispose();
                    readerStop.Dispose();
                    stopping.Dispose();
                }
            }
        }
    }
}
