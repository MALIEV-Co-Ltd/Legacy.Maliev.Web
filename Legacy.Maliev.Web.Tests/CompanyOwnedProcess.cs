using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Legacy.Maliev.Web.Tests;

internal interface ICompanyProcessBackend : IAsyncDisposable
{
    bool Started { get; }
    bool HasExited { get; }
    void Start();
    void CaptureOwnership();
    void BeginDrains();
    void Signal(bool force);
    Task WaitAsync(CancellationToken cancellationToken);
    Task DrainAsync(CancellationToken cancellationToken);
    Task WaitForLeaseExitAsync(CancellationToken cancellationToken);
}

internal sealed record CompanyProcessBudgets(TimeSpan Graceful, TimeSpan Forced, TimeSpan Drain)
{
    public static CompanyProcessBudgets Normal { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
}

internal sealed class CompanyOwnedProcess(ICompanyProcessBackend backend, CompanyProcessBudgets budgets) : IAsyncDisposable
{
    public static async Task<CompanyOwnedProcess> StartAsync(ICompanyProcessBackend backend, CompanyProcessBudgets? budgets = null)
    {
        var owned = new CompanyOwnedProcess(backend, budgets ?? CompanyProcessBudgets.Normal);
        try
        {
            // Ownership object exists before any operation which can spawn or fail after spawn.
            backend.Start();
            backend.CaptureOwnership();
            backend.BeginDrains();
            return owned;
        }
        catch (Exception startup)
        {
            try { await owned.DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException("Owned company startup and cleanup failed.", startup, cleanup); }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try
        {
            if (backend.Started)
            {
                try { backend.Signal(force: false); } catch (Exception error) { failures.Add(error); }
                try
                {
                    using var timeout = new CancellationTokenSource(budgets.Graceful);
                    await backend.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) { /* Escalate a bounded graceful timeout. */ }
                catch (Exception error) { failures.Add(error); }
                bool exited;
                try { exited = backend.HasExited; } catch (Exception error) { failures.Add(error); exited = false; }
                if (!exited)
                {
                    try { backend.Signal(force: true); } catch (Exception error) { failures.Add(error); }
                    try
                    {
                        using var timeout = new CancellationTokenSource(budgets.Forced);
                        await backend.WaitAsync(timeout.Token);
                    }
                    catch (Exception error) { failures.Add(error); }
                }
                try
                {
                    if (!backend.HasExited)
                    {
                        // The foreground supervisor establishes its deadline after forking its command.
                        // Await its actual exit; its lease alone is never reported as cleanup.
                        using var leaseWait = new CancellationTokenSource(TimeSpan.FromSeconds(190));
                        await backend.WaitForLeaseExitAsync(leaseWait.Token);
                    }
                }
                catch (Exception error) { failures.Add(error); }
                try
                {
                    if (!backend.HasExited) failures.Add(new InvalidOperationException("Owned company child did not exit."));
                }
                catch (Exception error) { failures.Add(error); }
                try
                {
                    using var timeout = new CancellationTokenSource(budgets.Drain);
                    await backend.DrainAsync(timeout.Token);
                }
                catch (Exception error) { failures.Add(error); }
            }
        }
        finally
        {
            try { await backend.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("Owned company cleanup did not fully succeed.", failures);
    }
}

internal sealed class NativeCompanyProcess(string dll, Dictionary<string, string> environment) : ICompanyProcessBackend
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NativeCompanyProcess> Retained = new();
    private readonly string ownershipId = Guid.NewGuid().ToString("N");
    private readonly Process process = new();
    private readonly CancellationTokenSource drainCancellation = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> errors = new();
    private SafeFileHandle? signalHandle;
    private Task stdout = Task.CompletedTask;
    private Task stderr = Task.CompletedTask;
    private DateTime? startedUtc;
    public bool Started { get; private set; }
    public bool HasExited => !Started || process.HasExited;
    public int ExitCode => process.ExitCode;
    public string BoundedErrors => string.Join("\n", errors);

    public void Start()
    {
        // Probe support before creating a child. No PID-based signal fallback is allowed.
        using (OpenPidHandle(Environment.ProcessId)) { }
        // GNU timeout creates and waits for its own command child. Foreground mode
        // targets only that command, without process-group or descendant selectors.
        // The supervisor invocation precedes dotnet; GNU establishes its timer after fork.
        // Timer establishment, installed version and native behavior require hosted proof.
        var start = new ProcessStartInfo("/usr/bin/timeout")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("--foreground");
        start.ArgumentList.Add("--signal=TERM");
        start.ArgumentList.Add("--kill-after=5s");
        start.ArgumentList.Add("180s");
        start.ArgumentList.Add("dotnet");
        start.ArgumentList.Add(dll);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Jwt", StringComparison.OrdinalIgnoreCase) || key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        foreach (var value in environment) start.Environment[value.Key] = value.Value;
        process.StartInfo = start;
        startedUtc = DateTime.UtcNow;
        try { Started = process.Start(); }
        catch
        {
            // A Process instance can retain an associated child even when post-start setup fails.
            try { _ = process.Id; Started = true; } catch (InvalidOperationException) { }
            if (Started) RecordSpawnOwnership("partial-start");
            throw;
        }
        if (!Started) throw new InvalidOperationException("Owned company child failed to start.");
        RecordSpawnOwnership("spawned-before-pidfd");
        // Acquire a kernel-bound signal handle before birth metadata or drain setup can fail.
        EnsureSignalHandle();
    }

    private void EnsureSignalHandle()
    {
        if (signalHandle is not null || HasExited) return;
        var handle = OpenPidHandle(process.Id);
        // This checks acquisition against the retained Process instance; it is not an atomic spawn/open claim.
        // All subsequent signals use this kernel handle, never a numeric PID.
        if (process.HasExited) { handle.Dispose(); return; }
        signalHandle = handle;
    }

    private void RecordSpawnOwnership(string state)
    {
        Retained[ownershipId] = this;
        int? pid = null;
        try { pid = process.Id; } catch (InvalidOperationException) { }
        Console.WriteLine("[company-owned-resource] " + System.Text.Json.JsonSerializer.Serialize(new
        {
            ownershipId, state, run = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"), purpose = Path.GetFileName(dll),
            pid, spawnObservedUtc = startedUtc, executable = "/usr/bin/timeout", managedExecutable = "dotnet",
            independentDeadlineSeconds = 180, independentKillAfterSeconds = 5,
            expiresUtc = startedUtc?.AddSeconds(185), persistentData = false, cleanupVerified = false,
        }));
    }

    public void CaptureOwnership()
    {
        EnsureSignalHandle();
        var actualStart = process.StartTime.ToUniversalTime();
        Console.WriteLine("[company-owned-birth] " + System.Text.Json.JsonSerializer.Serialize(new
        { ownershipId, pid = process.Id, actualStart, executable = process.StartInfo.FileName, kernelSignalHandle = signalHandle is not null }));
    }

    public void BeginDrains()
    {
        stdout = ReadBoundedAsync(process.StandardOutput, retainErrors: false);
        stderr = ReadBoundedAsync(process.StandardError, retainErrors: true);
    }

    private async Task ReadBoundedAsync(StreamReader reader, bool retainErrors)
    {
        var buffer = new char[512];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), drainCancellation.Token);
            if (count == 0) return;
            if (retainErrors && errors.Count < 30) errors.Enqueue(new string(buffer, 0, count));
        }
    }

    public void Signal(bool force)
    {
        if (HasExited) return;
        EnsureSignalHandle();
        var handle = signalHandle ?? throw new InvalidOperationException("Owned kernel signal handle unavailable.");
        if (PidSignal(handle.DangerousGetHandle().ToInt32(), force ? 14 : 15, IntPtr.Zero, 0) != 0 && !HasExited)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    public Task WaitAsync(CancellationToken cancellationToken) => Started ? process.WaitForExitAsync(cancellationToken) : Task.CompletedTask;
    public Task WaitForLeaseExitAsync(CancellationToken cancellationToken) => WaitAsync(cancellationToken);
    public Task DrainAsync(CancellationToken cancellationToken) => Task.WhenAll(stdout, stderr).WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Started && !HasExited)
        {
            RecordSpawnOwnership("retained-unverified-live-supervisor");
            // Retain the actual Process object and recorded deadline, rather than
            // silently dropping its ownership handle while a child may remain.
            throw new InvalidOperationException("Company supervisor exit unverified; owned handle retained, cleanup failed.");
        }
        var failures = new List<Exception>();
        drainCancellation.Cancel();
        if (Started)
        {
            try { process.StandardOutput.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { process.StandardError.Dispose(); } catch (Exception error) { failures.Add(error); }
        }
        try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (drainCancellation.IsCancellationRequested) { }
        catch (Exception error) { failures.Add(error); }
        signalHandle?.Dispose();
        process.Dispose();
        Retained.TryRemove(ownershipId, out _);
        drainCancellation.Dispose();
        Console.WriteLine("[company-owned-resource-released] " + System.Text.Json.JsonSerializer.Serialize(new
        { purpose = Path.GetFileName(dll), startedUtc, drainsCompleted = stdout.IsCompleted && stderr.IsCompleted, failureCount = failures.Count }));
        if (failures.Count != 0) throw new AggregateException("Owned company drain disposal failed.", failures);
    }

    private static SafeFileHandle OpenPidHandle(int pid)
    {
        var descriptor = PidOpen(pid, 0);
        if (descriptor < 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
    }

    [DllImport("libc", EntryPoint = "pidfd_open", SetLastError = true)]
    private static extern int PidOpen(int pid, uint flags);
    [DllImport("libc", EntryPoint = "pidfd_send_signal", SetLastError = true)]
    private static extern int PidSignal(int descriptor, int signal, IntPtr information, uint flags);
}
