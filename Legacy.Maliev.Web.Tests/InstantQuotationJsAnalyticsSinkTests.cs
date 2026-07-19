using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationJsAnalyticsSinkTests
{
    [Fact]
    public async Task EmitAsync_InvokesOnlyApprovedAnalyticsBridgeWithPayload()
    {
        var runtime = new RecordingJsRuntime();
        var sink = new JsInstantQuotationAnalyticsSink(runtime);
        var payload = CreatePayload();

        await sink.EmitAsync(payload);

        Assert.Equal("window.malievAnalytics.emit", runtime.Identifier);
        Assert.Collection(runtime.Arguments!, value => Assert.Same(payload, value));
    }

    [Fact]
    public async Task EmitAsync_JsFailureDoesNotBreakQuotationFlow()
    {
        var sink = new JsInstantQuotationAnalyticsSink(new ThrowingJsRuntime());

        await sink.EmitAsync(CreatePayload());
    }

    private static InstantQuotationAnalyticsPayload CreatePayload()
    {
        var sink = new CapturingSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);
        tracker.RecordEstimateShownAsync(1).AsTask().GetAwaiter().GetResult();
        return Assert.Single(sink.Payloads);
    }

    private sealed class CapturingSink : IInstantQuotationAnalyticsSink
    {
        public List<InstantQuotationAnalyticsPayload> Payloads { get; } = [];

        public ValueTask EmitAsync(
            InstantQuotationAnalyticsPayload payload,
            CancellationToken cancellationToken = default)
        {
            Payloads.Add(payload);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public string? Identifier { get; private set; }

        public object?[]? Arguments { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Identifier = identifier;
            Arguments = args;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class ThrowingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("Analytics bridge unavailable."));

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
