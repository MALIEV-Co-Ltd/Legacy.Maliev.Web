using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Http;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Observes one assembly in this test host; it starts no backend or child process.</summary>
public sealed class RuntimeAssemblyProbe
{
    [Fact]
    public Task ObserveHttpAssemblyWithoutBackend() => RuntimeAssemblyObserver.ObserveCore(typeof(RuntimeAssemblyProbe).Assembly);
}

/// <summary>Captures the shared bounded assembly identity for both test hosts.</summary>
public static class RuntimeAssemblyObserver
{
    /// <summary>Capture bounded identity only when an owned diagnostic path is supplied.</summary>
    public static async Task ObserveCore(Assembly testAssembly)
    {
        var assembly = typeof(HttpMessageHandlerBuilder).Assembly;
        Assert.False(assembly.IsDynamic);
        Assert.Equal("Microsoft.Extensions.Http", assembly.GetName().Name);
        var output = Environment.GetEnvironmentVariable("MALIEV_RUNTIME_PROBE_OUTPUT");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }
        Assert.True(OperatingSystem.IsLinux());
        Assert.True(Path.IsPathFullyQualified(output));
        var file = new FileInfo(assembly.Location);
        Assert.Null(file.LinkTarget);
        Assert.InRange(file.Length, 1L, 16L * 1024 * 1024);
        var beforeLength = file.Length;
        var beforeModified = file.LastWriteTimeUtc;
        var bytes = new byte[checked((int)beforeLength)];
        await using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await stream.ReadExactlyAsync(bytes);
            Assert.Equal(-1, stream.ReadByte());
        }
        file.Refresh();
        Assert.Equal(beforeLength, file.Length);
        Assert.Equal(beforeModified, file.LastWriteTimeUtc);
        Assert.Equal(beforeLength, bytes.LongLength);
        var stat = await File.ReadAllTextAsync("/proc/self/stat");
        var close = stat.LastIndexOf(") ", StringComparison.Ordinal);
        Assert.True(close > 0);
        var fields = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(fields.Length > 19);
        Assert.True(ulong.TryParse(fields[19], out var ticks));
        Assert.True(ticks > 0);
        var boot = (await File.ReadAllTextAsync("/proc/sys/kernel/random/boot_id")).Trim();
        Assert.True(Guid.TryParseExact(boot, "D", out var bootId));
        Assert.NotEqual(Guid.Empty, bootId);
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.InRange(version?.Length ?? 0, 1, 128);
        var receipt = new
        {
            pid = Environment.ProcessId,
            startTicks = ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bootId = boot,
            executable = Environment.ProcessPath,
            parentId = fields[1],
            assemblyName = assembly.GetName().Name,
            assemblyVersion = assembly.GetName().Version?.ToString(),
            informationalVersion = version,
            mvid = assembly.ManifestModule.ModuleVersionId.ToString("D"),
            assemblyPath = file.FullName,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            testAssembly = await DescribeTestAssembly(testAssembly),
            observerAssembly = await DescribeTestAssembly(typeof(RuntimeAssemblyProbe).Assembly),
            backendAllocated = false,
        };
        var pending = output + ".pending";
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, receipt);
        }
        File.Move(pending, output, overwrite: false);
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(output + ".ack") && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(50);
        }
        Assert.True(File.Exists(output + ".ack"), "Owned process identity was not acknowledged in time.");
    }

    private static async Task<object> DescribeTestAssembly(Assembly assembly)
    {
        Assert.False(assembly.IsDynamic);
        var file = new FileInfo(assembly.Location);
        Assert.Null(file.LinkTarget);
        Assert.InRange(file.Length, 1L, 16L * 1024 * 1024);
        var length = file.Length;
        var modified = file.LastWriteTimeUtc;
        var raw = new byte[checked((int)length)];
        await using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await stream.ReadExactlyAsync(raw);
            Assert.Equal(-1, stream.ReadByte());
        }
        file.Refresh();
        Assert.Equal(length, file.Length);
        Assert.Equal(modified, file.LastWriteTimeUtc);
        return new
        {
            name = assembly.GetName().Name,
            path = file.FullName,
            mvid = assembly.ManifestModule.ModuleVersionId.ToString("D"),
            sha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
        };
    }
}
