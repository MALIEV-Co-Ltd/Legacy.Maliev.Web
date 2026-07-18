using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Application;

public interface IInstantQuotationPricingService
{
    InstantQuotationOrderQuote Quote(InstantQuotationOrderState state);
}

public sealed class InstantQuotationPricingService : IInstantQuotationPricingService
{
    public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var partQuotes = (state.Parts ?? throw new ArgumentException("Parts are required.", nameof(state)))
            .Select(QuotePart)
            .ToArray();
        var shipping = partQuotes.Length == 0
            ? 0
            : ShippingCalculator.CustomerShippingThb(
                partQuotes.Sum(part => part.WeightGramsPerUnit * part.Quantity),
                partQuotes.Sum(part => part.BoundingCm3PerUnit * part.Quantity));
        var order = PricingEngine.QuoteOrder(
            partQuotes.Select(part => new OrderLine
            {
                Process = part.Process,
                Subtotal = part.Subtotal,
            }),
            shipping);
        var totalPrintMinutes = partQuotes.Sum(part => part.PrintTimeMinutesPerUnit * part.Quantity);
        var minimumLeadTimeDays = Math.Max(1, (int)Math.Ceiling(totalPrintMinutes / 1_440));

        return new InstantQuotationOrderQuote(
            partQuotes,
            order.ItemsSubtotal,
            order.Printing,
            order.ShippingCost,
            order.PriceBeforeVat,
            order.Vat,
            order.FinalOrderPrice,
            minimumLeadTimeDays,
            minimumLeadTimeDays + 2);
    }

    private static InstantQuotationPartQuote QuotePart(InstantQuotationPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(part.Geometry);
        ArgumentNullException.ThrowIfNull(part.Configuration);

        var configuration = part.Configuration;
        if (configuration.Quantity is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration.Quantity),
                configuration.Quantity,
                "Quantity must be between 1 and 1000.");
        }

        var material = PricingCatalog.ResolveMaterial(configuration.MaterialKey)
            ?? throw new ArgumentException("The selected material is not supported.", nameof(configuration.MaterialKey));
        if (!PricingCatalog.IsColorSupported(material.Key, configuration.Color))
        {
            throw new ArgumentException(
                "The selected color is not supported for the selected material.",
                nameof(configuration.Color));
        }

        var geometry = part.Geometry;
        var item = PricingEngine.QuoteItem(
            new GeometryInput
            {
                HeightMm = geometry.HeightMm,
                VolumeMm3 = geometry.VolumeMm3,
                FootprintMm2 = geometry.FootprintMm2,
                AreaProfileMm2 = geometry.AreaProfileMm2,
                PerimeterProfileMm = geometry.PerimeterProfileMm,
            },
            material,
            configuration.Quantity);

        return new InstantQuotationPartQuote(
            part.PartId,
            material.Key,
            configuration.Color,
            configuration.Quantity,
            item.Process,
            item.PrintTimeMinutesPerUnit,
            item.MaterialPerUnit,
            item.WeightGramsPerUnit,
            item.BoundingCm3PerUnit,
            item.UnitPrice,
            item.Subtotal,
            item.Tiers);
    }
}
