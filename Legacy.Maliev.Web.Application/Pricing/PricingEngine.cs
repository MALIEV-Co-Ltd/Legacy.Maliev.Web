namespace Legacy.Maliev.Web.Application.Pricing;

public static class PricingEngine
{
    public static ItemQuote QuoteItem(GeometryInput geometry, MaterialInfo material, int quantity)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(material);

        var normalizedQuantity = Math.Max(1, quantity);
        double printTime;
        double materialPerUnit;
        double weightGrams;
        double complexityAdjustedCost;

        if (material.Process == PrintProcess.Resin)
        {
            printTime = PrintTimeCalculator.ResinMinutes(geometry);
            var resinMilliliters = (Math.Abs(geometry.VolumeMm3) / 1_000.0)
                * (1 + PricingCatalog.ResinSupportAllowance);
            materialPerUnit = resinMilliliters;
            weightGrams = resinMilliliters * ShippingCalculator.ResinDensityGramsPerMl;
            var partsPerPlate = PricingCatalog.EstimatePartsPerPlate(geometry.FootprintMm2);
            complexityAdjustedCost = ResinDirectCost(
                printTime,
                resinMilliliters,
                material,
                partsPerPlate) * PricingCatalog.ComplexityFactor;
        }
        else
        {
            var estimate = PrintTimeCalculator.EstimateFdm(geometry, material);
            printTime = estimate.PrintMinutes;
            materialPerUnit = estimate.MaterialGrams;
            weightGrams = estimate.MaterialGrams;
            complexityAdjustedCost = FdmDirectCost(
                printTime,
                estimate.MaterialGrams,
                estimate.SupportGrams,
                material) * PricingCatalog.ComplexityFactor;
        }

        var setupLabor = PricingCatalog.SetupHours(material.Process) * PricingCatalog.LaborRatePerHour;
        var failureRate = PricingCatalog.FailureReserveRate(material.Process);
        var paymentGrossUp = 1 + (PricingCatalog.PaymentFeeRate / (1 - PricingCatalog.PaymentFeeRate));
        var activeTier = PricingCatalog.ResolveTier(normalizedQuantity);
        var tiers = PricingCatalog.DiscountTiers.Select(tier => new BulkTier
        {
            MinQuantity = tier.MinQuantity,
            UnitPrice = AllInUnitPrice(
                complexityAdjustedCost,
                setupLabor,
                failureRate,
                paymentGrossUp,
                tier,
                tier.MinQuantity),
            Active = tier.MinQuantity == activeTier.MinQuantity,
        }).ToArray();

        var unroundedUnitPrice = AllInUnitPrice(
            complexityAdjustedCost,
            setupLabor,
            failureRate,
            paymentGrossUp,
            activeTier,
            normalizedQuantity);
        var subtotal = RoundLineItemSubtotal(unroundedUnitPrice * normalizedQuantity);
        var boundingCm3 = (Math.Abs(geometry.FootprintMm2) * Math.Abs(geometry.HeightMm)) / 1_000.0;

        return new ItemQuote
        {
            Process = material.Process,
            PrintTimeMinutesPerUnit = printTime,
            MaterialPerUnit = materialPerUnit,
            WeightGramsPerUnit = weightGrams,
            BoundingCm3PerUnit = boundingCm3,
            UnitPrice = subtotal / normalizedQuantity,
            Subtotal = subtotal,
            Tiers = tiers,
        };
    }

    public static double RoundLineItemSubtotal(double subtotal) =>
        RoundUpToNearest(Math.Max(0, subtotal), 100);

    public static OrderQuote QuoteOrder(IEnumerable<OrderLine>? lines, double shippingThb)
    {
        var orderLines = lines?.ToArray() ?? [];
        if (orderLines.Length == 0)
        {
            return new OrderQuote();
        }

        var itemsSubtotal = orderLines.Sum(line => line.Subtotal);
        var minimumFloor = orderLines.Max(line => PricingCatalog.MinimumOrderPrice(line.Process));
        var printing = Math.Max(minimumFloor, itemsSubtotal);
        var shipping = Math.Max(0, shippingThb);
        var priceBeforeVat = printing + shipping;
        var vat = priceBeforeVat * PricingCatalog.VatRate;

        return new OrderQuote
        {
            ItemsSubtotal = itemsSubtotal,
            Printing = printing,
            ShippingCost = shipping,
            PriceBeforeVat = priceBeforeVat,
            Vat = vat,
            FinalOrderPrice = RoundUpToNearest(priceBeforeVat + vat, 5),
        };
    }

    internal static double FdmDirectCost(
        double printTimeMinutes,
        double weightGrams,
        double supportGrams,
        MaterialInfo material)
    {
        var machineHourly = PricingCatalog.MachineHourly(PrintProcess.Fdm);
        var overheadPerMinute = PricingCatalog.OverheadPerMinute(PrintProcess.Fdm);
        var materialCost = weightGrams * material.CostPerUnit * (1 + PricingCatalog.FdmWasteAllowance);
        var machineOverhead = printTimeMinutes * ((machineHourly / 60.0) + overheadPerMinute);
        var supportRemovalLabor = ((supportGrams * PricingCatalog.FdmSupportRemovalSecondsPerGram) / 3_600.0)
            * PricingCatalog.LaborRatePerHour;
        return materialCost + machineOverhead + supportRemovalLabor;
    }

    internal static double ResinDirectCost(
        double printTimeMinutes,
        double resinMilliliters,
        MaterialInfo material,
        int partsPerPlate)
    {
        var nestedParts = Math.Max(1, partsPerPlate);
        var machineHourly = PricingCatalog.MachineHourly(PrintProcess.Resin);
        var overheadPerMinute = PricingCatalog.OverheadPerMinute(PrintProcess.Resin);
        var resinCost = resinMilliliters * material.CostPerUnit;
        var machineCost = (printTimeMinutes * (machineHourly / 60.0)) / nestedParts;
        var overheadCost = (printTimeMinutes * overheadPerMinute) / nestedParts;
        var postProcessing = PricingCatalog.ResinPostProcessingHours * PricingCatalog.LaborRatePerHour;
        return resinCost + machineCost + overheadCost + postProcessing + PricingCatalog.ResinConsumablesPerPart;
    }

    private static double AllInUnitPrice(
        double complexityAdjustedCost,
        double setupLabor,
        double failureRate,
        double paymentGrossUp,
        DiscountTier tier,
        int quantity)
    {
        var unitCost = complexityAdjustedCost + (setupLabor / Math.Max(1, quantity));
        var marginBased = (unitCost / (1 - tier.TargetMargin)) * (1 - tier.BulkDiscount);
        return marginBased * (1 + failureRate) * paymentGrossUp;
    }

    private static double RoundUpToNearest(double value, double step) =>
        step <= 0 ? value : Math.Ceiling(value / step) * step;
}
