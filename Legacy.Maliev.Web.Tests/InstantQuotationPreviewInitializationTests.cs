using System.Reflection;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationPreviewInitializationTests
{
    [Theory]
    [InlineData("import")]
    [InlineData("create")]
    public async Task ConcurrentSelection_RetainsTheControllerThatOwnsItsGeometryKey(string phase)
    {
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runtime = new HeldInitialization(phase, lease.Token);
        var component = Create(runtime);
        var initialization = Invoke<Task>(component, "EnsurePreviewInteropAsync");
        Task<string[]>? selection = null;
        try
        {
            await runtime.Entered.Task.WaitAsync(lease.Token);
            selection = Invoke<Task<string[]>>(component, "BeginPreviewSelectionAsync");
            runtime.Release.TrySetResult();
            await initialization.WaitAsync(lease.Token);
            var key = Assert.Single(await selection.WaitAsync(lease.Token));
            var claim = await Invoke<Task<InstantQuotationGeometryClaim>>(component, "ReadGeometryClaimAsync", key)
                .WaitAsync(lease.Token);
            Assert.Equal(12, claim.FacetCount);
            Assert.Equal(1, runtime.ImportCalls);
            Assert.Equal(1, Assert.Single(runtime.Modules).CreateCalls);
            Assert.Equal(key, Assert.Single(Assert.Single(runtime.Modules).Controller.ReadKeys));
        }
        finally
        {
            runtime.Release.TrySetResult();
            try { await Task.WhenAll(initialization, (Task?)selection ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { await component.DisposeAsync(); }
        }
        var module = Assert.Single(runtime.Modules);
        Assert.Equal(1, module.DisposeCalls);
        Assert.Equal(1, module.Controller.DisposeCalls);
        Assert.Equal(1, module.Controller.ReleaseCalls);
        Assert.Throws<ObjectDisposedException>(() => module.Callback!.Value);
    }

    [Theory]
    [InlineData("import")]
    [InlineData("create")]
    [InlineData("create-failure")]
    public async Task Disposal_WaitsForPendingInitializationAndReleasesEveryAcquiredReference(string phase)
    {
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runtime = new HeldInitialization(phase, lease.Token);
        var component = Create(runtime);
        var initialization = Invoke<Task>(component, "OnAfterRenderAsync", true);
        Task? disposal = null;
        bool settledBeforeRelease = false;
        bool callbackOwned = false;
        DotNetObjectReference<InstantQuotationWorkflow>? callback = null;
        try
        {
            await runtime.Entered.Task.WaitAsync(lease.Token);
            disposal = component.DisposeAsync().AsTask();
            settledBeforeRelease = disposal.IsCompleted;
            callback = Assert.Single(runtime.Modules).Callback;
            try { callbackOwned = callback is null || ReferenceEquals(component, callback.Value); }
            catch (ObjectDisposedException) { callbackOwned = false; }
        }
        finally
        {
            runtime.Release.TrySetResult();
            try { await initialization.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { await (disposal ?? component.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(5)); }
        }

        Assert.False(settledBeforeRelease);
        Assert.True(callbackOwned);
        var module = Assert.Single(runtime.Modules);
        Assert.Equal(1, module.DisposeCalls);
        Assert.Equal(phase == "import" ? 0 : 1, module.CreateCalls);
        Assert.Equal(phase == "create" ? 1 : 0, module.Controller.DisposeCalls);
        Assert.Equal(phase == "create" ? 1 : 0, module.Controller.ReleaseCalls);
        Assert.Equal(0, module.Controller.OperationalCalls);
        if (callback is not null) Assert.Throws<ObjectDisposedException>(() => callback.Value);
        await component.DisposeAsync();
        await Invoke<Task>(component, "EnsurePreviewInteropAsync");
        await Invoke<Task>(component, "OnAfterRenderAsync", false);
        Assert.Equal(1, runtime.ImportCalls);
        Assert.Equal(1, module.DisposeCalls);
        Assert.Equal(0, module.Controller.OperationalCalls);
    }

    private static InstantQuotationWorkflow Create(IJSRuntime runtime)
    {
        var component = new InstantQuotationWorkflow();
        typeof(InstantQuotationWorkflow).GetProperty("Services", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, new NoOptionalServices());
        typeof(InstantQuotationWorkflow).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, runtime);
        typeof(InstantQuotationWorkflow).GetField("fileInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, new InputFile());
        return component;
    }

    private sealed class NoOptionalServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static T Invoke<T>(InstantQuotationWorkflow component, string method, params object[] args) =>
        (T)typeof(InstantQuotationWorkflow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, args)!;

    private sealed class HeldInitialization(string phase, CancellationToken token) : IJSRuntime
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<Module> Modules { get; } = [];
        internal int ImportCalls { get; private set; }
        private string Phase { get; } = phase;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("import", identifier);
            int ordinal = ++ImportCalls;
            var module = new Module(this, ordinal);
            Modules.Add(module);
            if (ordinal == 1 && Phase == "import") await HoldAsync();
            return (TValue)(object)module;
        }
        internal async Task HoldAsync()
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
        }
        internal sealed class Module(HeldInitialization owner, int ordinal) : IJSObjectReference
        {
            private int Ordinal { get; } = ordinal;
            internal Controller Controller { get; } = new($"selection-{ordinal}");
            internal DotNetObjectReference<InstantQuotationWorkflow>? Callback { get; private set; }
            internal int CreateCalls { get; private set; }
            internal int DisposeCalls { get; private set; }
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
            public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            {
                Assert.Equal("createInstantQuotationWorkflowInterop", identifier);
                CreateCalls++;
                Callback = Assert.IsType<DotNetObjectReference<InstantQuotationWorkflow>>(args![0]);
                if (Ordinal == 1 && owner.Phase.StartsWith("create", StringComparison.Ordinal)) await owner.HoldAsync();
                if (owner.Phase == "create-failure") throw new JSException("controlled-detached-initialization");
                return (TValue)(object)Controller;
            }
            public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
        }
    }

    private sealed class Controller(string ownedKey) : IJSObjectReference
    {
        internal List<string> ReadKeys { get; } = [];
        internal int DisposeCalls { get; private set; }
        internal int ReleaseCalls { get; private set; }
        internal int OperationalCalls { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? result = null;
            if (identifier != "dispose") OperationalCalls++;
            if (identifier == "beginSelection") result = new[] { ownedKey };
            if (identifier == "getGeometryClaim")
            {
                var key = Assert.IsType<string>(args![0]);
                Assert.Equal(ownedKey, key);
                ReadKeys.Add(key);
                result = new InstantQuotationGeometryClaim(1, new string('A', 64), 10, 10, 10, 1_000, 600,
                    [100], [40], 12, 1, true, false, false, 1);
            }
            if (identifier == "dispose") ReleaseCalls++;
            return ValueTask.FromResult(result is null ? default! : (TValue)result);
        }
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }
}
