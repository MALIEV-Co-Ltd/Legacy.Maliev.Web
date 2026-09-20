// <copyright file="AdditiveLeadTimeCalculatorTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web.Application.Pricing;
    using Xunit;

    /// <summary>Tests the owner-approved 24-hour, normally-idle capacity promise.</summary>
    public sealed class AdditiveLeadTimeCalculatorTests
    {
        [Theory]
        [InlineData(51, 1, 1, 3)]
        [InlineData(1441, 1, 2, 4)]
        [InlineData(800, 2, 2, 4)]
        public void Calculate_AddsTwoDayCustomerBuffer(decimal minutesPerUnit, int quantity, int expectedMinimum, int expectedMaximum)
        {
            AdditiveLeadTimeRange result = AdditiveLeadTimeCalculator.Calculate(
                new[] { new AdditiveLeadTimeLine { MinutesPerUnit = minutesPerUnit, Quantity = quantity } });

            Assert.Equal(expectedMinimum, result.MinimumDays);
            Assert.Equal(expectedMaximum, result.MaximumDays);
        }
    }
}
