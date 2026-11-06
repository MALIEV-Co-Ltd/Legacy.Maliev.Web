using System.Reflection;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationMaterialSelectorLifetimeTests
{
    [Theory]
    [InlineData("import")]
    [InlineData("create")]
    [InlineData("create-failure")]
    [InlineData("refresh")]
    public async Task Disposal_SettlesPendingRenderBeforeReleasingItsReferences(string phase)
    {
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DotNetObjectReference<InstantQuotationMaterialSelector>? callback = null;
        var controller = new Reference(async (method, _) =>
        {
            if (method == "refresh")
            {
                await release.Task.WaitAsync(lease.Token);
                throw new JSException("controlled-detached-selector");
            }
            return null;
        });
        var module = new Reference(async (_, args) =>
        {
            callback = Assert.IsType<DotNetObjectReference<InstantQuotationMaterialSelector>>(args![1]);
            if (phase.StartsWith("create", StringComparison.Ordinal)) await release.Task.WaitAsync(lease.Token);
            if (phase == "create-failure") throw new JSException("controlled-detached-selector");
            return controller;
        });
        var selector = Create(new Runtime(async () =>
        {
            if (phase == "import") await release.Task.WaitAsync(lease.Token);
            return module;
        }));
        var render = Render(selector, true);
        if (phase == "refresh")
        {
            await render.WaitAsync(lease.Token);
            render = Render(selector, false);
        }
        var disposal = selector.DisposeAsync().AsTask();
        bool disposedBeforeCompletion = disposal.IsCompleted;
        bool callbackStillOwned = callback is null || ReferenceEquals(selector, ReadValue(callback));
        release.TrySetResult();
        var failure = await Record.ExceptionAsync(() => Task.WhenAll(render, disposal).WaitAsync(lease.Token));

        Assert.False(disposedBeforeCompletion);
        Assert.True(callbackStillOwned);
        Assert.Null(failure);
        Assert.Equal(1, module.DisposeCalls);
        Assert.Equal(phase == "import" ? 0 : 1, module.InvokeCalls);
        Assert.Equal(phase is "import" or "create-failure" ? 0 : 1, controller.DisposeCalls);
        Assert.Equal(phase is "import" or "create-failure" ? 0 : 1, controller.ReleaseCalls);
        if (callback is not null) Assert.Throws<ObjectDisposedException>(() => callback.Value);
        await selector.DisposeAsync();
        await selector.SetPreviewCount(10);
        await Render(selector, true);
        Assert.Equal(1, module.DisposeCalls);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("refresh")]
    public async Task LiveRenderFailure_PreservesTheOriginalExceptionAndStillReleasesOwnership(string phase)
    {
        var original = new JSException("controlled-live-selector");
        var controller = new Reference((method, _) => method == "refresh"
            ? Task.FromException<object?>(original) : Task.FromResult<object?>(null));
        var module = new Reference((_, _) => phase == "create"
            ? Task.FromException<object?>(original) : Task.FromResult<object?>(controller));
        var selector = Create(new Runtime(() => Task.FromResult<IJSObjectReference>(module)));
        var render = Render(selector, true);
        if (phase == "refresh")
        {
            await render;
            render = Render(selector, false);
        }
        Assert.Same(original, await Record.ExceptionAsync(() => render));
        Assert.Same(original, await Record.ExceptionAsync(() => selector.DisposeAsync().AsTask()));
        Assert.Equal(1, module.DisposeCalls);
        Assert.Equal(phase == "create" ? 0 : 1, controller.DisposeCalls);
    }

    private static object? ReadValue(DotNetObjectReference<InstantQuotationMaterialSelector> reference)
    {
        try { return reference.Value; }
        catch (ObjectDisposedException) { return null; }
    }

    private static InstantQuotationMaterialSelector Create(IJSRuntime runtime)
    {
        var selector = new InstantQuotationMaterialSelector();
        typeof(InstantQuotationMaterialSelector).GetProperty("JavaScript", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(selector, runtime);
        return selector;
    }

    private static Task Render(InstantQuotationMaterialSelector selector, bool first) =>
        (Task)typeof(InstantQuotationMaterialSelector)
            .GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(selector, [first])!;

    private sealed class Runtime(Func<Task<IJSObjectReference>> import) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("import", identifier);
            return (TValue)await import();
        }
    }

    private sealed class Reference(Func<string, object?[]?, Task<object?>> invoke) : IJSObjectReference
    {
        public int InvokeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            InvokeCalls++;
            if (identifier == "dispose") ReleaseCalls++;
            var result = await invoke(identifier, args);
            return result is null ? default! : (TValue)result;
        }
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }
}
