// <copyright file="AdditiveGeometryValidatorTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web.Application.Pricing;
    using Xunit;

    /// <summary>Tests the server-side bounds applied before geometry can be priced.</summary>
    public sealed class AdditiveGeometryValidatorTests
    {
        [Fact]
        public void Validate_AcceptsFinitePositiveGeometryAndProfiles()
        {
            GeometryInput geometry = CreateValidGeometry();

            AdditiveGeometryValidationResult result = AdditiveGeometryValidator.Validate(geometry, 1);

            Assert.True(result.IsValid);
            Assert.Empty(result.ReasonCodes);
        }

        [Fact]
        public void Validate_AcceptsTenThousandPieces()
        {
            AdditiveGeometryValidationResult result = AdditiveGeometryValidator.Validate(CreateValidGeometry(), 10_000);

            Assert.True(result.IsValid);
            Assert.DoesNotContain("quantity_out_of_range", result.ReasonCodes);
        }

        [Theory]
        [InlineData(0, 1000, 100, "height_invalid")]
        [InlineData(100, 0, 100, "volume_invalid")]
        [InlineData(100, 1000, 0, "footprint_invalid")]
        [InlineData(1001, 1000, 100, "height_out_of_range")]
        public void Validate_RejectsInvalidOrUnboundedGeometry(double height, double volume, double footprint, string reason)
        {
            var geometry = new GeometryInput
            {
                HeightMm = height,
                VolumeMm3 = volume,
                FootprintMm2 = footprint,
                AreaProfileMm2 = new[] { 10d, 10d },
                PerimeterProfileMm = new[] { 12d, 12d },
                UnsupportedAreaProfileMm2 = new[] { 0d, 1d },
            };

            AdditiveGeometryValidationResult result = AdditiveGeometryValidator.Validate(geometry, 1);

            Assert.False(result.IsValid);
            Assert.Contains(reason, result.ReasonCodes);
        }

        [Fact]
        public void Validate_RejectsNonFiniteProfileAndUnsupportedQuantity()
        {
            var geometry = new GeometryInput
            {
                HeightMm = 100,
                VolumeMm3 = 1000,
                FootprintMm2 = 100,
                AreaProfileMm2 = new[] { 10d, double.NaN },
                PerimeterProfileMm = new[] { 12d, 12d },
                UnsupportedAreaProfileMm2 = new[] { 0d, 1d },
            };

            AdditiveGeometryValidationResult result = AdditiveGeometryValidator.Validate(geometry, 10_001);

            Assert.Contains("area_profile_invalid", result.ReasonCodes);
            Assert.Contains("quantity_out_of_range", result.ReasonCodes);
        }

        private static GeometryInput CreateValidGeometry()
        {
            return new GeometryInput
            {
                HeightMm = 100,
                VolumeMm3 = 1000,
                FootprintMm2 = 100,
                AreaProfileMm2 = new[] { 10d, 10d },
                PerimeterProfileMm = new[] { 12d, 12d },
                UnsupportedAreaProfileMm2 = new[] { 0d, 1d },
            };
        }
    }
}
