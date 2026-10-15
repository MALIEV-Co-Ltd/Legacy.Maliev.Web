using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncOwnedResourceScopeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("docker-acquisition")]
    [InlineData("unknown-identity")]
    [InlineData("stop-refusal")]
    [InlineData("reader-settlement")]
    public async Task CleanupRefusal_RetainsExactCustodyAndOriginalFailure(string mode)
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll));
        IReadOnlyList<object> receipts = [];
        var emitted = new List<string>();
        CncOwnedResourceScope.OwnedChild? recovery = null;
        (int Pid, DateTime Start)? retainedKey = null;
        var failure = await Record.ExceptionAsync(() => CncOwnedResourceScope.ExecuteAsync(async scope =>
        {
            var child = scope.StartChild(dll!, new() { ["MALIEV_CNC_PROBE_MODE"] = "partial-start" });
            recovery = child;
            if (mode == "docker-acquisition")
            {
                scope.AcquisitionRejectionControl = true;
                scope.DockerFactory = () => throw new InvalidOperationException("controlled-acquisition-refusal");
            }
            else
            {
                child.RejectIdentity = mode == "unknown-identity";
                child.RejectStop = mode == "stop-refusal";
                if (mode == "reader-settlement") child.ReaderRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await Task.CompletedTask;
            throw new InvalidOperationException("original-body-failure");
        }, rows => receipts = rows, receipt =>
        {
            emitted.Add(receipt);
            output.WriteLine(receipt);
        }));
        try
        {
            using (var captured = JsonDocument.Parse(Assert.Single(emitted)))
            {
                Assert.Equal(nameof(InvalidOperationException), captured.RootElement.GetProperty("primaryFailure").GetString());
                Assert.True(captured.RootElement.GetProperty("cleanupFailures").GetArrayLength() > 0);
                Assert.True(captured.RootElement.GetProperty("resources").GetArrayLength() > 0);
            }
            Assert.DoesNotContain("original-body-failure", emitted[0], StringComparison.Ordinal);
            if (mode != "docker-acquisition")
            {
                Assert.NotNull(recovery);
                retainedKey = (recovery.Process.Id, recovery.Process.StartTime.ToUniversalTime());
                var key = retainedKey.Value;
                Assert.True(CncOwnedResourceScope.RetainedCustody.TryGetValue(key, out var retained));
                Assert.Same(recovery, retained);
                using var refusal = JsonDocument.Parse(JsonSerializer.Serialize(receipts[^1]));
                Assert.Equal("unresolved-process", refusal.RootElement.GetProperty("kind").GetString());
                Assert.True(refusal.RootElement.GetProperty("handleRetained").GetBoolean());
                Assert.False(refusal.RootElement.GetProperty("closed").GetBoolean());
                if (mode == "reader-settlement") Assert.False(refusal.RootElement.GetProperty("readersSettled").GetBoolean());
                else Assert.False(refusal.RootElement.GetProperty("terminal").GetBoolean());
            }
        }
        finally
        {
            if (mode != "docker-acquisition" && recovery is not null)
            {
                // Clear only injected controls even when receipt capture assertions fail.
                recovery.RejectIdentity = false;
                recovery.RejectStop = false;
                recovery.ReaderRelease?.TrySetResult(true);
                await recovery.CloseAsync();
            }
        }
        if (mode != "docker-acquisition")
        {
            Assert.NotNull(recovery);
            Assert.True(retainedKey.HasValue);
            var key = retainedKey ?? throw new InvalidOperationException("Exact owned identity was not captured before recovery.");
            Assert.False(CncOwnedResourceScope.RetainedCustody.ContainsKey(key));
            Console.WriteLine(JsonSerializer.Serialize(receipts));
        }
        var original = Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal("original-body-failure", original.Message);
        using var final = JsonDocument.Parse(JsonSerializer.Serialize(receipts));
        Assert.Contains(final.RootElement.EnumerateArray(), row => row.GetProperty("kind").GetString() == "process"
            && row.GetProperty("terminal").GetBoolean() && row.GetProperty("readersSettled").GetBoolean());
        if (mode == "docker-acquisition")
            Assert.Contains(final.RootElement.EnumerateArray(), row => row.GetProperty("kind").GetString() == "cleanup-fault"
                && row.GetProperty("phase").GetString() == "docker-acquisition");
    }
    [Theory]
    [InlineData("partial-start")]
    [InlineData("overflow")]
    [InlineData("late-writer")]
    public async Task ChildFailure_PreservesPrimaryAndSettlesOwnedResource(string mode)
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll), "Build the bounded resource probe before new helper controls.");
        IReadOnlyList<object> receipts = [];
        var emitted = new List<string>();
        var failure = await Record.ExceptionAsync(() => CncOwnedResourceScope.ExecuteAsync(async scope =>
        {
            var child = scope.StartChild(dll!, new() { ["MALIEV_CNC_PROBE_MODE"] = mode },
                mode == "partial-start" ? () => throw new InvalidOperationException("partial-start-primary") : null);
            await child.CompleteAsync(TimeSpan.FromSeconds(mode == "late-writer" ? 1 : 10), CancellationToken.None);
        }, rows => receipts = rows, receipt =>
        {
            emitted.Add(receipt);
            output.WriteLine(receipt);
            if (mode == "partial-start") throw new InvalidOperationException("controlled-private-sink-failure");
        }));
        Assert.NotNull(failure);
        using (var captured = JsonDocument.Parse(Assert.Single(emitted)))
            Assert.Equal(failure.GetType().Name, captured.RootElement.GetProperty("primaryFailure").GetString());
        Assert.DoesNotContain("controlled-private-sink-failure", emitted[0], StringComparison.Ordinal);
        if (mode == "partial-start") Assert.Equal("partial-start-primary", failure.Message);
        else if (mode == "overflow") Assert.IsType<InvalidDataException>(failure);
        else Assert.IsType<TimeoutException>(failure);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(receipts)));
        Assert.Equal("process", json.RootElement.GetProperty("kind").GetString());
        Assert.True(json.RootElement.GetProperty("terminal").GetBoolean());
        Assert.True(json.RootElement.GetProperty("readersSettled").GetBoolean());
        Assert.True(json.RootElement.GetProperty("pid").GetInt32() > 0);
        if (mode == "overflow") Assert.Equal("output-quota", json.RootElement.GetProperty("budgetFailure").GetString());
        if (mode == "late-writer") Assert.Equal("phase-timeout", json.RootElement.GetProperty("budgetFailure").GetString());
    }

    [Fact]
    public async Task MetricObservation_NaturalExitDuringReadSettlesExactOwnedChild()
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll));
        await CncOwnedResourceScope.ExecuteAsync(async scope =>
        {
            var child = scope.StartChild(dll!, new() { ["MALIEV_CNC_PROBE_MODE"] = "natural-exit" },
                redirectStandardInput: true);
            var handle = child.Process.SafeHandle;
            var pid = child.Process.Id;
            Assert.False(child.Process.HasExited);
            child.Process.StandardInput.WriteLine("release-owned-natural-exit");
            Assert.True(child.Process.WaitForExit(10_000));
            Action readMetrics = () =>
            {
                child.Process.Refresh();
                _ = child.Process.WorkingSet64;
                _ = child.Process.TotalProcessorTime;
            };
            Assert.IsType<InvalidOperationException>(Record.Exception(readMetrics));
            var observed = OwnedProcessMetricObservation.TryRead(child.Process, handle, pid, readMetrics);
            Assert.False(observed, "The same real metrics read that faults without the terminal guard must settle the exact owned child.");
            await child.CompleteAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }, rows => output.WriteLine(JsonSerializer.Serialize(rows)));
    }

    [Fact]
    public async Task MetricObservation_LiveProcessErrorPreservesOriginalException()
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll));
        await CncOwnedResourceScope.ExecuteAsync(async scope =>
        {
            var child = scope.StartChild(dll!, new() { ["MALIEV_CNC_PROBE_MODE"] = "partial-start" });
            var original = new InvalidOperationException("controlled-live-metric-error");
            Assert.Same(original, Record.Exception(() => OwnedProcessMetricObservation.TryRead(
                child.Process, child.Process.SafeHandle, child.Process.Id, () => throw original)));
            await Task.CompletedTask;
        }, rows => output.WriteLine(JsonSerializer.Serialize(rows)));
    }

    [Theory]
    [InlineData("pid-mismatch")]
    [InlineData("invalid-handle")]
    [InlineData("closed-handle")]
    [InlineData("disposed-process")]
    public async Task MetricObservation_UnverifiableTerminalPreservesOriginalException(string mode)
    {
        var dll = Environment.GetEnvironmentVariable("MALIEV_CNC_LEASE_PROBE_DLL");
        Assert.True(File.Exists(dll));
        await CncOwnedResourceScope.ExecuteAsync(async scope =>
        {
            var child = scope.StartChild(dll!, new() { ["MALIEV_CNC_PROBE_MODE"] = "natural-exit" },
                redirectStandardInput: true);
            var pid = child.Process.Id;
            var handle = child.Process.SafeHandle;
            child.Process.StandardInput.WriteLine("release-owned-natural-exit");
            await child.CompleteAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            using var invalid = new Microsoft.Win32.SafeHandles.SafeProcessHandle(IntPtr.Zero, ownsHandle: false);
            using var disposed = new System.Diagnostics.Process();
            if (mode == "closed-handle") invalid.Dispose();
            if (mode == "disposed-process") disposed.Dispose();
            var original = new InvalidOperationException("controlled-unverifiable-metric-error");
            Assert.Same(original, Record.Exception(() => OwnedProcessMetricObservation.TryRead(
                mode == "disposed-process" ? disposed : child.Process,
                mode is "invalid-handle" or "closed-handle" ? invalid : handle,
                mode == "pid-mismatch" ? -1 : pid, () => throw original)));
        }, rows => output.WriteLine(JsonSerializer.Serialize(rows)));
    }
}
