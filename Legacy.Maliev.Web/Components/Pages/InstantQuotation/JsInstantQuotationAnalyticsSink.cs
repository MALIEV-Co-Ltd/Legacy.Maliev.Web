using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public sealed class JsInstantQuotationAnalyticsSink : IInstantQuotationAnalyticsSink
{
    private readonly IJSRuntime jsRuntime;

    public JsInstantQuotationAnalyticsSink(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

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
