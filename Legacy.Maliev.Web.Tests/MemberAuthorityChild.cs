using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Legacy.Maliev.Web.Tests;

internal static class MemberAuthorityCleanup
{
    internal static async Task CompleteAsync(Exception? primary, IEnumerable<Func<Task>> actions)
    {
        var failures = new List<Exception>();
        if (primary is not null) failures.Add(primary);
        foreach (var action in actions)
        {
            try { await action(); }
            catch (Exception exception) { failures.Add(exception); }
        }

        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Member authority cleanup retained all failures.", failures);
    }
}

// Only this type creates its Process; there is deliberately no borrowed-process attachment API.
internal enum MemberAuthorityStartStage
{
    Started,
    HandleCaptured,
    PidCaptured,
    StdoutCaptured,
    StderrCaptured,
}

internal sealed class MemberAuthorityChild : IAsyncDisposable
{
    internal static readonly ConcurrentDictionary<Guid, MemberAuthorityChild> RetainedCustody = new();
    internal Guid CreationToken { get; } = Guid.NewGuid();
    internal Process Process { get; } = new();
    internal bool WasStarted { get; private set; }
    internal bool IsClosed { get; private set; }
    internal bool ReadersSettled => stdout.IsCompleted && stderr.IsCompleted;
    internal DateTimeOffset Expires { get; private set; }
    private readonly CancellationTokenSource readerStop = new();
    private readonly CancellationTokenSource leaseStop = new();
    private readonly SemaphoreSlim stopping = new(1);
    private Task stdout = Task.CompletedTask;
    private Task stderr = Task.CompletedTask;
    private Task lease = Task.CompletedTask;
    private SafeProcessHandle? handle;
    private int pid;
    private DateTime birth;
    private string? executable;

    internal MemberAuthorityChild()
    {
        // Identity may be unavailable after a partial start; creation custody must never depend on PID zero.
        RetainedCustody[CreationToken] = this;
        Expires = DateTimeOffset.UtcNow.AddMinutes(30);
        lease = ExpireAsync();
    }

    internal void Start(ProcessStartInfo info, Action? afterStart = null,
        Action<MemberAuthorityStartStage>? observeInitialization = null)
    {
        if (WasStarted || IsClosed || info.UseShellExecute || !info.RedirectStandardOutput || !info.RedirectStandardError)
            throw new InvalidOperationException("Owned member child requires a fresh redirected process.");
        Process.StartInfo = info;
        if (!Process.Start()) throw new InvalidOperationException("Owned member child failed to start.");
        WasStarted = true;
        observeInitialization?.Invoke(MemberAuthorityStartStage.Started);
        handle = Process.SafeHandle;
        observeInitialization?.Invoke(MemberAuthorityStartStage.HandleCaptured);
        pid = Process.Id;
        observeInitialization?.Invoke(MemberAuthorityStartStage.PidCaptured);
        stdout = DrainAsync(Process.StandardOutput.BaseStream);
        observeInitialization?.Invoke(MemberAuthorityStartStage.StdoutCaptured);
        stderr = DrainAsync(Process.StandardError.BaseStream);
        observeInitialization?.Invoke(MemberAuthorityStartStage.StderrCaptured);
        _ = OwnedProcessMetricObservation.TryRead(Process, handle, pid, () =>
        {
            birth = Process.StartTime.ToUniversalTime();
            executable = ReadExecutableIdentity(Process);
        });
        afterStart?.Invoke();
    }

    private async Task DrainAsync(Stream stream)
    {
        var buffer = new byte[4096];
        try
        {
            while (await stream.ReadAsync(buffer.AsMemory(), readerStop.Token) != 0) { }
        }
        catch (OperationCanceledException) when (readerStop.IsCancellationRequested) { }
        catch (IOException) when (readerStop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (readerStop.IsCancellationRequested) { }
    }

    private async Task ExpireAsync()
    {
        try { await Task.Delay(TimeSpan.FromMinutes(30), leaseStop.Token); }
        catch (OperationCanceledException) when (leaseStop.IsCancellationRequested) { return; }
        await StopExactAsync();
        throw new TimeoutException("The exact owned member authority child exceeded its finite lease.");
    }

    internal void VerifyOwnership(Process candidate)
    {
        if (!ReferenceEquals(candidate, Process) || !WasStarted || handle is null || handle.IsClosed || handle.IsInvalid
            || !ReferenceEquals(candidate.SafeHandle, handle))
            throw new InvalidOperationException("Member process is not the retained creation-owned child.");
        candidate.Refresh();
        VerifyObservedIdentity(candidate.Id, candidate.StartTime.ToUniversalTime(), ReadExecutableIdentity(candidate));
    }

    internal void VerifyObservedIdentity(int actualPid, DateTime actualBirth, string? actualExecutable)
    {
        if (!WasStarted || pid <= 0 || birth == default || executable is null
            || actualPid != pid || actualBirth != birth || !IsMatchingExecutableIdentity(executable, actualExecutable))
            throw new InvalidOperationException("Owned PID, birth or executable identity changed; process preserved.");
    }

    internal static string ReadExecutableIdentity(Process process, Func<Process, string?>? readModule = null)
        => OperatingSystem.IsLinux()
            ? ReadLinuxExecutableIdentity(process.Id, path => new FileInfo(path).LinkTarget)
            : RequireAbsoluteExecutable((readModule ?? (child => child.MainModule?.FileName))(process));

    internal static string ReadLinuxExecutableIdentity(int processId, Func<string, string?> readLink)
    {
        // /proc may be mounted from another PID namespace. Never resolve an owned PID there.
        if (processId <= 0
            || !int.TryParse(readLink("/proc/self"), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var observedSelf)
            || observedSelf != Environment.ProcessId)
            throw new InvalidOperationException("Executable PID namespace identity unavailable.");
        return RequireAbsoluteExecutable(readLink("/proc/" + processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/exe"));
    }

    private static string RequireAbsoluteExecutable(string? image)
        => !string.IsNullOrWhiteSpace(image) && Path.IsPathFullyQualified(image)
            ? image : throw new InvalidOperationException("Executable identity unavailable.");

    internal static bool IsMatchingExecutableIdentity(string? captured, string? observed)
        => !string.IsNullOrWhiteSpace(captured) && Path.IsPathFullyQualified(captured)
            && StringComparer.Ordinal.Equals(captured, observed);

    private async Task StopExactAsync()
    {
        await stopping.WaitAsync();
        try
        {
            if (Process.HasExited) return;
            if (!OwnedProcessMetricObservation.TryRead(Process,
                handle ?? throw new InvalidOperationException("Owned member child handle unavailable."), pid,
                () => VerifyOwnership(Process))) return;
            if (OperatingSystem.IsLinux())
            {
                if (SendSignal(pid, 15) != 0 && !Process.HasExited)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Owned member child graceful signal failed.");
            }
            else
            {
                // A console child may have no main window; the exact-handle termination remains bounded below.
                Process.CloseMainWindow();
            }

            if (!Process.WaitForExit(250))
            {
                if (!OwnedProcessMetricObservation.TryRead(Process,
                    handle ?? throw new InvalidOperationException("Owned member child handle unavailable."), pid,
                    () => VerifyOwnership(Process))) return;
                Process.Kill();
            }

            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (!Process.HasExited) throw new InvalidOperationException("Exact owned member child remains.");
        }
        finally { stopping.Release(); }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int processId, int signal);

    public async ValueTask DisposeAsync()
    {
        if (IsClosed) return;
        try
        {
            Exception? stopFailure = null;
            try { if (WasStarted) await StopExactAsync(); }
            catch (Exception exception) { stopFailure = exception; }
            var terminal = !WasStarted;
            try { if (WasStarted) terminal = Process.HasExited; }
            catch (Exception exception)
            {
                stopFailure = stopFailure is null ? exception : new AggregateException(stopFailure, exception);
            }
            var actions = new List<Func<Task>>
            {
                () => { readerStop.Cancel(); return Task.CompletedTask; },
                () => Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)),
            };
            // Cancel only our readers on an unverified live child; keep its handles and finite lease in custody.
            if (terminal)
            {
                actions.Add(() => { leaseStop.Cancel(); return Task.CompletedTask; });
            }
            if (WasStarted && terminal)
            {
                actions.Add(() => { Process.StandardOutput.Dispose(); return Task.CompletedTask; });
                actions.Add(() => { Process.StandardError.Dispose(); return Task.CompletedTask; });
                if (Process.StartInfo.RedirectStandardInput)
                    actions.Add(() => { Process.StandardInput.Dispose(); return Task.CompletedTask; });
            }
            if (terminal) actions.Add(() => lease.WaitAsync(TimeSpan.FromSeconds(5)));
            Exception? settlementFailure = null;
            try { await MemberAuthorityCleanup.CompleteAsync(stopFailure, actions); }
            catch (Exception exception) { settlementFailure = exception; }
            if (!terminal || !ReadersSettled || !lease.IsCompleted)
            {
                if (settlementFailure is not null) ExceptionDispatchInfo.Capture(settlementFailure).Throw();
                throw new InvalidOperationException("Owned member child tasks remain; custody retained.");
            }

            Exception? releaseFailure = null;
            try
            {
                await MemberAuthorityCleanup.CompleteAsync(null,
                [
                    () => { Process.Dispose(); return Task.CompletedTask; },
                    () => { readerStop.Dispose(); return Task.CompletedTask; },
                    () => { leaseStop.Dispose(); return Task.CompletedTask; },
                    () => { stopping.Dispose(); return Task.CompletedTask; },
                ]);
            }
            catch (Exception exception) { releaseFailure = exception; }
            if (releaseFailure is null)
            {
                IsClosed = true;
                RetainedCustody.TryRemove(CreationToken, out _);
            }
            await MemberAuthorityCleanup.CompleteAsync(settlementFailure,
                releaseFailure is null ? [] : [() => Task.FromException(releaseFailure!)]);
        }
        catch
        {
            if (!IsClosed) RetainedCustody[CreationToken] = this;
            throw;
        }
    }
}
