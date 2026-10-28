using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

internal enum TestResponseRequestLabel { BaseQuotation, LegacyQuotationRedirect, UnknownQuotationPath, CompatibilityNotice }
internal enum TestResponsePhase { ClientStarted, ServerAdmitted, HeadersStarting, NextReturned, NextFaulted, ResponseCompleted, RequestAborted, ClientReturned, ClientFailed }

internal sealed class TestResponseFailureEvidence
{
    internal const string DataKey = "TestResponseFailureEvidence";
    private readonly object gate = new();
    private readonly TestResponseRequestLabel label;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset clockOriginUtc = DateTimeOffset.UtcNow;
    private readonly List<Observation> records = [];
    private CancellationTokenRegistration? abortRegistration;
    private bool overflow;
    private bool closed;

    internal TestResponseFailureEvidence(TestResponseRequestLabel label)
    {
        if (!Enum.IsDefined(label)) throw new ArgumentOutOfRangeException(nameof(label));
        this.label = label;
    }

    internal void Record(TestResponsePhase phase, int? status = null, long? knownContentLength = null)
    {
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        lock (gate)
        {
            if (closed) return;
            if (records.Count == 16) { overflow = true; return; }
            records.Add(new(phase.ToString(), Stopwatch.GetTimestamp() - started, status, knownContentLength));
        }
    }

    internal void Own(CancellationTokenRegistration registration)
    {
        lock (gate)
        {
            if (!closed)
            {
                abortRegistration = registration;
                return;
            }
        }
        registration.Dispose();
    }

    internal void Close()
    {
        CancellationTokenRegistration? registration;
        lock (gate)
        {
            closed = true;
            registration = abortRegistration;
            abortRegistration = null;
        }
        registration?.Dispose();
    }

    internal string Snapshot()
    {
        lock (gate)
        {
            return JsonSerializer.Serialize(new
            {
                Schema = "test-response-failure/v1",
                Label = label.ToString(),
                Frequency = Stopwatch.Frequency,
                ClockOriginUtc = clockOriginUtc,
                Records = records.ToArray(),
                Overflow = overflow,
                Closed = closed,
                ObservedBodyBytes = (long?)null,
                BodyByteObservation = "unavailable",
                CaptureBoundary = "client-settlement",
            });
        }
    }

    internal static async Task<HttpResponseMessage> GetAsync(
        IServiceProvider services, HttpClient client, string path, TestResponseRequestLabel label, Action<string> output)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(services, client, request, label, output);
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        IServiceProvider services, HttpClient client, HttpRequestMessage request, TestResponseRequestLabel label, Action<string> output)
    {
        var registry = services.GetRequiredService<TestResponseProbeRegistry>();
        using var lease = registry.Begin(label);
        request.Headers.Add(TestResponseProbeRegistry.Header, lease.Id.ToString("N"));
        lease.Probe.Record(TestResponsePhase.ClientStarted);
        try
        {
            // Preserve the existing buffered read and the client's unchanged default timeout.
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            lease.Probe.Record(TestResponsePhase.ClientReturned, (int)response.StatusCode);
            return response;
        }
        catch (Exception original)
        {
            lease.Probe.Record(TestResponsePhase.ClientFailed);
            lease.Probe.Close();
            try
            {
                var snapshot = lease.Probe.Snapshot();
                try { original.Data[DataKey] = snapshot; } catch { }
                output(snapshot);
            }
            catch { /* Evidence or output failure must not replace the original request failure. */ }
            throw;
        }
        finally
        {
            request.Headers.Remove(TestResponseProbeRegistry.Header);
        }
    }

    private sealed record Observation(string Phase, long ElapsedTicks, int? Status, long? KnownContentLength);
}

internal sealed class TestResponseProbeRegistry
{
    internal const string Header = "X-Maliev-Test-Response-Probe";
    private readonly object gate = new();
    private readonly Dictionary<Guid, TestResponseFailureEvidence> active = [];
    private readonly Func<long> timestamp = Stopwatch.GetTimestamp;
    private NavigationBinding? navigation;

    public TestResponseProbeRegistry() { }

    internal TestResponseProbeRegistry(Func<long> timestamp) =>
        this.timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));

    internal Lease? TryBeginNavigation(Uri origin, TestResponseRequestLabel label)
    {
        if (!origin.IsAbsoluteUri || !origin.IsLoopback || origin.UserInfo.Length != 0
            || origin.Scheme is not ("http" or "https") || origin.AbsolutePath != "/") return null;
        lock (gate)
        {
            if (navigation is not null || active.Count == 16) return null;
            var lease = Begin(label);
            navigation = new(lease, origin.Authority, timestamp());
            return lease;
        }
    }

    internal bool TryBindNavigation(HttpRequest request, out Guid id)
    {
        id = default;
        lock (gate)
        {
            if (navigation is not { Claimed: false } binding) return false;
            var age = Stopwatch.GetElapsedTime(binding.Started, timestamp());
            if (age < TimeSpan.Zero || age >= TimeSpan.FromSeconds(30)) return false;
            if (!HttpMethods.IsGet(request.Method)
                || !string.Equals(request.Path.Value, "/instantquotation/3d-printing", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(request.Host.Value, binding.Authority, StringComparison.OrdinalIgnoreCase)
                || request.Headers["Sec-Fetch-Dest"].ToString() != "document"
                || request.Headers["Sec-Fetch-Mode"].ToString() != "navigate"
                || !request.Headers.Accept.ToString().StartsWith("text/html", StringComparison.OrdinalIgnoreCase)) return false;
            binding.Claimed = true;
            id = binding.Lease.Id;
            return true;
        }
    }

    private sealed class NavigationBinding(Lease lease, string authority, long started)
    {
        internal Lease Lease { get; } = lease;
        internal string Authority { get; } = authority;
        internal long Started { get; } = started;
        internal bool Claimed { get; set; }
    }

    internal int ActiveCount { get { lock (gate) return active.Count; } }

    internal Lease Begin(TestResponseRequestLabel label)
    {
        lock (gate)
        {
            if (active.Count == 16) throw new InvalidOperationException("The test response probe registry is full.");
            var id = Guid.NewGuid();
            var probe = new TestResponseFailureEvidence(label);
            active.Add(id, probe);
            return new(this, id, probe);
        }
    }

    internal bool TryFind(Guid id, out TestResponseFailureEvidence? probe)
    {
        lock (gate) return active.TryGetValue(id, out probe);
    }

    internal sealed class Lease(TestResponseProbeRegistry owner, Guid id, TestResponseFailureEvidence probe) : IDisposable
    {
        internal Guid Id { get; } = id;
        internal TestResponseFailureEvidence Probe { get; } = probe;

        public void Dispose()
        {
            lock (owner.gate)
            {
                owner.active.Remove(Id);
                if (owner.navigation?.Lease.Id == Id) owner.navigation = null;
            }
            Probe.Close();
        }
    }
}

internal sealed class TestResponseProbeStartupFilter(TestResponseProbeRegistry registry) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(InvokeAsync);
        next(app);
    };

    private static void Observe(TestResponseFailureEvidence probe, TestResponsePhase phase, HttpContext context)
    {
        try { probe.Record(phase, context.Response.StatusCode, context.Response.ContentLength); }
        catch { /* Optional metadata observation must not change the existing pipeline result. */ }
    }

    internal async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var ids = context.Request.Headers[TestResponseProbeRegistry.Header];
        try
        {
            if (ids.Count == 0 && registry.TryBindNavigation(context.Request, out var navigationId))
            {
                // This test-only server request owns the nonce; never send it through the browser.
                context.Request.Headers[TestResponseProbeRegistry.Header] = navigationId.ToString("N");
                ids = context.Request.Headers[TestResponseProbeRegistry.Header];
            }
        }
        catch { /* Optional navigation correlation must not replace the existing pipeline result. */ }
        if (ids.Count != 1 || !Guid.TryParseExact(ids[0], "N", out var id)
            || !registry.TryFind(id, out var probe) || probe is null)
        {
            await next(context);
            return;
        }

        context.Request.Headers.Remove(TestResponseProbeRegistry.Header);
        Observe(probe, TestResponsePhase.ServerAdmitted, context);
        probe.Own(context.RequestAborted.Register(() => probe.Record(TestResponsePhase.RequestAborted)));
        context.Response.OnStarting(() =>
        {
            Observe(probe, TestResponsePhase.HeadersStarting, context);
            return Task.CompletedTask;
        });
        context.Response.OnCompleted(() =>
        {
            Observe(probe, TestResponsePhase.ResponseCompleted, context);
            return Task.CompletedTask;
        });
        try
        {
            await next(context);
            Observe(probe, TestResponsePhase.NextReturned, context);
        }
        catch
        {
            Observe(probe, TestResponsePhase.NextFaulted, context);
            throw;
        }
    }
}
