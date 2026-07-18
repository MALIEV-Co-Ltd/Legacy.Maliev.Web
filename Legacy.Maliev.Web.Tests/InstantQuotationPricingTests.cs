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
    public void ResinDirectCost_SharesPlateTimeButNotPerPartCosts()
    {
        var material = PricingCatalog.ResolveMaterial("M68")!;
        var single = PricingEngine.ResinDirectCost(765, 40, material, 1);
        var nested = PricingEngine.ResinDirectCost(765, 40, material, 10);
        var sharedTimeCost = (765 * (PricingCatalog.MachineHourly(PrintProcess.Resin) / 60.0))
            + (765 * PricingCatalog.OverheadPerMinute(PrintProcess.Resin));

        Assert.Equal(single - (0.9 * sharedTimeCost), nested, 2);
        Assert.True(nested < single);
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
    public void OrderTotal_SumsItemsThenAddsShippingVatAndFiveBahtRounding()
    {
        var result = PricingEngine.QuoteOrder(
            [
                new OrderLine { Process = PrintProcess.Fdm, Subtotal = 1_200 },
                new OrderLine { Process = PrintProcess.Fdm, Subtotal = 1_800 },
            ],
            200);

        Assert.Equal(3_000, result.ItemsSubtotal, 2);
        Assert.Equal(3_000, result.Printing, 2);
        Assert.Equal(3_200, result.PriceBeforeVat, 2);
        Assert.Equal(224, result.Vat, 2);
        Assert.Equal(3_425, result.FinalOrderPrice, 2);
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
            500,
            PricingEngine.QuoteOrder(
                [
                    new OrderLine { Process = PrintProcess.Fdm, Subtotal = 100 },
                    new OrderLine { Process = PrintProcess.Resin, Subtotal = 100 },
                ],
                0).Printing,
            2);
    }

    [Fact]
    public void FdmPerimeterAndOverhang_InfluenceTimeAndSupport()
    {
        var pla = PricingCatalog.ResolveMaterial("PLA")!;
        var compact = Geometry(area: 500, perimeter: 200);
        var elongated = Geometry(area: 500, perimeter: 800);
        var growing = new GeometryInput
        {
            HeightMm = 40,
            VolumeMm3 = 245 * 40,
            FootprintMm2 = 500,
            AreaProfileMm2 = Enumerable.Range(0, 40).Select(index => 50.0 + (index * 10.0)).ToArray(),
            PerimeterProfileMm = Enumerable.Repeat(60.0, 40).ToArray(),
        };

        var compactEstimate = PrintTimeCalculator.EstimateFdm(compact, pla);
        var elongatedEstimate = PrintTimeCalculator.EstimateFdm(elongated, pla);
        var growingEstimate = PrintTimeCalculator.EstimateFdm(growing, pla);

        Assert.True(elongatedEstimate.PrintMinutes > compactEstimate.PrintMinutes * 1.5);
        Assert.True(growingEstimate.SupportGrams > 0);
    }

    [Fact]
    public void ItemQuote_PreservesTierAndRoundedSubtotalBehavior()
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

        Assert.Equal(4, quote.Tiers.Count);
        Assert.True(quote.Tiers.Single(tier => tier.MinQuantity == 1).Active);
        Assert.Equal(quote.Subtotal, quote.UnitPrice, 2);
        Assert.Equal(0, quote.Subtotal % 100, 2);
    }

    [Fact]
    public void QuantityTier_PreservesLegacyFallbackAndBulkPriceReduction()
    {
        Assert.Equal(1, PricingCatalog.ResolveTier(0).MinQuantity);
        Assert.Equal(10, PricingCatalog.ResolveTier(49).MinQuantity);
        Assert.Equal(100, PricingCatalog.ResolveTier(5_000).MinQuantity);

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

    [Theory]
    [InlineData(89, 100)]
    [InlineData(2_720, 2_800)]
    [InlineData(2_800, 2_800)]
    public void LineSubtotal_RoundsUpToOneHundredBaht(double subtotal, double expected)
    {
        Assert.Equal(expected, PricingEngine.RoundLineItemSubtotal(subtotal), 2);
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
