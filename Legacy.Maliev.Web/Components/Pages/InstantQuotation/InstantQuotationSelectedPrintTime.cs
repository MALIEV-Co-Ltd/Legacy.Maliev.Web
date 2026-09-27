namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal static class InstantQuotationSelectedPrintTime
{
    internal static bool IsVisible(double? minutes, bool isRepricing) =>
        !isRepricing && minutes is > 0 && double.IsFinite(minutes.Value);
}
