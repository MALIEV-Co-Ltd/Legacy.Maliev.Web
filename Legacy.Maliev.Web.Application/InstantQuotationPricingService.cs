using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Application;

public interface IInstantQuotationPricingService
{
    InstantQuotationOrderQuote Quote(InstantQuotationOrderState state);
}

public sealed class InstantQuotationPricingService : IInstantQuotationPricingService
{
    public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state) => Quote(state, null);

    public InstantQuotationOrderQuote Quote(
        InstantQuotationOrderState state,
        string? destinationCountry)
    {
        ArgumentNullException.ThrowIfNull(state);

        var partQuotes = (state.Parts ?? throw new ArgumentException("Parts are required.", nameof(state)))
            .Select(QuotePart)
            .ToArray();
        var shippingQuote = partQuotes.Length == 0
            ? new ShippingQuote
            {
                DestinationCountryCode = "TH",
                State = ShippingPricingState.DomesticPriced,
                AmountThb = 0,
            }
            : ShippingCalculator.Quote(
                destinationCountry,
                partQuotes.Sum(part => part.WeightGramsPerUnit * part.Quantity),
                partQuotes.Sum(part => part.BoundingCm3PerUnit * part.Quantity));
        var lines = partQuotes.Select(part =>
        {
            var tier = PricingCatalog.ResolveTier(part.Quantity);
            var material = PricingCatalog.ResolveMaterial(part.MaterialKey)!;
            var materialMinimum = material.RequiresDrying
                ? Convert.ToDecimal(PricingCatalog.TechnicalFilamentMinimumPrice)
                : 0m;
            return new AdditiveOrderCostLine
            {
                LineId = part.PartId.ToString("N"),
                Quantity = part.Quantity,
                DirectCostPerUnitThb = Convert.ToDecimal(part.DirectCostPerUnit),
                ComplexityFactor = 1m,
                TargetMarginRate = part.Process == PrintProcess.Resin
                    ? 0.30m
                    : Convert.ToDecimal(tier.TargetMargin),
                DiscountRate = Convert.ToDecimal(tier.BulkDiscount),
                ReserveRate = Convert.ToDecimal(PricingCatalog.FailureReserveRate(part.Process)),
                MinimumOrderPriceThb = Math.Max(
                    Convert.ToDecimal(PricingCatalog.MinimumOrderPrice(part.Process)),
                    materialMinimum),
            };
        }).ToArray();
        var deliveryIncludesPackaging = shippingQuote.State == ShippingPricingState.DomesticPriced;
        var order = AdditiveOrderCostCalculator.Calculate(lines, new AdditiveOrderCharges
        {
            SetupThb = partQuotes.Max(part => Convert.ToDecimal(
                PricingCatalog.SetupHours(part.Process) * PricingCatalog.LaborRatePerHour)),
            PackagingThb = deliveryIncludesPackaging
                ? 0m
                : partQuotes.Max(part => Convert.ToDecimal(PricingCatalog.PackagingCost(part.Process))),
            DeliveryThb = shippingQuote.AmountThb,
            RushRate = Convert.ToDecimal(PricingCatalog.RushSurcharge),
            PaymentFeeRate = Convert.ToDecimal(PricingCatalog.PaymentFeeRate),
            VatRate = Convert.ToDecimal(PricingCatalog.VatRate),
        });
        var minimumOrderPrice = lines.Max(static line => line.MinimumOrderPriceThb);
        var allocationsByPart = order.LineAllocations.ToDictionary(
            static allocation => Guid.ParseExact(allocation.LineId, "N"),
            static allocation => allocation.TotalThb);
        var allocatedParts = partQuotes.Select(part => part with
        {
            AllocatedOrderTotal = Convert.ToDouble(allocationsByPart[part.PartId]),
        }).ToArray();
        var leadTime = AdditiveLeadTimeCalculator.Calculate(partQuotes.Select(part => new AdditiveLeadTimeLine
        {
            MinutesPerUnit = Convert.ToDecimal(part.PrintTimeMinutesPerUnit),
            Quantity = part.Quantity,
        }));

        return new InstantQuotationOrderQuote(
            allocatedParts,
            Convert.ToDouble(order.UnroundedBaseThb),
            Convert.ToDouble(order.BaseOrderThb),
            Convert.ToDouble(minimumOrderPrice),
            Convert.ToDouble(Math.Max(0m, order.BaseOrderThb - order.UnroundedBaseThb)),
            Convert.ToDouble(order.DeliveryThb),
            Convert.ToDouble(order.PriceBeforeVatThb),
            Convert.ToDouble(order.VatThb),
            Convert.ToDouble(order.TotalThb),
            leadTime.MinimumDays,
            leadTime.MaximumDays,
            shippingQuote.State,
            shippingQuote.DestinationCountryCode,
            Convert.ToDouble(order.SetupThb),
            Convert.ToDouble(order.ReserveThb),
            Convert.ToDouble(order.PackagingThb),
            Convert.ToDouble(order.PaymentFeeThb),
            Convert.ToDouble(order.RoundingAdjustmentThb),
            allocatedParts.Select(static part => part.AllocatedOrderTotal).ToArray());
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
        var geometryInput = new GeometryInput
        {
            HeightMm = geometry.HeightMm,
            VolumeMm3 = geometry.VolumeMm3,
            FootprintMm2 = geometry.FootprintMm2,
            AreaProfileMm2 = geometry.AreaProfileMm2,
            PerimeterProfileMm = geometry.PerimeterProfileMm,
            UnsupportedAreaProfileMm2 = geometry.UnsupportedAreaProfileMm2,
        };
        var validation = AdditiveGeometryValidator.Validate(geometryInput, configuration.Quantity);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                $"The submitted geometry is not eligible for pricing: {string.Join(',', validation.ReasonCodes)}.",
                nameof(part.Geometry));
        }
        var buildPreference = material.Process == PrintProcess.Resin
            ? BuildPreference.Standard
            : configuration.BuildPreference;
        var item = PricingEngine.QuoteItem(
            geometryInput,
            material,
            configuration.Quantity,
            buildPreference);
        var materialPrices = PricingCatalog.Materials.Values
            .Select(candidate => new InstantQuotationMaterialPrice(
                candidate.Key,
                PricingEngine.QuoteItem(
                    geometryInput,
                    candidate,
                    configuration.Quantity,
                    candidate.Process == PrintProcess.Resin ? BuildPreference.Standard : buildPreference).UnitPrice))
            .ToArray();

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
            item.DirectCostPerUnit,
            item.UnitPrice,
            item.Subtotal,
            item.TechnicalFilamentMinimumApplied,
            item.TechnicalFilamentMinimumPrice,
            item.TechnicalFilamentMinimumAdjustment,
            item.Tiers,
            buildPreference,
            materialPrices);
    }
}
