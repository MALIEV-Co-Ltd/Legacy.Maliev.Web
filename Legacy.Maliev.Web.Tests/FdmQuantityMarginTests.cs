// <copyright file="FdmQuantityMarginTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web.Application.Pricing;
    using Legacy.Maliev.Web.Application.Pricing.Simulation;
    using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Xunit;

    /// <summary>Guards the FDM quantity margin against a second quantity discount.</summary>
    public sealed class FdmQuantityMarginTests
    {
        /// <summary>Server physical pricing uses each quantity margin exactly once.</summary>
        [Theory]
        [InlineData(1, 0.50)]
        [InlineData(9, 0.50)]
        [InlineData(10, 0.35)]
        [InlineData(49, 0.35)]
        [InlineData(50, 0.25)]
        [InlineData(99, 0.25)]
        [InlineData(100, 0.20)]
        [InlineData(101, 0.20)]
        [InlineData(500, 0.18)]
        [InlineData(1000, 0.17)]
        [InlineData(5000, 0.16)]
        [InlineData(10000, 0.15)]
        public void ServerPhysicalQuote_UsesQuantityMarginOnce(int quantity, double margin)
        {
            MaterialInfo material = PricingCatalog.ResolveMaterial("PLA")!;
            ItemQuote quote = PricingEngine.QuoteFdmSimulation(Physical(material, quantity), material, quantity, 80);

            AssertMarginPrice(quote, material, quantity, margin);
        }

        /// <summary>The retained geometry estimate uses each quantity margin once.</summary>
        [Theory]
        [InlineData(1, 0.50)]
        [InlineData(9, 0.50)]
        [InlineData(10, 0.35)]
        [InlineData(49, 0.35)]
        [InlineData(50, 0.25)]
        [InlineData(99, 0.25)]
        [InlineData(100, 0.20)]
        [InlineData(101, 0.20)]
        public void GeometryEstimate_UsesQuantityMarginOnce(int quantity, double margin)
        {
            MaterialInfo material = PricingCatalog.ResolveMaterial("PLA")!;
            ItemQuote quote = PricingEngine.QuoteItem(Geometry(), material, quantity);

            Assert.Equal(margin, PricingCatalog.ResolveTier(quantity).TargetMargin);
            Assert.Equal(ExpectedGeometryUnitPrice(quote.DirectCostPerUnit, material, quantity), quote.UnitPrice);
            Assert.Equal(quote.UnitPrice * quantity, quote.Subtotal);
            JsonElement response = JsonSerializer.SerializeToElement(InstantQuotationCalculator.GetEstimate(
                material.Key, 40, 80000, 2000, null, null, "THB", quantity));
            Assert.True(response.GetProperty("success").GetBoolean());
            Assert.Equal(quote.UnitPrice, response.GetProperty("unitPrice").GetDouble());
            Assert.Equal(quote.Subtotal, response.GetProperty("subtotal").GetDouble());
        }

        /// <summary>Every displayed price break uses the corrected FDM policy.</summary>
        [Fact]
        public void BulkPriceTable_UsesQuantityMarginOnce()
        {
            MaterialInfo material = PricingCatalog.ResolveMaterial("PLA")!;
            ItemQuote quote = PricingEngine.QuoteItem(Geometry(), material, 100);

            foreach (BulkTier tier in quote.Tiers)
            {
                Assert.Equal(ExpectedGeometryUnitPrice(quote.DirectCostPerUnit, material, tier.MinQuantity), tier.UnitPrice);
            }

            Assert.Equal(100, Assert.Single(quote.Tiers.Where(tier => tier.Active)).MinQuantity);
        }

        /// <summary>Technical filament floors remain independent of the quantity margin.</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(49)]
        [InlineData(50)]
        [InlineData(99)]
        [InlineData(100)]
        [InlineData(101)]
        public void TechnicalMinimum_RemainsAppliedPerUnit(int quantity)
        {
            MaterialInfo material = PricingCatalog.ResolveMaterial("PA6")!;
            SimulationResult physical = SimulationResult.Create("test", new string('A', 64),
                new[] { new RoleDeposition(ExtrusionRole.OuterWall, material.Key, 1, 1) },
                new MotionLedger(new Dictionary<ExtrusionRole, double> { [ExtrusionRole.OuterWall] = 1 }, 0, 0, 0),
                Array.Empty<SupportRegionReport>(), Array.Empty<string>());
            ItemQuote quote = PricingEngine.QuoteFdmSimulation(physical, material, quantity, 1);

            AssertMarginPrice(quote, material, quantity, PricingCatalog.ResolveTier(quantity).TargetMargin);
            Assert.True(quote.TechnicalFilamentMinimumApplied);
            Assert.Equal(500, quote.TechnicalFilamentMinimumPrice);
            Assert.True(quote.UnitPrice >= 500);
        }

        /// <summary>Resin retains its existing commercial policy in this FDM-only correction.</summary>
        [Theory]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        public void ResinQuantityDiscount_RemainsUnchanged(int quantity)
        {
            MaterialInfo material = PricingCatalog.ResolveMaterial("M68")!;
            ItemQuote quote = PricingEngine.QuoteItem(new GeometryInput { HeightMm = 40, VolumeMm3 = 80000, FootprintMm2 = 2000 },
                material, quantity);
            DiscountTier tier = PricingCatalog.ResolveTier(quantity);

            Assert.Equal(ExpectedUnitPrice(quote.DirectCostPerUnit, material, quantity, tier.TargetMargin, tier.BulkDiscount), quote.UnitPrice);
        }

        private static GeometryInput Geometry() => new() { HeightMm = 40, VolumeMm3 = 80000, FootprintMm2 = 2000 };

        private static double ExpectedGeometryUnitPrice(double directCost, MaterialInfo material, int quantity)
        {
            double margin = PricingCatalog.ResolveTier(quantity).TargetMargin;
            double reserved = directCost / (1 - margin) * (1 + PricingCatalog.FailureReserveRate(material.Process));
            double setup = PricingCatalog.SetupHours(material.Process) * PricingCatalog.LaborRatePerHour / quantity;
            return PricingEngine.RoundUnitPrice((reserved + setup) / (1 - PricingCatalog.PaymentFeeRate));
        }

        private static void AssertMarginPrice(ItemQuote quote, MaterialInfo material, int quantity, double margin)
        {
            Assert.Equal(margin, PricingCatalog.ResolveTier(quantity).TargetMargin);
            Assert.Equal(ExpectedUnitPrice(quote.DirectCostPerUnit, material, quantity, margin), quote.UnitPrice);
            Assert.Equal(quote.UnitPrice * quantity, quote.Subtotal);
            Assert.Equal(0, quote.UnitPrice % 10);
            BulkTier tier = Assert.Single(quote.Tiers);
            Assert.Equal(quantity, tier.MinQuantity);
            Assert.True(tier.Active);
            Assert.Equal(quote.UnitPrice, tier.UnitPrice);
        }

        internal static double ExpectedUnitPrice(double directCost, MaterialInfo material, int quantity, double margin, double discount = 0)
        {
            AdditiveOrderCostBreakdown expected = AdditiveOrderCostCalculator.Calculate(new[]
            {
                new AdditiveOrderCostLine
                {
                    LineId = "expected",
                    Quantity = quantity,
                    DirectCostPerUnitThb = Convert.ToDecimal(directCost),
                    TargetMarginRate = Convert.ToDecimal(margin),
                    DiscountRate = Convert.ToDecimal(discount),
                    ReserveRate = Convert.ToDecimal(PricingCatalog.FailureReserveRate(material.Process)),
                    MinimumLineBaseThb = material.RequiresDrying ? 500m : 0m,
                    MinimumOrderPriceThb = Convert.ToDecimal(PricingCatalog.MinimumOrderPrice(material.Process)),
                },
            }, new AdditiveOrderCharges
            {
                SetupThb = Convert.ToDecimal(PricingCatalog.SetupHours(material.Process) * PricingCatalog.LaborRatePerHour),
                PackagingThb = Convert.ToDecimal(PricingCatalog.PackagingCost(material.Process)),
                RushRate = Convert.ToDecimal(PricingCatalog.RushSurcharge),
                PaymentFeeRate = Convert.ToDecimal(PricingCatalog.PaymentFeeRate),
                VatRate = Convert.ToDecimal(PricingCatalog.VatRate),
            });
            return PricingEngine.RoundUnitPrice(Convert.ToDouble(expected.CommercialSubtotalThb / quantity));
        }

        internal static SimulationResult Physical(MaterialInfo material, int quantity)
        {
            return SimulationResult.Create("test", new string('A', 64),
                new[] { new RoleDeposition(ExtrusionRole.OuterWall, material.Key, 80000d * quantity, 3600d * quantity) },
                new MotionLedger(new Dictionary<ExtrusionRole, double> { [ExtrusionRole.OuterWall] = 3600d * quantity }, 0, 0, 0),
                Array.Empty<SupportRegionReport>(), Array.Empty<string>());
        }
    }
}
