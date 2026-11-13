using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Derives display-only bulk savings from the current part's matching server quote.</summary>
public static class InstantQuotationBulkSavings
{
    /// <summary>Compares active and single-piece tier prices only while the accepted configuration remains current.</summary>
    /// <param name="part">The part configuration and its matching price tiers.</param>
    /// <param name="quantityBeingEdited">Whether a quantity draft makes the current quote unsuitable for savings display.</param>
    /// <returns>The positive order saving rounded to two decimals, or null when no current saving is available.</returns>
    public static double? Calculate(
        InstantQuotationWorkflowPartViewModel part,
        bool quantityBeingEdited)
    {
        var quote = part.Quote;
        var configuration = part.Configuration;
        if (quantityBeingEdited || quote is null || configuration.Quantity <= 1
            || quote.PartId != part.PartId
            || !string.Equals(quote.MaterialKey, configuration.MaterialKey, StringComparison.Ordinal)
            || quote.Quantity != configuration.Quantity
            || quote.BuildPreference != (quote.Process is PrintProcess.Resin
                ? BuildPreference.Standard : configuration.BuildPreference))
        {
            return null;
        }

        var single = quote.Tiers.SingleOrDefault(static tier => tier.MinQuantity == 1);
        var active = quote.Tiers.SingleOrDefault(static tier => tier.Active);
        if (single is null || active is null
            || !double.IsFinite(single.UnitPrice) || !double.IsFinite(active.UnitPrice))
        {
            return null;
        }

        var saving = (single.UnitPrice - active.UnitPrice) * configuration.Quantity;
        return double.IsFinite(saving) && saving > 0 ? Math.Round(saving, 2, MidpointRounding.AwayFromZero) : null;
    }
}
