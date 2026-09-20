// <copyright file="AdditiveLeadTimeCalculator.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>One verified line entering the capacity calculation.</summary>
    public sealed class AdditiveLeadTimeLine
    {
        /// <summary>Gets or sets occupied machine minutes per unit.</summary>
        public decimal MinutesPerUnit { get; set; }

        /// <summary>Gets or sets ordered units.</summary>
        public int Quantity { get; set; }
    }

    /// <summary>Customer-facing production-day range.</summary>
    public sealed class AdditiveLeadTimeRange
    {
        /// <summary>Gets or sets the capacity-derived lower bound.</summary>
        public int MinimumDays { get; set; }

        /// <summary>Gets or sets the buffered promise upper bound.</summary>
        public int MaximumDays { get; set; }
    }

    /// <summary>Calculates lead time for printers operating 24 hours daily with no live queue.</summary>
    public static class AdditiveLeadTimeCalculator
    {
        private const decimal MinutesPerDay = 1440m;
        private const int PromiseBufferDays = 2;

        /// <summary>Calculates occupied days and the approved two-day customer buffer.</summary>
        public static AdditiveLeadTimeRange Calculate(IEnumerable<AdditiveLeadTimeLine> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);
            AdditiveLeadTimeLine[] values = lines.ToArray();
            if (values.Length == 0 || values.Any(line => line == null || line.MinutesPerUnit < 0m || line.Quantity < 1))
            {
                throw new ArgumentException("Lead-time lines must be complete and non-negative.", nameof(lines));
            }

            decimal totalMinutes = values.Sum(line => line.MinutesPerUnit * line.Quantity);
            int minimumDays = Math.Max(1, (int)decimal.Ceiling(totalMinutes / MinutesPerDay));
            return new AdditiveLeadTimeRange
            {
                MinimumDays = minimumDays,
                MaximumDays = minimumDays + PromiseBufferDays,
            };
        }
    }
}
