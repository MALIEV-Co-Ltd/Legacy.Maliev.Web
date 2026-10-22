using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Sends typed quotation observations to the browser analytics bridge without interrupting the workflow.</summary>
public sealed class JsInstantQuotationAnalyticsSink : IInstantQuotationAnalyticsSink
{
    private readonly IJSRuntime jsRuntime;

    /// <summary>Creates the browser-interop sink for best-effort quotation observations.</summary>
    /// <param name="jsRuntime">The browser interop runtime used to invoke the analytics bridge.</param>
    public JsInstantQuotationAnalyticsSink(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    /// <inheritdoc />
    public async ValueTask EmitAsync(
        InstantQuotationAnalyticsPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            await jsRuntime.InvokeVoidAsync(
                "window.malievAnalytics.emit",
                cancellationToken,
                payload);
        }
        catch (Exception)
        {
            // Analytics is best-effort and must never interrupt the quotation workflow.
        }
    }
}
