using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;
using System.Globalization;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Literal commercial-policy expectations, not manufacturing calibration evidence.</summary>
public sealed class AdditiveCommercialEconomicsTests
{
    [Fact]
    public void QuoteOrder_DryingFloorLine_UsesRoundedPublicMoneyNotStandaloneCalculatorGross()
    {
        var item = PricingEngine.QuoteFdmSimulation(Ledger("PA6", 2, 1, 0, 1), PricingCatalog.ResolveMaterial("PA6")!, 2, 1);
        var order = PricingEngine.QuoteOrder([new OrderLine { Process = PrintProcess.Fdm, Subtotal = item.Subtotal }], 100);

        Assert.Equal(600, item.UnitPrice, 2);
        Assert.Equal(1200, item.Subtotal, 2);
        Assert.Equal(1200, order.ItemsSubtotal, 2);
        Assert.Equal(0, order.MinimumOrderSurcharge, 2);
        Assert.Equal(1300, order.PriceBeforeVat, 2);
        Assert.Equal(91, order.Vat, 2);
        Assert.Equal(1391, order.FinalOrderPrice, 2);
    }

    [Fact]
    public void QuoteOrder_CommodityLine_KeepsPublicMinimumDeliveryAndTaxSeparate()
    {
        var item = PricingEngine.QuoteFdmSimulation(Ledger("PLA", 1, 1, 0, 1), PricingCatalog.ResolveMaterial("PLA")!, 1, 1);
        var order = PricingEngine.QuoteOrder([new OrderLine { Process = PrintProcess.Fdm, Subtotal = item.Subtotal }], 100);

        Assert.Equal(110, item.UnitPrice, 2);
        Assert.Equal(110, order.ItemsSubtotal, 2);
        Assert.Equal(190, order.MinimumOrderSurcharge, 2);
        Assert.Equal(100, order.ShippingCost, 2);
        Assert.Equal(400, order.PriceBeforeVat, 2);
        Assert.Equal(28, order.Vat, 2);
        Assert.Equal(428, order.FinalOrderPrice, 2);
    }

    [Theory]
    [InlineData(1, 640, 640)]
    [InlineData(2, 600, 1200)]
    [InlineData(5, 580, 2900)]
    public void QuotePhysical_DryingRequiredLedger_AppliesFloorPerOrderedUnitBeforeCommercialStages(
        int quantity, double unitPrice, double subtotal)
    {
        var physical = Ledger("PA6", quantity, modelMm3: 1, supportMm3: 0, seconds: 1);

        var quote = PricingEngine.QuoteFdmSimulation(physical, PricingCatalog.ResolveMaterial("PA6")!, quantity, 1);

        Assert.Equal(unitPrice, quote.UnitPrice, 2);
        Assert.Equal(subtotal, quote.Subtotal, 2);
        Assert.True(quote.TechnicalFilamentMinimumApplied);
        Assert.Equal(500, quote.TechnicalFilamentMinimumPrice, 2);
        Assert.True(quote.TechnicalFilamentMinimumAdjustment > 0);
        Assert.Equal(unitPrice * quantity, quote.Subtotal, 2);
    }

    [Theory]
    [InlineData("TPU", 1.43, 1.05, true)]
    [InlineData("PC-FR", 1.19, 2.15, true)]
    [InlineData("ASA-CF", 1.02, 1.834, true)]
    [InlineData("PETG-CF", 1.30, 1.55, true)]
    [InlineData("PET-CF", 1.34, 1.70, true)]
    [InlineData("HIPS", 1.05, 0.925, false)]
    [InlineData("PA12", 1.012, 2.034, true)]
    public void QuotePhysical_QualifiedMaterialComposition_MatchesActualAdmittedProfile(
        string materialKey, double density, double cost, bool drying)
    {
        var material = PricingCatalog.ResolveMaterial(materialKey)!;
        var profiles = FdmRuntimeProfileCatalog.LoadEmbedded();
        using var manifest = JsonDocument.Parse(profiles.BrowserManifestJson);
        var composition = manifest.RootElement.GetProperty("materials").GetProperty(materialKey);
        Assert.Equal(density, double.Parse(composition.GetProperty("densityGramsPerCm3").GetString()!, CultureInfo.InvariantCulture), 6);
        Assert.True(composition.GetProperty("automaticPricingEligible").GetBoolean());
        Assert.Equal(64, composition.GetProperty("sourceArtifactSha256").GetString()!.Length);
        Assert.True(profiles.TryResolveTrustedProfile(materialKey, BuildPreference.Standard, out var profile));
        Assert.NotNull(profile);
        Assert.Equal(materialKey, profile.MaterialId);
        Assert.Equal(density, profile.MaterialDensityGramsPerCm3, 6);
        Assert.NotEqual(PricingCatalog.AdditivePricingPolicyVersion, profiles.ProfileVersion);
        var quote = PricingEngine.QuoteFdmSimulation(Ledger(materialKey, 1, 800, 200, 60), material, 1, 1);

        Assert.Equal(density, quote.MaterialPerUnit, 6);
        Assert.Equal(density, quote.WeightGramsPerUnit, 6);
        Assert.Equal(cost, material.CostPerUnit, 6);
        Assert.Equal(drying, material.RequiresDrying);
        // Total deposition already includes support; only removal labour uses support separately.
        var expectedDirectCost = (density * cost * 1.10)
            + (17d / 60 + PricingCatalog.OverheadPerMinute(PrintProcess.Fdm))
            + (density * 0.2 * 8 / 3600 * 156.25);
        Assert.Equal(expectedDirectCost, quote.DirectCostPerUnit, 6);
    }

    [Theory]
    [InlineData(499, .20, .15, 1570, 783430)]
    [InlineData(500, .18, .15, 1530, 765000)]
    [InlineData(999, .18, .15, 1530, 1528470)]
    [InlineData(1000, .17, .15, 1520, 1520000)]
    [InlineData(4999, .17, .15, 1520, 7598480)]
    [InlineData(5000, .16, .15, 1500, 7500000)]
    [InlineData(9999, .16, .15, 1500, 14998500)]
    [InlineData(10000, .15, .15, 1480, 14800000)]
    public void QuotePhysical_QuantityAtCommercialBoundary_UsesContributionProtectedEconomics(
        int quantity, double margin, double discount, double expectedUnitPrice, double expectedSubtotal)
    {
        // A synthetic non-drying material avoids the floor masking a tier regression.
        var material = new MaterialInfo
        {
            Key = "synthetic",
            DisplayName = "Synthetic policy fixture",
            Process = PrintProcess.Fdm,
            DensityGramsPerCm3 = 1,
            CostPerUnit = 1000,
        };
        var quote = PricingEngine.QuoteFdmSimulation(Ledger("synthetic", quantity, 1000, 0, 60), material, quantity, 1);

        var tier = PricingCatalog.ResolveTier(quantity);
        Assert.Equal(margin, tier.TargetMargin, 6);
        // The shared table retains the resin discount; FDM applies only its quantity margin.
        Assert.Equal(discount, tier.BulkDiscount, 6);
        // Independent synthetic direct cost:1000*1.10 +17/60 +311097*.70/43200.
        // Literal goldens use the reviewed decimal commercial stages, not a production helper.
        // Round the margin base to 5 THB, add reserve/setup/packaging, then payment/VAT;
        // round gross to 5 THB and commercial unit price to 10 THB without a second discount.
        Assert.Equal(1105.3242569444444, quote.DirectCostPerUnit, 6);
        Assert.Equal(expectedUnitPrice, quote.UnitPrice, 2);
        Assert.Equal(expectedSubtotal, quote.Subtotal, 2);
        Assert.True(quote.UnitPrice > quote.DirectCostPerUnit);
        Assert.Equal(quote.UnitPrice * quantity, quote.Subtotal, 2);
    }

    private static SimulationResult Ledger(string material, int quantity, double modelMm3, double supportMm3, double seconds) =>
        SimulationResult.Create(FdmSimulationEngine.AnalysisVersion, new string('B', 64),
        [
            new RoleDeposition(ExtrusionRole.OuterWall, material, modelMm3 * quantity, seconds * quantity),
            new RoleDeposition(ExtrusionRole.SupportBody, material, supportMm3 * quantity, 0),
        ],
        new MotionLedger(new Dictionary<ExtrusionRole, double> { [ExtrusionRole.OuterWall] = seconds * quantity }, 0, 0, 0),
        [], []);
}
