using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

internal enum AntiforgeryPageResponseLabel { AntiforgeryPage }
internal enum AntiforgeryPageResponsePhase { ClientStarted, ServerAdmitted, HeadersStarting, NextReturned, NextFaulted, ResponseCompleted, RequestAborted, ClientReturned, ClientFailed }

internal sealed class AntiforgeryPageResponseEvidence
{
    internal const string DataKey = "AntiforgeryPageResponseEvidence";
    private readonly object gate = new();
    private readonly AntiforgeryPageResponseLabel label;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly List<Observation> records = [];
    private CancellationTokenRegistration? abortRegistration;
    private bool overflow;
    private bool closed;

    internal AntiforgeryPageResponseEvidence(AntiforgeryPageResponseLabel label)
    {
        if (!Enum.IsDefined(label)) throw new ArgumentOutOfRangeException(nameof(label));
        this.label = label;
    }

    internal void Record(AntiforgeryPageResponsePhase phase, int? status = null, long? knownContentLength = null)
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
                Schema = "antiforgery-page-response-failure/v1",
                Label = label.ToString(),
                Frequency = Stopwatch.Frequency,
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
        IServiceProvider services, HttpClient client, string path, AntiforgeryPageResponseLabel label, Action<string> output)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(services, client, request, label, output);
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        IServiceProvider services, HttpClient client, HttpRequestMessage request, AntiforgeryPageResponseLabel label, Action<string> output)
    {
        var registry = services.GetRequiredService<AntiforgeryPageResponseProbeRegistry>();
        using var lease = registry.Begin(label);
        request.Headers.Add(AntiforgeryPageResponseProbeRegistry.Header, lease.Id.ToString("N"));
        lease.Probe.Record(AntiforgeryPageResponsePhase.ClientStarted);
        try
        {
            // Preserve the existing buffered read and the client's unchanged default timeout.
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            lease.Probe.Record(AntiforgeryPageResponsePhase.ClientReturned, (int)response.StatusCode);
            return response;
        }
        catch (Exception original)
        {
            lease.Probe.Record(AntiforgeryPageResponsePhase.ClientFailed);
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
            request.Headers.Remove(AntiforgeryPageResponseProbeRegistry.Header);
        }
    }

    private sealed record Observation(string Phase, long ElapsedTicks, int? Status, long? KnownContentLength);
}

internal sealed class AntiforgeryPageResponseProbeRegistry
{
    internal const string Header = "X-Maliev-Test-Antiforgery-Page-Probe";
    private readonly object gate = new();
    private readonly Dictionary<Guid, AntiforgeryPageResponseEvidence> active = [];

    internal int ActiveCount { get { lock (gate) return active.Count; } }

    internal Lease Begin(AntiforgeryPageResponseLabel label)
    {
        lock (gate)
        {
            if (active.Count == 16) throw new InvalidOperationException("The test response probe registry is full.");
            var id = Guid.NewGuid();
            var probe = new AntiforgeryPageResponseEvidence(label);
            active.Add(id, probe);
            return new(this, id, probe);
        }
    }

    internal bool TryFind(Guid id, out AntiforgeryPageResponseEvidence? probe)
    {
        lock (gate) return active.TryGetValue(id, out probe);
    }

    internal sealed class Lease(AntiforgeryPageResponseProbeRegistry owner, Guid id, AntiforgeryPageResponseEvidence probe) : IDisposable
    {
        internal Guid Id { get; } = id;
        internal AntiforgeryPageResponseEvidence Probe { get; } = probe;

        public void Dispose()
        {
            lock (owner.gate) owner.active.Remove(Id);
            Probe.Close();
        }
    }
}

internal sealed class AntiforgeryPageResponseProbeStartupFilter(AntiforgeryPageResponseProbeRegistry registry) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(InvokeAsync);
        next(app);
    };

    private static void Observe(AntiforgeryPageResponseEvidence probe, AntiforgeryPageResponsePhase phase, HttpContext context)
    {
        try { probe.Record(phase, context.Response.StatusCode, context.Response.ContentLength); }
        catch { /* Optional metadata observation must not change the existing pipeline result. */ }
    }

    internal async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var ids = context.Request.Headers[AntiforgeryPageResponseProbeRegistry.Header];
        if (ids.Count != 1 || !Guid.TryParseExact(ids[0], "N", out var id)
            || !registry.TryFind(id, out var probe) || probe is null)
        {
            await next(context);
            return;
        }

        context.Request.Headers.Remove(AntiforgeryPageResponseProbeRegistry.Header);
        Observe(probe, AntiforgeryPageResponsePhase.ServerAdmitted, context);
        probe.Own(context.RequestAborted.Register(() => probe.Record(AntiforgeryPageResponsePhase.RequestAborted)));
        context.Response.OnStarting(() =>
        {
            Observe(probe, AntiforgeryPageResponsePhase.HeadersStarting, context);
            return Task.CompletedTask;
        });
        context.Response.OnCompleted(() =>
        {
            Observe(probe, AntiforgeryPageResponsePhase.ResponseCompleted, context);
            return Task.CompletedTask;
        });
        try
        {
            await next(context);
            Observe(probe, AntiforgeryPageResponsePhase.NextReturned, context);
        }
        catch
        {
            Observe(probe, AntiforgeryPageResponsePhase.NextFaulted, context);
            throw;
        }
    }
}
