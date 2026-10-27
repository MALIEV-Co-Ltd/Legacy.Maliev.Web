using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Passive, fixture-owned first-document boundaries; never records request or customer values.</summary>
internal sealed class MaterialInitialRequestStages
{
    internal enum Boundary
    {
        Middleware, PageParameters, Countries, WorkflowInitialization, IdentityRead, IdentityWrite,
        StoreCreate, StoreGet, StorePut, StoreRemove, ResponseStarting, ResponseCompleted, RequestAborted,
    }

    private sealed record Row(string Stage, string Outcome, long ElapsedMilliseconds);
    private readonly object sync = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<Row> rows = [];
    private int omitted;
    private bool frozen;
    private bool claimed;
    private bool observerFaulted;

    internal void Record(Boundary boundary, string outcome)
    {
        try
        {
            lock (sync)
            {
                if (frozen) return;
                if (rows.Count == 64) { if (omitted < int.MaxValue) omitted++; return; }
                // Both values come only from this helper's finite source-owned vocabularies.
                if (!Enum.IsDefined(boundary) || outcome is not ("entered" or "returned" or "faulted" or "cancelled" or "observed"))
                { observerFaulted = true; return; }
                rows.Add(new(boundary.ToString(), outcome, clock.ElapsedMilliseconds));
            }
        }
        catch { MarkFaulted(); }
    }

    internal object Snapshot()
    {
        lock (sync) return new
        {
            SchemaVersion = 1,
            ClockDomain = "fixture-managed-elapsed-milliseconds",
            MilestoneSemantics = "unobserved-is-not-absence",
            Rows = rows.ToArray(),
            Omitted = omitted,
            Frozen = frozen,
            ObserverFaulted = observerFaulted,
        };
    }

    internal void Freeze() { lock (sync) frozen = true; }

    private void MarkFaulted() { try { lock (sync) observerFaulted = true; } catch { } }

    private bool Claim()
    {
        lock (sync)
        {
            if (frozen || claimed) return false;
            claimed = true;
            return true;
        }
    }

    internal async Task<T> ObserveAsync<T>(Boundary boundary, Func<Task<T>> operation)
    {
        Record(boundary, "entered");
        var outcome = "returned";
        try { return await operation(); }
        catch (OperationCanceledException) { outcome = "cancelled"; throw; }
        catch { outcome = "faulted"; throw; }
        finally { Record(boundary, outcome); }
    }

    private Task ObserveAsync(Boundary boundary, Func<Task> operation) =>
        ObserveAsync(boundary, async () => { await operation(); return true; });

    internal void Register(IServiceCollection services)
    {
        services.AddSingleton<IStartupFilter>(new RequestFilter(this));
        Decorate<ICountryClient>(services, inner => new Countries(inner, this));
        Decorate<IInstantQuotationWorkflowSessionIdentityAccessor>(services, inner => new Identity(inner, this));
        Decorate<IComponentActivator>(services, inner => new Components(inner, this));
    }

    private void Decorate<T>(IServiceCollection services, Func<T, T> wrap) where T : class
    {
        var candidates = services.Where(item => item.ServiceType == typeof(T) && !item.IsKeyedService).ToArray();
        if (candidates.Length != 1) { MarkFaulted(); return; }
        var original = candidates[0];
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(T), provider =>
            wrap((T)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!))), original.Lifetime));
    }

    private sealed class RequestFilter(MaterialInitialRequestStages stages) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext context, RequestDelegate pipeline) =>
            {
                if (!HttpMethods.IsGet(context.Request.Method)
                    || !string.Equals(context.Request.Path.Value, "/instantquotation/3d-printing", StringComparison.OrdinalIgnoreCase)
                    || !stages.Claim())
                {
                    await pipeline(context);
                    return;
                }

                using var aborted = AttachCallbacks(context);
                await stages.ObserveAsync(Boundary.Middleware, () => pipeline(context));
            });
            next(app);
        };

        private CancellationTokenRegistration AttachCallbacks(HttpContext context)
        {
            CancellationTokenRegistration aborted = default;
            try { aborted = context.RequestAborted.Register(() => stages.Record(Boundary.RequestAborted, "observed")); }
            catch { stages.MarkFaulted(); }
            try
            {
                context.Response.OnStarting(() =>
                {
                    stages.Record(Boundary.ResponseStarting, "observed");
                    return Task.CompletedTask;
                });
            }
            catch { stages.MarkFaulted(); }
            try
            {
                context.Response.OnCompleted(() =>
                {
                    stages.Record(Boundary.ResponseCompleted, "observed");
                    return Task.CompletedTask;
                });
            }
            catch { stages.MarkFaulted(); }
            return aborted;
        }
    }

    private sealed class Countries(ICountryClient inner, MaterialInitialRequestStages stages) : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken token) =>
            stages.ObserveAsync(Boundary.Countries, () => inner.GetCountriesAsync(token));
    }

    private sealed class Identity(IInstantQuotationWorkflowSessionIdentityAccessor inner, MaterialInitialRequestStages stages)
        : IInstantQuotationWorkflowSessionIdentityAccessor
    {
        public ValueTask<string?> GetProtectedSessionIdentityAsync(CancellationToken token) =>
            new(stages.ObserveAsync(Boundary.IdentityRead, () => inner.GetProtectedSessionIdentityAsync(token).AsTask()));

        public ValueTask SetProtectedSessionIdentityAsync(string value, CancellationToken token) =>
            new(stages.ObserveAsync(Boundary.IdentityWrite, () => inner.SetProtectedSessionIdentityAsync(value, token).AsTask()));
    }

    private sealed class Components(IComponentActivator inner, MaterialInitialRequestStages stages) : IComponentActivator
    {
        public IComponent CreateInstance(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type componentType) =>
            componentType == typeof(InstantQuotationPage) ? new Page(stages)
            : componentType == typeof(InstantQuotationWorkflow) ? new Workflow(stages)
            : inner.CreateInstance(componentType);
    }

    private sealed class Page(MaterialInitialRequestStages stages) : InstantQuotationPage
    {
        protected override Task OnParametersSetAsync() =>
            stages.ObserveAsync(Boundary.PageParameters, () => base.OnParametersSetAsync());
    }

    private sealed class Workflow(MaterialInitialRequestStages stages) : InstantQuotationWorkflow
    {
        protected override Task OnInitializedAsync() =>
            stages.ObserveAsync(Boundary.WorkflowInitialization, () => base.OnInitializedAsync());
    }

    internal sealed class Store(IInstantQuotationSessionStore inner, MaterialInitialRequestStages stages)
        : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState> CreateAsync(string? owner, InstantQuotationOrderState request, CancellationToken token) =>
            stages.ObserveAsync(Boundary.StoreCreate, () => inner.CreateAsync(owner, request, token));

        public Task<InstantQuotationSessionState?> GetAsync(string id, string? owner, CancellationToken token) =>
            stages.ObserveAsync(Boundary.StoreGet, () => inner.GetAsync(id, owner, token));

        public Task<bool> PutAsync(InstantQuotationSessionState session, string? owner, CancellationToken token) =>
            stages.ObserveAsync(Boundary.StorePut, () => inner.PutAsync(session, owner, token));

        public Task<bool> RemoveAsync(string id, string? owner, CancellationToken token) =>
            stages.ObserveAsync(Boundary.StoreRemove, () => inner.RemoveAsync(id, owner, token));
    }
}
