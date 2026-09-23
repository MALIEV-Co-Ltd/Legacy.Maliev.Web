namespace Legacy.Maliev.Web.Application.Pricing;

public static class PricingEngine
{
    public static ItemQuote QuoteItem(
        GeometryInput geometry,
        MaterialInfo material,
        int quantity,
        BuildPreference buildPreference = BuildPreference.Standard)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(material);

        var normalizedQuantity = PricingCatalog.NormalizeAdditiveQuantity(quantity);
        double printTime;
        double materialPerUnit;
        double weightGrams;
        Func<int, double> complexityAdjustedCostAtQuantity;

        if (material.Process == PrintProcess.Resin)
        {
            printTime = PrintTimeCalculator.ResinMinutes(geometry);
            var resinMilliliters = (Math.Abs(geometry.VolumeMm3) / 1_000.0)
                * (1 + PricingCatalog.ResinSupportAllowance);
            materialPerUnit = resinMilliliters;
            weightGrams = resinMilliliters * ShippingCalculator.ResinDensityGramsPerMl;
            var capacityPerPlate = PricingCatalog.EstimatePartsPerPlate(geometry.FootprintMm2);
            complexityAdjustedCostAtQuantity = pricedQuantity => ResinDirectCost(
                printTime,
                resinMilliliters,
                material,
                pricedQuantity,
                capacityPerPlate) * PricingCatalog.ComplexityFactor;
        }
        else
        {
            var estimate = PrintTimeCalculator.EstimateFdm(geometry, material, buildPreference);
            printTime = estimate.PrintMinutes;
            materialPerUnit = estimate.MaterialGrams;
            weightGrams = estimate.MaterialGrams;
            var fdmCost = FdmDirectCost(
                printTime,
                estimate.MaterialGrams,
                estimate.SupportGrams,
                material) * PricingCatalog.ComplexityFactor;
            complexityAdjustedCostAtQuantity = _ => fdmCost;
        }

        var setupLabor = PricingCatalog.SetupHours(material.Process) * PricingCatalog.LaborRatePerHour;
        var failureRate = PricingCatalog.FailureReserveRate(material.Process);
        var paymentGrossUp = 1 + (PricingCatalog.PaymentFeeRate / (1 - PricingCatalog.PaymentFeeRate));
        var activeTier = PricingCatalog.ResolveTier(normalizedQuantity);
        var activeBulkQuantity = PricingCatalog.ResolveBulkQuoteQuantity(normalizedQuantity);
        var tiers = PricingCatalog.BulkQuoteQuantities.Select(bulkQuantity => new BulkTier
        {
            MinQuantity = bulkQuantity,
            UnitPrice = ApplyTechnicalFilamentMinimumUnitPrice(
                RoundUnitPrice(AllInUnitPrice(
                    complexityAdjustedCostAtQuantity(bulkQuantity),
                    setupLabor,
                    failureRate,
                    paymentGrossUp,
                    PricingCatalog.ResolveTier(bulkQuantity),
                    bulkQuantity)),
                bulkQuantity,
                material),
            Active = bulkQuantity == activeBulkQuantity,
        }).ToArray();

        var complexityAdjustedCost = complexityAdjustedCostAtQuantity(normalizedQuantity);
        var unroundedUnitPrice = AllInUnitPrice(
            complexityAdjustedCost,
            setupLabor,
            failureRate,
            paymentGrossUp,
            activeTier,
            normalizedQuantity);
        var calculatedUnitPrice = RoundUnitPrice(unroundedUnitPrice);
        var unitPrice = ApplyTechnicalFilamentMinimumUnitPrice(calculatedUnitPrice, normalizedQuantity, material);
        var calculatedSubtotal = calculatedUnitPrice * normalizedQuantity;
        var subtotal = unitPrice * normalizedQuantity;
        var boundingCm3 = (Math.Abs(geometry.FootprintMm2) * Math.Abs(geometry.HeightMm)) / 1_000.0;

        return new ItemQuote
        {
            Process = material.Process,
            DirectCostPerUnit = complexityAdjustedCost,
            PrintTimeMinutesPerUnit = printTime,
            MaterialPerUnit = materialPerUnit,
            WeightGramsPerUnit = weightGrams,
            BoundingCm3PerUnit = boundingCm3,
            UnitPrice = unitPrice,
            Subtotal = subtotal,
            TechnicalFilamentMinimumApplied = unitPrice > calculatedUnitPrice,
            TechnicalFilamentMinimumPrice = material.RequiresDrying ? PricingCatalog.TechnicalFilamentMinimumPrice : 0,
            TechnicalFilamentMinimumAdjustment = subtotal - calculatedSubtotal,
            Tiers = tiers,
        };
    }

    private static double ApplyTechnicalFilamentMinimumUnitPrice(
        double unitPrice,
        int quantity,
        MaterialInfo material)
    {
        if (!material.RequiresDrying || (unitPrice * quantity) >= PricingCatalog.TechnicalFilamentMinimumPrice)
        {
            return unitPrice;
        }

        return Math.Max(
            unitPrice,
            RoundUnitPrice(PricingCatalog.TechnicalFilamentMinimumPrice / quantity));
    }

    public static double RoundUnitPrice(double unitPrice) =>
        RoundUpToNearest(Math.Max(0, unitPrice), 10);

    public static OrderQuote QuoteOrder(IEnumerable<OrderLine>? lines, double shippingThb)
    {
        var orderLines = lines?.ToArray() ?? [];
        if (orderLines.Length == 0)
        {
            return new OrderQuote();
        }

        var itemsSubtotal = orderLines.Sum(line => line.Subtotal);
        var minimumOrderPrice = orderLines.Max(line => PricingCatalog.MinimumOrderPrice(line.Process));
        var printing = Math.Max(minimumOrderPrice, itemsSubtotal);
        var minimumOrderSurcharge = printing - itemsSubtotal;
        var shipping = Math.Max(0, shippingThb);
        var priceBeforeVat = printing + shipping;
        var vat = priceBeforeVat * PricingCatalog.VatRate;

        return new OrderQuote
        {
            ItemsSubtotal = itemsSubtotal,
            Printing = printing,
            MinimumOrderPrice = minimumOrderPrice,
            MinimumOrderSurcharge = minimumOrderSurcharge,
            ShippingCost = shipping,
            PriceBeforeVat = priceBeforeVat,
            Vat = vat,
            FinalOrderPrice = priceBeforeVat + vat,
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
        return ResinDirectCost(printTimeMinutes, resinMilliliters, material, 1, partsPerPlate);
    }

    internal static double ResinDirectCost(
        double printTimeMinutes,
        double resinMilliliters,
        MaterialInfo material,
        int quantity,
        int capacityPerPlate)
    {
        var normalizedQuantity = Math.Max(1, quantity);
        var nestedParts = Math.Max(1, capacityPerPlate);
        var occupiedPlates = (int)Math.Ceiling(normalizedQuantity / (double)nestedParts);
        var machineHourly = PricingCatalog.MachineHourly(PrintProcess.Resin);
        var overheadPerMinute = PricingCatalog.OverheadPerMinute(PrintProcess.Resin);
        var resinCost = resinMilliliters * material.CostPerUnit;
        var machineCost = (printTimeMinutes * (machineHourly / 60.0) * occupiedPlates) / normalizedQuantity;
        var overheadCost = (printTimeMinutes * overheadPerMinute * occupiedPlates) / normalizedQuantity;
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
        var marginBased = (complexityAdjustedCost / (1 - tier.TargetMargin)) * (1 - tier.BulkDiscount);
        var reservedPrice = marginBased * (1 + failureRate);
        var unitWithSetup = reservedPrice + (setupLabor / Math.Max(1, quantity));
        return unitWithSetup * paymentGrossUp;
    }

    private static double RoundUpToNearest(double value, double step) =>
        step <= 0 ? value : Math.Ceiling(value / step) * step;
}
