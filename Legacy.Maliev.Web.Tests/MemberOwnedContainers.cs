using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Local-daemon, exact-ID disposable member fixture container ownership; no global Docker mutations.</summary>
internal sealed class MemberOwnedContainers(string run) : IDisposable
{
    internal const string Endpoint = "unix:///var/run/docker.sock";
    private DockerClient? client;
    private string? daemonId;
    private readonly DateTimeOffset creationWindowStart = DateTimeOffset.UtcNow;
    private readonly Dictionary<string, Birth> births = [];
    private readonly HashSet<string> released = [];
    private sealed record Birth(string Id, string Image, string Created, string Mounts, int Port);

    internal static void Cap(CreateContainerParameters value, long memory, string tmpfsPath, long tmpfsBytes)
    {
        value.HostConfig ??= new HostConfig();
        value.HostConfig.Memory = memory;
        value.HostConfig.MemorySwap = memory;
        value.HostConfig.NanoCPUs = 1_000_000_000;
        value.HostConfig.PidsLimit = 128;
        value.HostConfig.Tmpfs = new Dictionary<string, string> { [tmpfsPath] = $"rw,nosuid,nodev,size={tmpfsBytes}" };
        if (value.HostConfig.PortBindings is not null)
            foreach (var bindings in value.HostConfig.PortBindings.Values)
                foreach (var binding in bindings) binding.HostIP = "127.0.0.1";
    }

    internal async Task PreflightAsync()
    {
        NativeMemberAuthorityProcess.Preflight();
        var available = File.ReadLines("/proc/meminfo").Single(line => line.StartsWith("MemAvailable:", StringComparison.Ordinal));
        var fields = available.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kib) || kib < 4096L * 1024)
            throw new InvalidOperationException("Member authority allocation requires at least 4096 MiB available memory.");
        client = new DockerClientBuilder().WithEndpoint(new Uri(Endpoint)).Build();
        await VerifyDaemonAsync();
    }

    private async Task VerifyDaemonAsync()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var info = JsonSerializer.SerializeToElement(await Client.System.GetSystemInfoAsync(budget.Token));
        var id = String(info, "ID");
        if (string.IsNullOrWhiteSpace(id) || String(info, "OSType") != "linux" || daemonId is not null && daemonId != id)
            throw new InvalidOperationException("Member local Linux Docker daemon identity is unavailable or changed.");
        daemonId ??= id;
    }

    private DockerClient Client => client ?? throw new InvalidOperationException("Member Docker preflight has not completed.");

    internal async Task CaptureAsync(string id, string expectedImage, int internalPort)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Member container creation identity is unbound; preserve unknown allocation.");
        await VerifyDaemonAsync();
        var observed = await InspectAsync(id) ?? throw new InvalidOperationException("Expected member container is absent before capture.");
        var running = Field(Field(observed, "State"), "Running").GetBoolean();
        Validate(observed, id, expectedImage, internalPort, requireBinding: running);
        var created = String(observed, "Created");
        if (!DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var birth)
            || birth < creationWindowStart.AddSeconds(-2) || birth > DateTimeOffset.UtcNow.AddSeconds(2))
            throw new InvalidOperationException("Member container birth is outside this fixture creation window.");
        var record = new Birth(id, String(observed, "Image"), created, Field(observed, "Mounts").GetRawText(), running ? MappedPort(observed, internalPort) : 0);
        if (births.TryGetValue(id, out var previous) && previous != record)
            throw new InvalidOperationException("Member immutable container birth binding changed.");
        births[id] = record;
        Console.WriteLine("[member-container-birth] " + JsonSerializer.Serialize(new
        {
            id,
            run,
            image = record.Image,
            created = record.Created,
            port = record.Port,
            mounts = "tmpfs-only",
            persistentData = false,
            independentLifetimeSeconds = 185,
        }));
    }

    internal async Task ReleaseAsync(string id, string expectedImage, int internalPort)
    {
        if (released.Contains(id)) return;
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Member container identity is unbound; preserve unknown allocation.");
        await VerifyDaemonAsync();
        var observed = await InspectAsync(id);
        // A confirmed exact-ID 404 is absence evidence, not authority to mutate an unknown resource.
        if (observed is null) { released.Add(id); return; }
        if (!births.ContainsKey(id)) await CaptureAsync(id, expectedImage, internalPort);
        var current = observed.Value;
        var running = Field(Field(current, "State"), "Running").GetBoolean();
        Validate(current, id, expectedImage, internalPort, requireBinding: running);
        var birth = births[id];
        if (String(current, "Image") != birth.Image || String(current, "Created") != birth.Created
            || Field(current, "Mounts").GetRawText() != birth.Mounts || running && MappedPort(current, internalPort) != birth.Port)
            throw new InvalidOperationException("Member exact container identity/mount/port binding changed; preserve resource.");
        EnsureNoLocalClients(birth.Port);
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            await Client.Containers.StopContainerAsync(id, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }, budget.Token);
        observed = await InspectAsync(id);
        if (observed is not null)
        {
            Validate(observed.Value, id, expectedImage, internalPort, requireBinding: false);
            if (Field(Field(observed.Value, "State"), "Running").GetBoolean())
                throw new InvalidOperationException("Member container graceful stop did not establish quiescence.");
            EnsureNoLocalClients(birth.Port);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Client.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = false, RemoveVolumes = false }, budget.Token);
        }
        if (await InspectAsync(id) is not null)
            throw new InvalidOperationException("Member exact container removal was not verified.");
        released.Add(id);
        Console.WriteLine("[member-container-released] " + JsonSerializer.Serialize(new { id, run, verifiedAbsent = true }));
    }

    private async Task<JsonElement?> InspectAsync(string id)
    {
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return JsonSerializer.SerializeToElement(await Client.Containers.InspectContainerAsync(id, budget.Token));
        }
        catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    internal static void ValidateDisposableIdentity(JsonElement observed, string id, string expectedImage, string expectedRun)
    {
        var config = Field(observed, "Config");
        var labels = Field(config, "Labels");
        if (String(observed, "ID") != id || String(config, "Image") != expectedImage
            || String(labels, "maliev.task") != "web-476-member-authority"
            || String(labels, "maliev.run") != expectedRun || String(labels, "maliev.disposable") != "true")
            throw new InvalidOperationException("Member exact container ownership/image labels mismatch.");
        foreach (var mount in Field(observed, "Mounts").EnumerateArray())
            if (String(mount, "Type") != "tmpfs")
                throw new InvalidOperationException("Member container has a non-disposable mount; preserve it.");
    }

    private void Validate(JsonElement observed, string id, string expectedImage, int internalPort, bool requireBinding = true)
    {
        ValidateDisposableIdentity(observed, id, expectedImage, run);
        if (requireBinding) _ = MappedPort(observed, internalPort);
    }

    private static int MappedPort(JsonElement observed, int internalPort)
    {
        var mappings = Field(Field(Field(observed, "NetworkSettings"), "Ports"), internalPort.ToString(CultureInfo.InvariantCulture) + "/tcp").EnumerateArray().ToArray();
        if (mappings.Length != 1 || String(mappings[0], "HostIP") != "127.0.0.1"
            || !int.TryParse(String(mappings[0], "HostPort"), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port <= 0)
            throw new InvalidOperationException("Member container port is not exclusively bound to loopback.");
        return port;
    }

    private static void EnsureNoLocalClients(int port)
    {
        if (port == 0) return; // A captured stopped allocation has no running endpoint; hosts/children were already quiesced.
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(connection =>
            connection.State == TcpState.Established && connection.RemoteEndPoint.Port == port
            && IPAddress.IsLoopback(connection.RemoteEndPoint.Address)))
            throw new InvalidOperationException("Member container still has active local clients; preserve it.");
    }

    private static JsonElement Field(JsonElement element, string name) => element.EnumerateObject()
        .Single(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    private static string String(JsonElement element, string name) => Field(element, name).ToString();

    public void Dispose() => client?.Dispose();
}
