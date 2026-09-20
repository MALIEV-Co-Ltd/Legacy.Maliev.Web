// <copyright file="AdditiveOrderCostCalculator.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Reproduces the approved Service Pricing workbook's order-level commercial sequence.
    /// This calculator intentionally accepts physical direct cost as an input so pricing-policy
    /// parity can be tested independently from analytical or slicer estimation accuracy.
    /// </summary>
    internal static class AdditiveOrderCostCalculator
    {
        /// <summary>Calculates one canonical THB order breakdown using decimal arithmetic.</summary>
        /// <param name="lines">Physical-cost lines in stable line-ID order.</param>
        /// <param name="charges">Order-level commercial charges and tax rates.</param>
        /// <returns>An auditable order-cost breakdown.</returns>
        internal static AdditiveOrderCostBreakdown Calculate(
            IEnumerable<AdditiveOrderCostLine> lines,
            AdditiveOrderCharges charges)
        {
            if (lines == null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            if (charges == null)
            {
                throw new ArgumentNullException(nameof(charges));
            }

            AdditiveOrderCostLine[] orderedLines = lines
                .OrderBy(line => line.LineId, StringComparer.Ordinal)
                .ToArray();
            if (orderedLines.Length == 0)
            {
                throw new ArgumentException("At least one pricing line is required.", nameof(lines));
            }

            ValidateCharges(charges);
            foreach (AdditiveOrderCostLine line in orderedLines)
            {
                ValidateLine(line);
            }

            decimal unroundedBase = orderedLines.Sum(CalculateUnroundedLineBase);
            decimal minimumOrderPrice = orderedLines.Max(line => line.MinimumOrderPriceThb);
            decimal baseOrder = RoundUp(Math.Max(minimumOrderPrice, unroundedBase), 5m);
            decimal reserve = AllocateReserve(orderedLines, unroundedBase, baseOrder);
            decimal preRush = baseOrder
                + charges.SetupThb
                + reserve
                + charges.PackagingThb
                + charges.DeliveryThb;
            decimal afterRush = preRush * (1m + charges.RushRate);
            decimal exVat = afterRush / (1m - charges.PaymentFeeRate);
            decimal paymentFee = exVat - afterRush;
            decimal vat = exVat * charges.VatRate;
            decimal unroundedGross = exVat + vat;
            decimal gross = RoundUp(unroundedGross, 5m);
            IReadOnlyList<AdditiveOrderCostAllocation> allocations = AllocateLineTotals(orderedLines, unroundedBase, gross);

            return new AdditiveOrderCostBreakdown
            {
                Currency = "THB",
                UnroundedBaseThb = RoundCurrency(unroundedBase),
                BaseOrderThb = baseOrder,
                SetupThb = charges.SetupThb,
                ReserveThb = RoundCurrency(reserve),
                PackagingThb = charges.PackagingThb,
                DeliveryThb = charges.DeliveryThb,
                RushThb = RoundCurrency(afterRush - preRush),
                PaymentFeeThb = RoundCurrency(paymentFee),
                PriceBeforeVatThb = RoundCurrency(exVat),
                VatThb = RoundCurrency(vat),
                RoundingAdjustmentThb = RoundCurrency(gross - unroundedGross),
                TotalThb = gross,
                LineAllocations = allocations,
            };
        }

        private static IReadOnlyList<AdditiveOrderCostAllocation> AllocateLineTotals(
            IReadOnlyList<AdditiveOrderCostLine> lines,
            decimal unroundedBase,
            decimal total)
        {
            var candidates = lines.Select(line =>
            {
                decimal share = unroundedBase == 0m
                    ? 1m / lines.Count
                    : CalculateUnroundedLineBase(line) / unroundedBase;
                decimal raw = total * share;
                decimal floor = decimal.Floor(raw * 100m) / 100m;
                return new { line.LineId, Raw = raw, Floor = floor, Fraction = raw - floor };
            }).ToList();
            int remainingSatang = decimal.ToInt32(decimal.Round((total - candidates.Sum(item => item.Floor)) * 100m, 0));
            string[] incrementIds = candidates
                .OrderByDescending(item => item.Fraction)
                .ThenBy(item => item.LineId, StringComparer.Ordinal)
                .Take(remainingSatang)
                .Select(item => item.LineId)
                .ToArray();

            return candidates
                .OrderBy(item => item.LineId, StringComparer.Ordinal)
                .Select(item => new AdditiveOrderCostAllocation
                {
                    LineId = item.LineId,
                    TotalThb = item.Floor + (incrementIds.Contains(item.LineId, StringComparer.Ordinal) ? 0.01m : 0m),
                })
                .ToArray();
        }

        private static decimal CalculateUnroundedLineBase(AdditiveOrderCostLine line)
        {
            decimal marginPrice = line.DirectCostPerUnitThb
                * line.ComplexityFactor
                / (1m - line.TargetMarginRate);
            return marginPrice * (1m - line.DiscountRate) * line.Quantity;
        }

        private static decimal AllocateReserve(
            IReadOnlyCollection<AdditiveOrderCostLine> lines,
            decimal unroundedBase,
            decimal baseOrder)
        {
            if (unroundedBase == 0m)
            {
                decimal equalAllocation = baseOrder / lines.Count;
                return lines.Sum(line => equalAllocation * line.ReserveRate);
            }

            return lines.Sum(line =>
            {
                decimal share = CalculateUnroundedLineBase(line) / unroundedBase;
                return baseOrder * share * line.ReserveRate;
            });
        }

        private static void ValidateLine(AdditiveOrderCostLine line)
        {
            if (line == null)
            {
                throw new ArgumentException("Pricing lines cannot contain null entries.", nameof(line));
            }

            if (string.IsNullOrWhiteSpace(line.LineId))
            {
                throw new ArgumentException("Every pricing line requires a stable line ID.", nameof(line));
            }

            if (line.Quantity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "Line quantity must be at least one.");
            }

            RequireNonNegative(line.DirectCostPerUnitThb, nameof(line.DirectCostPerUnitThb));
            RequireNonNegative(line.MinimumOrderPriceThb, nameof(line.MinimumOrderPriceThb));
            RequireRate(line.DiscountRate, nameof(line.DiscountRate), allowOne: true);
            RequireRate(line.TargetMarginRate, nameof(line.TargetMarginRate), allowOne: false);
            RequireRate(line.ReserveRate, nameof(line.ReserveRate), allowOne: true);
            if (line.ComplexityFactor <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "Complexity factor must be positive.");
            }
        }

        private static void ValidateCharges(AdditiveOrderCharges charges)
        {
            RequireNonNegative(charges.SetupThb, nameof(charges.SetupThb));
            RequireNonNegative(charges.PackagingThb, nameof(charges.PackagingThb));
            RequireNonNegative(charges.DeliveryThb, nameof(charges.DeliveryThb));
            RequireRate(charges.RushRate, nameof(charges.RushRate), allowOne: true);
            RequireRate(charges.PaymentFeeRate, nameof(charges.PaymentFeeRate), allowOne: false);
            RequireRate(charges.VatRate, nameof(charges.VatRate), allowOne: true);
        }

        private static void RequireNonNegative(decimal value, string name)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(name, "Value cannot be negative.");
            }
        }

        private static void RequireRate(decimal value, string name, bool allowOne)
        {
            if (value < 0m || (allowOne ? value > 1m : value >= 1m))
            {
                throw new ArgumentOutOfRangeException(name, "Rate is outside its supported range.");
            }
        }

        private static decimal RoundCurrency(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal RoundUp(decimal value, decimal step)
        {
            return decimal.Ceiling(value / step) * step;
        }
    }

    /// <summary>One physical-cost line entering the order-level pricing policy.</summary>
    internal sealed class AdditiveOrderCostLine
    {
        /// <summary>Gets or sets the stable line identifier used for deterministic allocation.</summary>
        internal string LineId { get; set; } = string.Empty;

        /// <summary>Gets or sets the ordered quantity.</summary>
        internal int Quantity { get; set; }

        /// <summary>Gets or sets direct physical cost per unit before margin and discount.</summary>
        internal decimal DirectCostPerUnitThb { get; set; }

        /// <summary>Gets or sets the complexity multiplier.</summary>
        internal decimal ComplexityFactor { get; set; } = 1m;

        /// <summary>Gets or sets the target gross-margin rate.</summary>
        internal decimal TargetMarginRate { get; set; }

        /// <summary>Gets or sets the quantity-tier discount rate.</summary>
        internal decimal DiscountRate { get; set; }

        /// <summary>Gets or sets the named commercial reprint-reserve rate.</summary>
        internal decimal ReserveRate { get; set; }

        /// <summary>Gets or sets the minimum order price contributed by this process.</summary>
        internal decimal MinimumOrderPriceThb { get; set; }
    }

    /// <summary>Order-level charges applied once after the manufacturing base.</summary>
    internal sealed class AdditiveOrderCharges
    {
        /// <summary>Gets or sets setup/admin cost applied once per order.</summary>
        internal decimal SetupThb { get; set; }

        /// <summary>Gets or sets packaging cost applied once per order.</summary>
        internal decimal PackagingThb { get; set; }

        /// <summary>Gets or sets separately priced delivery.</summary>
        internal decimal DeliveryThb { get; set; }

        /// <summary>Gets or sets the rush surcharge rate.</summary>
        internal decimal RushRate { get; set; }

        /// <summary>Gets or sets the payment fee gross-up rate.</summary>
        internal decimal PaymentFeeRate { get; set; }

        /// <summary>Gets or sets the VAT rate.</summary>
        internal decimal VatRate { get; set; }
    }

    /// <summary>Auditable THB order-cost output matching the workbook's commercial stages.</summary>
    internal sealed class AdditiveOrderCostBreakdown
    {
        /// <summary>Gets or sets the canonical currency code.</summary>
        internal string Currency { get; set; } = string.Empty;

        /// <summary>Gets or sets manufacturing base before minimum and THB 5 ceiling.</summary>
        internal decimal UnroundedBaseThb { get; set; }

        /// <summary>Gets or sets the minimum-adjusted manufacturing base.</summary>
        internal decimal BaseOrderThb { get; set; }

        /// <summary>Gets or sets once-per-order setup.</summary>
        internal decimal SetupThb { get; set; }

        /// <summary>Gets or sets allocated commercial reprint reserve.</summary>
        internal decimal ReserveThb { get; set; }

        /// <summary>Gets or sets once-per-order packaging.</summary>
        internal decimal PackagingThb { get; set; }

        /// <summary>Gets or sets delivery.</summary>
        internal decimal DeliveryThb { get; set; }

        /// <summary>Gets or sets rush surcharge.</summary>
        internal decimal RushThb { get; set; }

        /// <summary>Gets or sets payment-fee gross-up amount.</summary>
        internal decimal PaymentFeeThb { get; set; }

        /// <summary>Gets or sets ex-VAT order price.</summary>
        internal decimal PriceBeforeVatThb { get; set; }

        /// <summary>Gets or sets VAT.</summary>
        internal decimal VatThb { get; set; }

        /// <summary>Gets or sets final THB 5 ceiling adjustment.</summary>
        internal decimal RoundingAdjustmentThb { get; set; }

        /// <summary>Gets or sets gross customer total.</summary>
        internal decimal TotalThb { get; set; }

        /// <summary>Gets or sets deterministic line allocations that reconcile to the total.</summary>
        internal IReadOnlyList<AdditiveOrderCostAllocation> LineAllocations { get; set; } = Array.Empty<AdditiveOrderCostAllocation>();
    }

    /// <summary>One deterministic satang-accurate allocation of the order total.</summary>
    internal sealed class AdditiveOrderCostAllocation
    {
        /// <summary>Gets or sets the stable line id.</summary>
        internal string LineId { get; set; } = string.Empty;

        /// <summary>Gets or sets the total allocated to the line.</summary>
        internal decimal TotalThb { get; set; }
    }
}
