// <copyright file="AdditiveOrderCostCalculatorTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web.Application.Pricing;
    using System;
    using System.Linq;
    using Xunit;

    /// <summary>Freezes the approved Service Pricing workbook calculation sequence.</summary>
    public sealed class AdditiveOrderCostCalculatorTests
    {
        [Theory]
        [InlineData(1, 500, 498, 50, 627.90, 43.95, 675, 631.05)]
        [InlineData(2, 1000, 996, 100, 1194.91, 83.64, 1280, 1196.36)]
        [InlineData(5, 2500, 2490, 250, 2895.94, 202.72, 3100, 2897.28)]
        public void Calculate_PerUnitFloor_PrecedesAllCommercialStages(
            int quantity, decimal manufacturing, decimal adjustment, decimal reserve,
            decimal exVat, decimal vat, decimal gross, decimal commercial)
        {
            var line = CreateLine("floor", 1m);
            line.Quantity = quantity;
            line.MinimumLineBaseThb = 500m;
            line.MinimumOrderPriceThb = 300m;

            var result = AdditiveOrderCostCalculator.Calculate([line], WorkbookCharges);

            Assert.Equal(manufacturing, result.BaseOrderThb);
            Assert.Equal(adjustment, result.LineMinimumAdjustmentThb);
            Assert.Equal(0m, result.MinimumOrderSurchargeThb);
            Assert.Equal(reserve, result.ReserveThb);
            Assert.Equal(exVat, result.PriceBeforeVatThb);
            Assert.Equal(vat, result.VatThb);
            Assert.Equal(gross, result.TotalThb);
            Assert.Equal(commercial, result.CommercialSubtotalThb);
            Assert.Equal(commercial, Assert.Single(result.CommercialLineAllocations).TotalThb);
        }

        [Fact]
        public void Calculate_CommodityMinimum_SeparatesExplicitSurchargeFromCommercialSubtotal()
        {
            var line = CreateLine("commodity", 1m);
            line.MinimumOrderPriceThb = 300m;

            var result = AdditiveOrderCostCalculator.Calculate([line], WorkbookCharges);

            Assert.Equal(2m, result.UnroundedBaseThb);
            Assert.Equal(300m, result.BaseOrderThb);
            Assert.Equal(295m, result.MinimumOrderSurchargeThb);
            Assert.Equal(30m, result.ReserveThb);
            Assert.Equal(401.10m, result.PriceBeforeVatThb);
            Assert.Equal(28.08m, result.VatThb);
            Assert.Equal(430m, result.TotalThb);
            Assert.Equal(106.92m, result.CommercialSubtotalThb);
        }

        [Fact]
        public void Calculate_DeliveryCommercialSubtotal_SubtractsUnroundedVatNotRoundedVat()
        {
            var line = CreateLine("delivery", 250m);
            var charges = new AdditiveOrderCharges
            {
                SetupThb = 39.0625m,
                PackagingThb = 20m,
                DeliveryThb = 100.0051m,
                PaymentFeeRate = .03m,
                VatRate = .07m,
            };

            var result = AdditiveOrderCostCalculator.Calculate([line], charges);

            Assert.Equal(731m, result.PriceBeforeVatThb);
            Assert.Equal(51.17m, result.VatThb);
            Assert.Equal(785m, result.TotalThb);
            Assert.Equal(633.82m, decimal.Round(result.TotalThb - result.VatThb - result.DeliveryThb, 2, MidpointRounding.AwayFromZero));
            Assert.Equal(633.83m, result.CommercialSubtotalThb);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Calculate_FlooredHeterogeneousReserves_ConservesGrossAndCommercialAllocations(bool reverse)
        {
            var first = CreateLine("a", 1m);
            first.Quantity = 2;
            first.MinimumLineBaseThb = 500m;
            var second = CreateLine("b", 100m);
            second.ReserveRate = .20m;

            var result = AdditiveOrderCostCalculator.Calculate(reverse ? [second, first] : [first, second], WorkbookCharges);

            Assert.Equal(204m, result.UnroundedBaseThb);
            Assert.Equal(996m, result.LineMinimumAdjustmentThb);
            Assert.Equal(1200m, result.BaseOrderThb);
            Assert.Equal(140m, result.ReserveThb);
            Assert.Equal(1545m, result.TotalThb);
            Assert.Equal(1444.04m, result.CommercialSubtotalThb);
            Assert.Equal(new[] { "a", "b" }, result.LineAllocations.Select(line => line.LineId));
            Assert.Equal(new[] { 1287.50m, 257.50m }, result.LineAllocations.Select(line => line.TotalThb));
            Assert.Equal(new[] { "a", "b" }, result.CommercialLineAllocations.Select(line => line.LineId));
            Assert.Equal(new[] { 1203.37m, 240.67m }, result.CommercialLineAllocations.Select(line => line.TotalThb));
            Assert.Equal(result.TotalThb, result.LineAllocations.Sum(line => line.TotalThb));
            Assert.Equal(result.CommercialSubtotalThb, result.CommercialLineAllocations.Sum(line => line.TotalThb));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Calculate_EqualResidualShares_ConservesRoundedCommercialTotalWithStableTieBreak(bool reverse)
        {
            var lines = new[] { CreateLine("a", 100m), CreateLine("b", 100m), CreateLine("c", 100m) };
            var result = AdditiveOrderCostCalculator.Calculate(reverse ? lines.Reverse() : lines, WorkbookCharges);

            Assert.Equal(795m, result.TotalThb);
            Assert.Equal(new[] { 265m, 265m, 265m }, result.LineAllocations.Select(line => line.TotalThb));
            Assert.Equal(new[] { "a", "b", "c" }, result.CommercialLineAllocations.Select(line => line.LineId));
            Assert.Equal(new[] { 247.71m, 247.70m, 247.70m }, result.CommercialLineAllocations.Select(line => line.TotalThb));
            Assert.Equal(743.11m, result.CommercialSubtotalThb);
            Assert.Equal(result.TotalThb, result.LineAllocations.Sum(line => line.TotalThb));
            Assert.Equal(result.CommercialSubtotalThb, result.CommercialLineAllocations.Sum(line => line.TotalThb));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Calculate_NaturalHeterogeneousBases_AllocatesLiteralCommercialMoneyInStableOrder(bool reverse)
        {
            var first = CreateLine("a", 250m);
            first.Quantity = 2;
            var second = CreateLine("b", 100m);
            second.ReserveRate = .20m;

            var result = AdditiveOrderCostCalculator.Calculate(reverse ? [second, first] : [first, second], WorkbookCharges);

            Assert.Equal(1545m, result.TotalThb);
            Assert.Equal(new[] { 1287.50m, 257.50m }, result.LineAllocations.Select(line => line.TotalThb));
            Assert.Equal(new[] { "a", "b" }, result.CommercialLineAllocations.Select(line => line.LineId));
            Assert.Equal(new[] { 1203.37m, 240.67m }, result.CommercialLineAllocations.Select(line => line.TotalThb));
            Assert.Equal(1444.04m, result.CommercialSubtotalThb);
            Assert.Equal(result.TotalThb, result.LineAllocations.Sum(line => line.TotalThb));
            Assert.Equal(result.CommercialSubtotalThb, result.CommercialLineAllocations.Sum(line => line.TotalThb));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Calculate_HeterogeneousReserves_ConservesLiteralGrossAllocationsInStableOrder(bool reverse)
        {
            // Natural manufacturing bases1000/200 freeze allocation behaviour independently
            // of the separately tested technical floor; these are not slicer observations.
            var first = CreateLine("a", 250m);
            first.Quantity = 2;
            var second = CreateLine("b", 100m);
            second.ReserveRate = .20m;
            var result = AdditiveOrderCostCalculator.Calculate(reverse ? [second, first] : [first, second], WorkbookCharges);

            Assert.Equal(1200m, result.BaseOrderThb);
            Assert.Equal(140m, result.ReserveThb);
            Assert.Equal(1442.33m, result.PriceBeforeVatThb);
            Assert.Equal(100.96m, result.VatThb);
            Assert.Equal(1545m, result.TotalThb);
            Assert.Equal(new[] { "a", "b" }, result.LineAllocations.Select(line => line.LineId));
            Assert.Equal(new[] { 1287.50m, 257.50m }, result.LineAllocations.Select(line => line.TotalThb));
            Assert.Equal(result.TotalThb, result.LineAllocations.Sum(line => line.TotalThb));
        }

        [Fact]
        public void Calculate_DuplicateStableLineIds_RejectsRatherThanDoubleAllocatingResidualSatang()
        {
            Assert.Throws<ArgumentException>(() => AdditiveOrderCostCalculator.Calculate(
                [CreateLine("duplicate", 100m), CreateLine("duplicate", 100m), CreateLine("third", 100m)], WorkbookCharges));
        }

        private static readonly AdditiveOrderCharges WorkbookCharges = new AdditiveOrderCharges
        {
            SetupThb = 39.0625m,
            PackagingThb = 20m,
            DeliveryThb = 0m,
            RushRate = 0m,
            PaymentFeeRate = 0.03m,
            VatRate = 0.07m,
        };

        /// <summary>Reproduces the reviewed ASA workbook example exactly.</summary>
        [Fact]
        public void Calculate_AsaTenGramsFiftyOneMinutes_ReturnsSevenHundredFiftyFiveBaht()
        {
            AdditiveOrderCostBreakdown result = AdditiveOrderCostCalculator.Calculate(
                new[]
                {
                    new AdditiveOrderCostLine
                    {
                        LineId = "280te",
                        Quantity = 1,
                        DirectCostPerUnitThb = FdmDirectCost(10m, 0.86m, 51m),
                        ComplexityFactor = 1m,
                        TargetMarginRate = 0.50m,
                        DiscountRate = 0m,
                        ReserveRate = 0.10m,
                        MinimumOrderPriceThb = 300m,
                    },
                },
                WorkbookCharges);

            Assert.Equal("THB", result.Currency);
            Assert.Equal(565m, result.BaseOrderThb);
            Assert.Equal(56.50m, result.ReserveThb);
            Assert.Equal(701.61m, result.PriceBeforeVatThb);
            Assert.Equal(49.11m, result.VatThb);
            Assert.Equal(755m, result.TotalThb);
        }

        /// <summary>Reproduces the reviewed Body4 workbook total from frozen direct inputs.</summary>
        [Fact]
        public void Calculate_BodyFourReference_ReturnsThreeThousandFourHundredNinetyBaht()
        {
            AdditiveOrderCostBreakdown result = AdditiveOrderCostCalculator.Calculate(
                new[]
                {
                    new AdditiveOrderCostLine
                    {
                        LineId = "body4",
                        Quantity = 1,
                        DirectCostPerUnitThb = FdmDirectCost(207.39m, 0.83m, 229m),
                        ComplexityFactor = 1m,
                        TargetMarginRate = 0.50m,
                        DiscountRate = 0m,
                        ReserveRate = 0.10m,
                        MinimumOrderPriceThb = 300m,
                    },
                },
                WorkbookCharges);

            Assert.Equal(2820m, result.BaseOrderThb);
            Assert.Equal(282m, result.ReserveThb);
            Assert.Equal(3490m, result.TotalThb);
        }

        /// <summary>Ensures setup and packaging are charged once for a multi-line order.</summary>
        [Fact]
        public void Calculate_MultipleLines_AppliesOrderChargesOnce()
        {
            AdditiveOrderCostBreakdown combined = AdditiveOrderCostCalculator.Calculate(
                new[]
                {
                    CreateLine("a", 100m),
                    CreateLine("b", 100m),
                },
                WorkbookCharges);
            AdditiveOrderCostBreakdown single = AdditiveOrderCostCalculator.Calculate(
                new[] { CreateLine("a", 200m) },
                WorkbookCharges);

            Assert.Equal(single.TotalThb, combined.TotalThb);
            Assert.Equal(WorkbookCharges.SetupThb, combined.SetupThb);
            Assert.Equal(WorkbookCharges.PackagingThb, combined.PackagingThb);
            Assert.Equal(combined.TotalThb, combined.LineAllocations.Sum(allocation => allocation.TotalThb));
            Assert.Equal(new[] { "a", "b" }, combined.LineAllocations.Select(allocation => allocation.LineId));
        }

        /// <summary>Rejects rates that would make fee or margin gross-up undefined.</summary>
        [Fact]
        public void Calculate_InvalidPaymentFee_FailsClosed()
        {
            var charges = new AdditiveOrderCharges
            {
                PaymentFeeRate = 1m,
            };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AdditiveOrderCostCalculator.Calculate(new[] { CreateLine("a", 100m) }, charges));
        }

        private static AdditiveOrderCostLine CreateLine(string lineId, decimal directCost)
        {
            return new AdditiveOrderCostLine
            {
                LineId = lineId,
                Quantity = 1,
                DirectCostPerUnitThb = directCost,
                ComplexityFactor = 1m,
                TargetMarginRate = 0.50m,
                DiscountRate = 0m,
                ReserveRate = 0.10m,
                MinimumOrderPriceThb = 0m,
            };
        }

        private static decimal FdmDirectCost(decimal materialGrams, decimal costPerGram, decimal printMinutes)
        {
            const decimal monthlyFixedCost = 311097m;
            const decimal fdmAllocation = 0.70m;
            const decimal availablePrinterMinutes = 2m * 0.50m * 30m * 24m * 60m;
            const decimal machineHourly = 17m;
            decimal material = materialGrams * costPerGram * 1.10m;
            decimal overheadPerMinute = (monthlyFixedCost * fdmAllocation) / availablePrinterMinutes;
            decimal machineAndOverhead = printMinutes * ((machineHourly / 60m) + overheadPerMinute);
            return material + machineAndOverhead;
        }
    }
}
