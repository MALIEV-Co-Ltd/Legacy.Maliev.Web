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
