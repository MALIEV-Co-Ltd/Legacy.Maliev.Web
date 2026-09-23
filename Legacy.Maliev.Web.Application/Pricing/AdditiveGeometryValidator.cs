// <copyright file="AdditiveGeometryValidator.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Application.Pricing
{
    using System;
    using System.Collections.Generic;

    /// <summary>Result of validating browser-derived geometry before provisional pricing.</summary>
    public sealed class AdditiveGeometryValidationResult
    {
        /// <summary>Gets or sets a value indicating whether every supported bound passed.</summary>
        public bool IsValid { get; set; }

        /// <summary>Gets or sets stable machine-readable rejection reasons.</summary>
        public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    }

    /// <summary>Applies finite and resource bounds to additive geometry input.</summary>
    public static class AdditiveGeometryValidator
    {
        private const double MaximumDimensionMm = 1000d;
        private const double MaximumVolumeMm3 = 1_000_000_000d;
        private const double MaximumFootprintMm2 = 1_000_000d;
        private const int MaximumProfileSamples = 10_000;

        /// <summary>Validates geometry and quantity before any protected price is issued.</summary>
        public static AdditiveGeometryValidationResult Validate(GeometryInput geometry, int quantity)
        {
            var reasons = new List<string>();
            if (geometry == null)
            {
                reasons.Add("geometry_missing");
                return Result(reasons);
            }

            ValidateScalar(geometry.HeightMm, MaximumDimensionMm, "height", reasons);
            ValidateScalar(geometry.VolumeMm3, MaximumVolumeMm3, "volume", reasons);
            ValidateScalar(geometry.FootprintMm2, MaximumFootprintMm2, "footprint", reasons);
            ValidateProfile(geometry.AreaProfileMm2, "area_profile_invalid", reasons);
            ValidateProfile(geometry.PerimeterProfileMm, "perimeter_profile_invalid", reasons);
            ValidateProfile(geometry.UnsupportedAreaProfileMm2, "unsupported_area_profile_invalid", reasons);
            if (quantity < 1 || quantity > PricingCatalog.MaximumAdditiveQuantity)
            {
                reasons.Add("quantity_out_of_range");
            }

            return Result(reasons);
        }

        private static void ValidateScalar(double value, double maximum, string field, ICollection<string> reasons)
        {
            if (!double.IsFinite(value) || value <= 0d)
            {
                reasons.Add(field + "_invalid");
            }
            else if (value > maximum)
            {
                reasons.Add(field + "_out_of_range");
            }
        }

        private static void ValidateProfile(IReadOnlyList<double> values, string reason, ICollection<string> reasons)
        {
            if (values == null)
            {
                return;
            }

            // Legacy contracts normalize an omitted optional profile to an empty immutable list.
            // Treat that representation the same as the source application's null value.
            if (values.Count == 0)
            {
                return;
            }

            if (values.Count > MaximumProfileSamples)
            {
                reasons.Add(reason);
                return;
            }

            foreach (double value in values)
            {
                if (!double.IsFinite(value) || value < 0d || value > MaximumFootprintMm2)
                {
                    reasons.Add(reason);
                    return;
                }
            }
        }

        private static AdditiveGeometryValidationResult Result(IReadOnlyList<string> reasons)
        {
            return new AdditiveGeometryValidationResult
            {
                IsValid = reasons.Count == 0,
                ReasonCodes = reasons,
            };
        }
    }
}
