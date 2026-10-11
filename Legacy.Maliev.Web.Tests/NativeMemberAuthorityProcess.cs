using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Legacy.Maliev.Web.Tests;

internal sealed class NativeMemberAuthorityProcess(string dll, Dictionary<string, string> environment, Dictionary<string, string>? hostileAmbient = null) : ICompanyProcessBackend
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NativeMemberAuthorityProcess> Retained = new();
    private readonly string ownershipId = Guid.NewGuid().ToString("N");
    private readonly Process process = new();
    private readonly CancellationTokenSource drainCancellation = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> errors = new();
    private SafeFileHandle? signalHandle;
    private Task stdout = Task.CompletedTask;
    private Task stderr = Task.CompletedTask;
    private DateTime? startedUtc;
    public bool Started { get; private set; }
    private bool exitVerified;
    private bool released;
    public bool Released => released;
    public bool HasExited => exitVerified || !Started || process.HasExited;

    public static void Preflight()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/timeout"))
            throw new PlatformNotSupportedException("Member authority resource successor requires explicitly reviewed genuine Linux hosted execution; no Windows or fake-Linux acceptance.");
        using (OpenPidHandle(Environment.ProcessId)) { }
    }
    public int ExitCode => process.ExitCode;
    public string BoundedErrors => string.Join("\n", errors);

    public void Start()
    {
        Preflight();
        // Probe support before creating a child. No PID-based signal fallback is allowed.
        using (OpenPidHandle(Environment.ProcessId)) { }
        // GNU timeout creates and waits for its own command child. Foreground mode
        // targets only that command, without process-group or descendant selectors.
        // The supervisor invocation precedes dotnet; GNU establishes its timer after fork.
        // Timer establishment, installed version and native behavior require hosted proof.
        var start = new ProcessStartInfo("/usr/bin/timeout")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--foreground");
        start.ArgumentList.Add("--signal=TERM");
        start.ArgumentList.Add("--kill-after=5s");
        start.ArgumentList.Add("180s");
        start.ArgumentList.Add("dotnet");
        start.ArgumentList.Add(dll);
        if (hostileAmbient is not null)
            foreach (var item in hostileAmbient) start.Environment[item.Key] = item.Value;
        var essentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)",
        };
        var inheritedEssentials = start.Environment.Where(item => essentials.Contains(item.Key)).ToArray();
        start.Environment.Clear();
        foreach (var item in inheritedEssentials) start.Environment[item.Key] = item.Value;
        foreach (var value in environment) start.Environment[value.Key] = value.Value;
        start.Environment["LEGACY_DEPLOY_ENABLED"] = "false";
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
        Console.WriteLine("[member-owned-resource] " + System.Text.Json.JsonSerializer.Serialize(new
        {
            ownershipId,
            state,
            run = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
            purpose = Path.GetFileName(dll),
            pid,
            spawnObservedUtc = startedUtc,
            executable = "/usr/bin/timeout",
            managedExecutable = "dotnet",
            independentDeadlineSeconds = 180,
            independentKillAfterSeconds = 5,
            expiresUtc = startedUtc?.AddSeconds(185),
            persistentData = false,
            cleanupVerified = false,
        }));
    }

    public void CaptureOwnership()
    {
        EnsureSignalHandle();
        var actualStart = process.StartTime.ToUniversalTime();
        Console.WriteLine("[member-owned-birth] " + System.Text.Json.JsonSerializer.Serialize(new
        { ownershipId, pid = process.Id, actualStart, executable = process.StartInfo.FileName, kernelSignalHandle = signalHandle is not null }));
    }

    public void BeginDrains()
    {
        stdout = ReadBoundedAsync(process.StandardOutput, retainErrors: false);
        stderr = ReadBoundedAsync(process.StandardError, retainErrors: false);
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
        if (released) return;
        if (Started && !HasExited)
        {
            RecordSpawnOwnership("retained-unverified-live-supervisor");
            // Retain the actual Process object and recorded deadline, rather than
            // silently dropping its ownership handle while a child may remain.
            throw new InvalidOperationException("Company supervisor exit unverified; owned handle retained, cleanup failed.");
        }
        exitVerified = true;
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
        released = true;
        drainCancellation.Dispose();
        Console.WriteLine("[member-owned-resource-released] " + System.Text.Json.JsonSerializer.Serialize(new
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
