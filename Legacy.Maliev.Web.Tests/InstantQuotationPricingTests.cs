using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationPricingTests
{
    [Fact]
    public void CapacityAndOverhead_MatchAuditedWorkbook()
    {
        Assert.Equal(43_200, PricingCatalog.AvailablePrinterMinutes, 2);
        Assert.Equal(5.04092, PricingCatalog.OverheadPerMinute(PrintProcess.Fdm), 4);
        Assert.Equal(2.16040, PricingCatalog.OverheadPerMinute(PrintProcess.Resin), 4);
    }

    [Fact]
    public void FdmDirectCost_MatchesAuditedPlaExample()
    {
        var pla = PricingCatalog.ResolveMaterial("PLA");

        var directCost = PricingEngine.FdmDirectCost(300, 130, 0, pla!);

        Assert.Equal(1_715.97, directCost, 2);
    }

    [Fact]
    public void ResinDirectCost_PricesActualOccupiedPlateCount()
    {
        var material = PricingCatalog.ResolveMaterial("M68")!;
        var onePart = PricingEngine.ResinDirectCost(765, 40, material, 1, 10);
        var fullPlate = PricingEngine.ResinDirectCost(765, 40, material, 10, 10);
        var partialSecondPlate = PricingEngine.ResinDirectCost(765, 40, material, 11, 10);
        var sharedTimeCost = (765 * (PricingCatalog.MachineHourly(PrintProcess.Resin) / 60.0))
            + (765 * PricingCatalog.OverheadPerMinute(PrintProcess.Resin));
        var perPartCost = (40 * material.CostPerUnit)
            + (PricingCatalog.ResinPostProcessingHours * PricingCatalog.LaborRatePerHour)
            + PricingCatalog.ResinConsumablesPerPart;

        Assert.Equal(perPartCost + sharedTimeCost, onePart, 2);
        Assert.Equal(perPartCost + (sharedTimeCost / 10), fullPlate, 2);
        Assert.Equal(perPartCost + ((sharedTimeCost * 2) / 11), partialSecondPlate, 2);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20_000, 1)]
    [InlineData(900, 16)]
    [InlineData(25, 64)]
    public void ResinPlateNesting_MatchesLegacyBounds(double footprint, int expected)
    {
        Assert.Equal(expected, PricingCatalog.EstimatePartsPerPlate(footprint));
    }

    [Fact]
    public void OrderTotal_SumsItemsThenAddsShippingAndVatWithoutOrderLevelRounding()
    {
        var result = PricingEngine.QuoteOrder(
            [
                new OrderLine { Process = PrintProcess.Fdm, Subtotal = 1_200 },
                new OrderLine { Process = PrintProcess.Fdm, Subtotal = 1_800 },
            ],
            200);

        Assert.Equal(3_000, result.ItemsSubtotal, 2);
        Assert.Equal(3_000, result.Printing, 2);
        Assert.Equal(300, result.MinimumOrderPrice, 2);
        Assert.Equal(0, result.MinimumOrderSurcharge, 2);
        Assert.Equal(3_200, result.PriceBeforeVat, 2);
        Assert.Equal(224, result.Vat, 2);
        Assert.Equal(3_424, result.FinalOrderPrice, 2);
    }

    [Fact]
    public void EmptyAndBelowMinimumOrders_PreserveLegacyFloorRules()
    {
        Assert.Equal(0, PricingEngine.QuoteOrder([], 100).FinalOrderPrice, 2);
        Assert.Equal(
            300,
            PricingEngine.QuoteOrder([new OrderLine { Process = PrintProcess.Fdm, Subtotal = 50 }], 0).Printing,
            2);
        Assert.Equal(
            250,
            PricingEngine.QuoteOrder([new OrderLine { Process = PrintProcess.Fdm, Subtotal = 50 }], 0).MinimumOrderSurcharge,
            2);
        Assert.Equal(
            500,
            PricingEngine.QuoteOrder(
                [
                    new OrderLine { Process = PrintProcess.Fdm, Subtotal = 100 },
                    new OrderLine { Process = PrintProcess.Resin, Subtotal = 100 },
                ],
                0).Printing,
            2);
        Assert.Equal(
            300,
            PricingEngine.QuoteOrder(
                [
                    new OrderLine { Process = PrintProcess.Fdm, Subtotal = 100 },
                    new OrderLine { Process = PrintProcess.Resin, Subtotal = 100 },
                ],
                0).MinimumOrderSurcharge,
            2);
    }

    [Fact]
    public void FdmOverhang_InfluencesSupport()
    {
        var pla = PricingCatalog.ResolveMaterial("PLA")!;
        var growing = new GeometryInput
        {
            HeightMm = 40,
            VolumeMm3 = 245 * 40,
            FootprintMm2 = 500,
            AreaProfileMm2 = Enumerable.Range(0, 40).Select(index => 50.0 + (index * 10.0)).ToArray(),
            PerimeterProfileMm = Enumerable.Repeat(60.0, 40).ToArray(),
        };

        var growingEstimate = PrintTimeCalculator.EstimateFdm(growing, pla);

        Assert.True(growingEstimate.SupportGrams > 0);
    }

    [Fact]
    public void ResinMinutes_UsesApprovedExposureAndBottomLayerProfile()
    {
        var geometry = new GeometryInput { HeightMm = 10, VolumeMm3 = 5_000 };

        Assert.Equal(48.02, PrintTimeCalculator.ResinMinutes(geometry), 2);
    }

    [Fact]
    public void QuoteItem_AppliesManufacturingMarginBeforeOrderSetupLabor()
    {
        var pla = PricingCatalog.ResolveMaterial("PLA")!;
        var geometry = Geometry(area: 500, perimeter: 200);
        var item = PricingEngine.QuoteItem(geometry, pla, 1);
        var estimate = PrintTimeCalculator.EstimateFdm(geometry, pla);
        var core = PricingEngine.FdmDirectCost(
            estimate.PrintMinutes,
            estimate.MaterialGrams,
            estimate.SupportGrams,
            pla) * PricingCatalog.ComplexityFactor;
        var setup = PricingCatalog.SetupHours(PrintProcess.Fdm) * PricingCatalog.LaborRatePerHour;
        var failure = PricingCatalog.FailureReserveRate(PrintProcess.Fdm);
        var grossUp = 1 + (PricingCatalog.PaymentFeeRate / (1 - PricingCatalog.PaymentFeeRate));
        var expected = (((core / (1 - 0.50)) * (1 + failure)) + setup) * grossUp;

        Assert.Equal(core, item.DirectCostPerUnit, 6);
        Assert.Equal(PricingEngine.RoundUnitPrice(expected), item.UnitPrice, 2);
    }

    [Fact]
    public void FdmUnsupportedAreaProfile_OverridesAreaGrowthFallback()
    {
        var pla = PricingCatalog.ResolveMaterial("PLA")!;
        var vertical = new GeometryInput
        {
            HeightMm = 20,
            VolumeMm3 = 4_000,
            FootprintMm2 = 200,
            AreaProfileMm2 = Enumerable.Repeat(200.0, 20).ToArray(),
            PerimeterProfileMm = Enumerable.Repeat(60.0, 20).ToArray(),
            UnsupportedAreaProfileMm2 = new double[20],
        };
        var shifted = new GeometryInput
        {
            HeightMm = vertical.HeightMm,
            VolumeMm3 = vertical.VolumeMm3,
            FootprintMm2 = vertical.FootprintMm2,
            AreaProfileMm2 = vertical.AreaProfileMm2,
            PerimeterProfileMm = vertical.PerimeterProfileMm,
            UnsupportedAreaProfileMm2 = Enumerable.Repeat(50.0, 20).ToArray(),
        };

        var verticalEstimate = PrintTimeCalculator.EstimateFdm(vertical, pla);
        var shiftedEstimate = PrintTimeCalculator.EstimateFdm(shifted, pla);

        Assert.Equal(0, verticalEstimate.SupportGrams, 6);
        Assert.True(shiftedEstimate.SupportGrams > 0);
        Assert.True(shiftedEstimate.PrintMinutes > verticalEstimate.PrintMinutes);
        Assert.True(shiftedEstimate.MaterialGrams > verticalEstimate.MaterialGrams);
    }

    [Theory]
    [InlineData("Thailand", ShippingPricingState.DomesticPriced, "TH", 100)]
    [InlineData("TH", ShippingPricingState.DomesticPriced, "TH", 100)]
    [InlineData("THA", ShippingPricingState.DomesticPriced, "TH", 100)]
    [InlineData("Japan", ShippingPricingState.ToBeQuoted, "JAPAN", 0)]
    public void ShippingQuote_UsesDomesticRateOnlyForThailand(
        string destination,
        ShippingPricingState state,
        string code,
        decimal amount)
    {
        var quote = ShippingCalculator.Quote(destination, 100, 500);

        Assert.Equal(state, quote.State);
        Assert.Equal(code, quote.DestinationCountryCode);
        Assert.Equal(amount, quote.AmountThb);
    }

    [Fact]
    public void FrameChassisPetCfStrength_ReconcilesWithSlicerReference()
    {
        var geometry = new GeometryInput
        {
            HeightMm = 38.390871196985245,
            VolumeMm3 = 178530.49236773944,
            FootprintMm2 = 233.9974365234375 * 232,
            AreaProfileMm2 =
            [
                0, 17551.773853, 19161.533876, 19893.822115, 20085.778751, 19941.613221, 14806.574583, 12565.364414,
                8349.357194, 7342.723312, 6714.898193, 6121.038095, 5853.299626, 5667.736698, 5470.362105, 5346.442721,
                8177.103367, 8182.737152, 8225.400400, 5305.205763, 5198.868845, 5107.260981, 5027.985902, 5006.892132,
                2185.768256, 2272.230631, 2499.843106, 2665.878269, 2747.719832, 2774.832177, 2883.709203, 3086.103266,
                3084.004320, 2981.333435, 2800.463697, 2665.516360, 2742.246847, 2680.047373, 2544.904734, 2290.015480,
                1455.776635, 1442.380794, 1394.979348, 1317.197140, 1236.013520, 1140.381429, 1065.494998, 1017.513384,
                960.493879, 911.042259, 1109.770278, 1088.376991, 1036.540973, 952.270903, 823.271786, 534.129093,
                441.103249, 373.456519, 294.685835, 198.663806, 134.615571, 109.082193, 70.909672, 0,
            ],
            PerimeterProfileMm =
            [
                0, 1453.388769, 1506.781285, 1520.694922, 1549.603687, 1495.859757, 2011.403550, 2124.879975,
                2554.136848, 2400.164067, 2366.200644, 2095.272690, 2026.316035, 2019.251419, 1990.770790, 1890.048131,
                2323.416328, 2311.558553, 2315.133501, 1543.569742, 1530.910654, 1524.895103, 1521.545266, 1469.791693,
                953.618330, 1007.250804, 1087.284211, 1089.610808, 1077.762689, 1093.973148, 1077.454678, 1089.797184,
                1047.442610, 1000.533859, 898.553149, 824.798378, 790.077661, 770.406236, 741.194782, 679.884298,
                495.652331, 480.870610, 459.977118, 426.562018, 406.683837, 385.030470, 371.588030, 355.872612,
                333.446057, 300.878989, 303.153078, 264.298328, 250.396026, 239.756814, 223.511253, 168.121898,
                146.865616, 133.318407, 118.461265, 96.191378, 64.484509, 59.627171, 53.243145, 0,
            ],
        };

        var quote = PricingEngine.QuoteItem(
            geometry,
            PricingCatalog.ResolveMaterial("PET-CF")!,
            1,
            BuildPreference.Strength);

        Assert.InRange(quote.PrintTimeMinutesPerUnit, 420, 460);
        Assert.InRange(quote.MaterialPerUnit, 195, 230);
        Assert.InRange(quote.UnitPrice, 6_000, 6_600);
    }

    [Fact]
    public void QualityProfile_ReportsItsLongerPhysicalPrintTime()
    {
        var material = PricingCatalog.ResolveMaterial("PLA")!;
        var geometry = Geometry(500, 200);

        var standard = PricingEngine.QuoteItem(geometry, material, 1, BuildPreference.Standard);
        var quality = PricingEngine.QuoteItem(geometry, material, 1, BuildPreference.Quality);

        Assert.True(quality.PrintTimeMinutesPerUnit > standard.PrintTimeMinutesPerUnit);
        Assert.True(quality.UnitPrice > standard.UnitPrice);
    }

    [Fact]
    public void ItemQuote_RoundsCustomerUnitPriceBeforeCalculatingSubtotal()
    {
        var quote = PricingEngine.QuoteItem(
            new GeometryInput
            {
                HeightMm = 30,
                VolumeMm3 = 20_000,
                FootprintMm2 = 400,
                AreaProfileMm2 = Enumerable.Repeat(20_000.0 / 30, 40).ToArray(),
                PerimeterProfileMm = Enumerable.Repeat(80.0, 40).ToArray(),
            },
            PricingCatalog.ResolveMaterial("PLA")!,
            1);

        Assert.Equal([1, 10, 50, 100, 500, 1_000, 5_000, 10_000], quote.Tiers.Select(tier => tier.MinQuantity));
        Assert.True(quote.Tiers.Single(tier => tier.MinQuantity == 1).Active);
        Assert.Equal(quote.Subtotal, quote.UnitPrice, 2);
        Assert.Equal(0, quote.UnitPrice % 10, 2);
        Assert.All(quote.Tiers, tier => Assert.Equal(0, tier.UnitPrice % 10, 2));
    }

    [Fact]
    public void QuantityTier_PreservesLegacyFallbackAndBulkPriceReduction()
    {
        Assert.Equal(1, PricingCatalog.ResolveTier(0).MinQuantity);
        Assert.Equal(10, PricingCatalog.ResolveTier(49).MinQuantity);
        Assert.Equal(100, PricingCatalog.ResolveTier(5_000).MinQuantity);
        Assert.Equal(100, PricingCatalog.ResolveTier(10_000).MinQuantity);

        var geometry = new GeometryInput
        {
            HeightMm = 30,
            VolumeMm3 = 20_000,
            AreaProfileMm2 = Enumerable.Repeat(20_000.0 / 30, 40).ToArray(),
        };
        var material = PricingCatalog.ResolveMaterial("PLA")!;

        Assert.True(
            PricingEngine.QuoteItem(geometry, material, 100).UnitPrice
            < PricingEngine.QuoteItem(geometry, material, 1).UnitPrice);
    }

    [Fact]
    public void ItemQuote_QuantityAboveMaximumUsesTenThousandPiecePriceAndSubtotal()
    {
        var geometry = new GeometryInput
        {
            HeightMm = 30,
            VolumeMm3 = 20_000,
            FootprintMm2 = 400,
            AreaProfileMm2 = Enumerable.Repeat(20_000.0 / 30, 40).ToArray(),
        };
        var material = PricingCatalog.ResolveMaterial("PLA")!;

        var atMaximum = PricingEngine.QuoteItem(geometry, material, 10_000);
        var aboveMaximum = PricingEngine.QuoteItem(geometry, material, 10_001);

        Assert.Equal(atMaximum.UnitPrice, aboveMaximum.UnitPrice);
        Assert.Equal(atMaximum.Subtotal, aboveMaximum.Subtotal);
        Assert.Equal(10_000 * atMaximum.UnitPrice, atMaximum.Subtotal, 2);
        Assert.True(atMaximum.Tiers.Single(tier => tier.MinQuantity == 10_000).Active);
    }

    [Theory]
    [InlineData(89, 90)]
    [InlineData(2_720, 2_720)]
    [InlineData(2_801, 2_810)]
    public void UnitPrice_RoundsUpToTenBaht(double unitPrice, double expected)
    {
        Assert.Equal(expected, PricingEngine.RoundUnitPrice(unitPrice), 2);
    }

    [Fact]
    public void Shipping_PreservesMinimumWeightAndVolumetricRules()
    {
        Assert.Equal(100, ShippingCalculator.CustomerShippingThb(100, 500), 2);
        Assert.Equal(150, ShippingCalculator.CustomerShippingThb(4_800, 1_000), 2);
        Assert.Equal(270, ShippingCalculator.CustomerShippingThb(100, 50_000), 2);
        Assert.Equal(100, ShippingCalculator.CustomerShippingThb(-10, -20), 2);
        Assert.Equal(270, ShippingCalculator.CarrierRateThb(21), 2);
    }

    [Fact]
    public void Materials_AreCaseInsensitiveAndIncludeLegacyEngineeringOptions()
    {
        Assert.Equal(PrintProcess.Fdm, PricingCatalog.ResolveMaterial("pla")!.Process);
        Assert.Equal(PrintProcess.Resin, PricingCatalog.ResolveMaterial("F80")!.Process);
        Assert.Equal(3.80, PricingCatalog.ResolveMaterial("PC-ESD")!.CostPerUnit, 2);
        Assert.Null(PricingCatalog.ResolveMaterial("NOT-A-MATERIAL"));
    }

    private static GeometryInput Geometry(double area, double perimeter) => new()
    {
        HeightMm = 20,
        VolumeMm3 = area * 20,
        AreaProfileMm2 = Enumerable.Repeat(area, 20).ToArray(),
        PerimeterProfileMm = Enumerable.Repeat(perimeter, 20).ToArray(),
    };
}
