using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class BillingSystemChildIdentityTests
{
    [LinuxFact]
    public Task EmptyCachedModules_PreservesTheRealOwnedExecutableIdentity() => WithOwnedChildAsync((child, process) =>
    {
        var modules = RequiredField(typeof(Process), "_modules");
        var original = modules.GetValue(process);
        try
        {
            modules.SetValue(process, new ProcessModuleCollection([]));
            Assert.Null(process.MainModule);
            child.CaptureIdentity();
            Assert.Null(process.MainModule);
            var identity = JsonSerializer.SerializeToElement(child.Receipt);
            Assert.Equal(process.Id, identity.GetProperty("pid").GetInt32());
            Assert.Equal(process.StartTime.ToUniversalTime(), identity.GetProperty("birth").GetDateTime());
            Assert.Equal(new FileInfo($"/proc/{process.Id}/exe").LinkTarget,
                identity.GetProperty("executable").GetString());
        }
        finally { modules.SetValue(process, original); }
        return Task.CompletedTask;
    });

    [LinuxTheory]
    [InlineData("pid")]
    [InlineData("birth")]
    [InlineData("executable")]
    public Task ChangedCapturedIdentity_RefusesBothMutationPaths(string member) => WithOwnedChildAsync((child, process) =>
    {
        var field = RequiredField(typeof(BillingSystemChild), member);
        var original = field.GetValue(child);
        object changed = member switch
        {
            "pid" => checked(process.Id + 1),
            "birth" => ((DateTime)original!).AddTicks(1),
            "executable" => (string)original! + ".different-image",
            _ => throw new InvalidOperationException("Unknown identity control."),
        };
        try
        {
            field.SetValue(child, changed);
            Assert.Throws<InvalidOperationException>(child.RequestGracefulStop);
            Assert.Throws<InvalidOperationException>(child.KillExact);
            Assert.False(process.HasExited);
        }
        finally { field.SetValue(child, original); }
        return Task.CompletedTask;
    });

    [LinuxFact]
    public Task ExitedOriginal_RefusesIdentityAcquisition() => WithOwnedChildAsync(async (child, process) =>
    {
        var identity = JsonSerializer.SerializeToElement(child.Receipt);
        child.KillExact();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(process.HasExited);
        Assert.Throws<InvalidOperationException>(child.CaptureIdentity);
        var after = JsonSerializer.SerializeToElement(child.Receipt);
        foreach (var member in new[] { "pid", "birth", "executable" })
            Assert.Equal(identity.GetProperty(member).GetRawText(), after.GetProperty(member).GetRawText());
    });

    private static async Task WithOwnedChildAsync(Func<BillingSystemChild, Process, Task> assertion)
    {
        var owner = new BillingProofLifetime(TimeSpan.FromSeconds(20));
        var start = new ProcessStartInfo("/bin/sleep")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("30");
        var child = new BillingSystemChild(start);
        BillingChildLease? lease = null;
        SafeHandle? handle = null;
        var dependencyReleased = false;
        try
        {
            lease = owner.StartChild(child);
            var process = Assert.IsType<Process>(RequiredField(typeof(BillingSystemChild), "process").GetValue(child));
            var ownedHandle = process.SafeHandle;
            handle = ownedHandle;
            owner.OwnDependency(() =>
            {
                Assert.True(ownedHandle.IsClosed);
                dependencyReleased = true;
                return ValueTask.CompletedTask;
            });
            await assertion(child, process);
        }
        finally { await owner.DisposeAsync(); }
        Assert.False(owner.IsRetained);
        Assert.True(dependencyReleased);
        Assert.NotNull(handle);
        Assert.True(handle.IsClosed);
        Assert.NotNull(lease);
        var receipt = JsonSerializer.SerializeToElement(lease.Receipt);
        Assert.True(receipt.GetProperty("exitVerified").GetBoolean());
        Assert.True(receipt.GetProperty("readersClosed").GetBoolean());
        Assert.True(receipt.GetProperty("released").GetBoolean());
        Assert.True(receipt.GetProperty("process").GetProperty("stdoutClosed").GetBoolean());
        Assert.True(receipt.GetProperty("process").GetProperty("stderrClosed").GetBoolean());
    }

    private static FieldInfo RequiredField(Type type, string name) =>
        Assert.IsAssignableFrom<FieldInfo>(type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic));

    private sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            if (!OperatingSystem.IsLinux()) Skip = "Linux /proc executable identity is required.";
        }
    }

    private sealed class LinuxTheoryAttribute : TheoryAttribute
    {
        public LinuxTheoryAttribute()
        {
            if (!OperatingSystem.IsLinux()) Skip = "Linux /proc executable identity is required.";
        }
    }
}
