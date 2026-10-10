namespace Legacy.Maliev.Web.Application.Pricing;

using Legacy.Maliev.Web.Application.Pricing.Simulation;

public static class PricingEngine
{
    /// <summary>Applies the existing commercial policy to a server-verified, quantity-bound FDM ledger.</summary>
    public static ItemQuote QuoteFdmSimulation(
        SimulationResult physical,
        MaterialInfo material,
        int quantity,
        double boundingCm3PerUnit)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(material);
        if (material.Process != PrintProcess.Fdm
            || quantity < 1 || quantity > PricingCatalog.MaximumAdditiveQuantity
            || !double.IsFinite(boundingCm3PerUnit) || boundingCm3PerUnit <= 0
            || physical.Diagnostics.Count != 0
            || !double.IsFinite(physical.TotalDepositedMm3) || physical.TotalDepositedMm3 <= 0
            || !double.IsFinite(physical.Motion.TotalSeconds) || physical.Motion.TotalSeconds <= 0)
        {
            throw new ArgumentException("The physical FDM evidence is not eligible for a price.", nameof(physical));
        }

        double grams = physical.TotalDepositedMm3 * material.DensityGramsPerCm3 / (1_000d * quantity);
        double supportGrams = physical.SupportDepositedMm3 * material.DensityGramsPerCm3 / (1_000d * quantity);
        double minutes = physical.Motion.TotalSeconds / (60d * quantity);
        double directCost = FdmDirectCost(minutes, grams, supportGrams, material) * PricingCatalog.ComplexityFactor;
        if (!double.IsFinite(directCost) || directCost <= 0)
        {
            throw new ArgumentException("The physical FDM cost is not finite and positive.", nameof(physical));
        }

        var price = CalculateStandalonePrice(material, directCost, quantity, PricingCatalog.ResolveTier(quantity));
        var unitPrice = RoundUnitPrice(Convert.ToDouble(price.CommercialSubtotalThb / quantity));
        var minimumAdjustment = material.RequiresDrying ? Convert.ToDouble(price.LineMinimumAdjustmentThb) : 0;
        return new ItemQuote
        {
            Process = PrintProcess.Fdm,
            DirectCostPerUnit = directCost,
            PrintTimeMinutesPerUnit = minutes,
            MaterialPerUnit = grams,
            WeightGramsPerUnit = grams,
            BoundingCm3PerUnit = boundingCm3PerUnit,
            UnitPrice = unitPrice,
            Subtotal = unitPrice * quantity,
            TechnicalFilamentMinimumApplied = minimumAdjustment > 0,
            TechnicalFilamentMinimumPrice = material.RequiresDrying ? PricingCatalog.TechnicalFilamentMinimumPrice : 0,
            TechnicalFilamentMinimumAdjustment = minimumAdjustment,
            // Other quantities require their own physical simulation; do not fabricate tier prices.
            Tiers = [new BulkTier { MinQuantity = quantity, UnitPrice = unitPrice, Active = true }],
        };
    }

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
        ResinQuoteProfileIdentity? resinProfile = null;

        if (material.Process == PrintProcess.Resin)
        {
            var profile = ResinBuildProfileCatalog.ResolveProvisionalSupportProfile();
            var estimate = PrintTimeCalculator.EstimateResin(geometry, profile);
            printTime = estimate.PrintMinutes;
            var resinMilliliters = estimate.TotalResinMl;
            if (!IsAdmittedResinValue(printTime) || printTime > 20_160
                || !IsAdmittedResinValue(resinMilliliters))
            {
                throw new ArgumentException("The provisional resin estimate is unavailable or outside pricing bounds.", nameof(geometry));
            }

            resinProfile = new(profile.ProfileVersion!, ResinBuildProfileComposition.CreateSha256(profile));
            materialPerUnit = resinMilliliters;
            weightGrams = resinMilliliters * ShippingCalculator.ResinDensityGramsPerMl;
            var capacityPerPlate = PricingCatalog.EstimatePartsPerPlate(geometry.FootprintMm2);
            complexityAdjustedCostAtQuantity = pricedQuantity =>
            {
                var cost = ResinDirectCost(printTime, resinMilliliters, material,
                    pricedQuantity, capacityPerPlate) * PricingCatalog.ComplexityFactor;
                if (!IsAdmittedResinValue(cost)
                    || cost >= (double)decimal.MaxValue / (PricingCatalog.MaximumAdditiveQuantity * 10d))
                {
                    throw new ArgumentException("The resin direct cost is outside monetary bounds.", nameof(material));
                }

                return cost;
            };
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
            UnitPrice = material.Process == PrintProcess.Resin
                ? RoundUnitPrice(Convert.ToDouble(CalculateStandalonePrice(material,
                    complexityAdjustedCostAtQuantity(bulkQuantity), bulkQuantity,
                    PricingCatalog.ResolveTier(bulkQuantity)).CommercialSubtotalThb / bulkQuantity))
                : ApplyTechnicalFilamentMinimumUnitPrice(
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
        }).ToList();
        if (material.Process == PrintProcess.Resin)
        {
            tiers = CollapseRepeatedTerminalTiers(tiers, normalizedQuantity);
        }

        var complexityAdjustedCost = complexityAdjustedCostAtQuantity(normalizedQuantity);
        var unroundedUnitPrice = material.Process == PrintProcess.Resin ? 0 : AllInUnitPrice(
            complexityAdjustedCost,
            setupLabor,
            failureRate,
            paymentGrossUp,
            activeTier,
            normalizedQuantity);
        var calculatedUnitPrice = material.Process == PrintProcess.Resin
            ? RoundUnitPrice(Convert.ToDouble(CalculateStandalonePrice(material,
                complexityAdjustedCost, normalizedQuantity, activeTier).CommercialSubtotalThb / normalizedQuantity))
            : RoundUnitPrice(unroundedUnitPrice);
        var unitPrice = ApplyTechnicalFilamentMinimumUnitPrice(calculatedUnitPrice, normalizedQuantity, material);
        var calculatedSubtotal = calculatedUnitPrice * normalizedQuantity;
        var subtotal = unitPrice * normalizedQuantity;
        var boundingCm3 = (Math.Abs(geometry.FootprintMm2) * Math.Abs(geometry.HeightMm)) / 1_000.0;

        return new ItemQuote
        {
            ResinProfile = resinProfile,
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

    private static bool IsAdmittedResinValue(double value) =>
        double.IsFinite(value) && value > 0 && value < (double)decimal.MaxValue;

    private static List<BulkTier> CollapseRepeatedTerminalTiers(List<BulkTier> tiers, int quantity)
    {
        var keepCount = tiers.Count;
        while (keepCount > 1 && tiers[keepCount - 1].UnitPrice == tiers[keepCount - 2].UnitPrice)
        {
            keepCount--;
        }

        var visible = tiers.Take(keepCount).ToList();
        var activeQuantity = PricingCatalog.ResolveBulkQuoteQuantity(quantity);
        if (visible.All(tier => tier.MinQuantity != activeQuantity))
        {
            visible.Add(tiers.Single(tier => tier.MinQuantity == activeQuantity));
        }

        return visible;
    }

    private static AdditiveOrderCostBreakdown CalculateStandalonePrice(
        MaterialInfo material, double directCostPerUnit, int quantity, DiscountTier tier)
    {
        return AdditiveOrderCostCalculator.Calculate(
            [new AdditiveOrderCostLine
            {
                LineId = "standalone",
                Quantity = quantity,
                DirectCostPerUnitThb = Convert.ToDecimal(directCostPerUnit),
                TargetMarginRate = Convert.ToDecimal(tier.TargetMargin),
                DiscountRate = material.Process == PrintProcess.Fdm ? 0m : Convert.ToDecimal(tier.BulkDiscount),
                ReserveRate = Convert.ToDecimal(PricingCatalog.FailureReserveRate(material.Process)),
                MinimumLineBaseThb = material.RequiresDrying ? Convert.ToDecimal(PricingCatalog.TechnicalFilamentMinimumPrice) : 0m,
                MinimumOrderPriceThb = Convert.ToDecimal(PricingCatalog.MinimumOrderPrice(material.Process)),
            }],
            new AdditiveOrderCharges
            {
                SetupThb = Convert.ToDecimal(PricingCatalog.SetupHours(material.Process) * PricingCatalog.LaborRatePerHour),
                PackagingThb = Convert.ToDecimal(PricingCatalog.PackagingCost(material.Process)),
                RushRate = Convert.ToDecimal(PricingCatalog.RushSurcharge),
                PaymentFeeRate = Convert.ToDecimal(PricingCatalog.PaymentFeeRate),
                VatRate = Convert.ToDecimal(PricingCatalog.VatRate),
            });
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
        var machineHourly = PricingCatalog.MachineHourly(PrintProcess.Fdm)
            + (material.RequiresHeatedEnclosure ? PricingCatalog.FdmHeatedEnclosureEnergyHourly : 0);
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
        var marginBased = complexityAdjustedCost / (1 - tier.TargetMargin);
        var reservedPrice = marginBased * (1 + failureRate);
        var unitWithSetup = reservedPrice + (setupLabor / Math.Max(1, quantity));
        return unitWithSetup * paymentGrossUp;
    }

    private static double RoundUpToNearest(double value, double step) =>
        step <= 0 ? value : Math.Ceiling(value / step) * step;
}
