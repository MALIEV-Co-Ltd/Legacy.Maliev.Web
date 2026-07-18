namespace Legacy.Maliev.Web.Application.Pricing;

public static class PrintTimeCalculator
{
    public static FdmEstimate EstimateFdm(GeometryInput? geometry, MaterialInfo? material)
    {
        if (geometry is null || material is null || geometry.HeightMm <= 0)
        {
            return new FdmEstimate();
        }

        var height = geometry.HeightMm;
        var layerHeight = PricingCatalog.FdmLayerHeightMm;
        var layers = Math.Max(1, (int)Math.Ceiling(height / layerHeight));
        var lineWidth = PricingCatalog.FdmLineWidthMm;
        var walls = PricingCatalog.FdmWallCount;
        var infillDensity = PricingCatalog.FdmInfillDensity;
        var wallSpeed = PricingCatalog.FdmWallSpeedMmPerSec;
        var infillFlow = PricingCatalog.FdmFlowRateMm3PerSecond(material.FlowClass);
        var minLayer = material.MinLayerSeconds;
        var density = material.DensityGramsPerCm3;
        var uniformArea = Math.Abs(geometry.VolumeMm3) / height;

        double totalSeconds = 0;
        double depositedMm3 = 0;
        double supportMm3 = 0;
        double previousArea = -1;

        for (var layer = 0; layer < layers; layer++)
        {
            var fraction = (layer + 0.5) / layers;
            var area = InterpolateProfile(geometry.AreaProfileMm2, fraction, uniformArea);
            var fallbackPerimeter = 4.0 * Math.Sqrt(Math.Max(0, area));
            var perimeter = InterpolateProfile(geometry.PerimeterProfileMm, fraction, fallbackPerimeter);

            var wallCrossArea = Math.Min(area, perimeter * walls * lineWidth);
            var wallPathLength = wallCrossArea / lineWidth;
            var infillArea = Math.Max(0, area - wallCrossArea);
            var wallDeposit = wallCrossArea * layerHeight;
            var infillDeposit = infillArea * layerHeight * infillDensity;
            depositedMm3 += wallDeposit + infillDeposit;

            var wallTime = wallPathLength / wallSpeed;
            var infillTime = infillDeposit / infillFlow;
            totalSeconds += Math.Max(minLayer, wallTime + infillTime);

            if (previousArea >= 0)
            {
                var growth = Math.Max(0, area - previousArea);
                var heightFromBase = fraction * height;
                supportMm3 += growth
                    * heightFromBase
                    * PricingCatalog.FdmSupportReachFactor
                    * PricingCatalog.FdmSupportDensity;
            }

            previousArea = area;
        }

        var maximumSupport = geometry.FootprintMm2 > 0
            ? geometry.FootprintMm2 * height
            : Math.Abs(geometry.VolumeMm3) * 3.0;
        supportMm3 = Math.Min(supportMm3, maximumSupport);
        totalSeconds += supportMm3 / infillFlow;

        return new FdmEstimate
        {
            PrintMinutes = totalSeconds / 60.0,
            MaterialGrams = ((depositedMm3 + supportMm3) / 1_000.0) * density,
            SupportGrams = (supportMm3 / 1_000.0) * density,
        };
    }

    public static double ResinMinutes(GeometryInput? geometry)
    {
        if (geometry is null || geometry.HeightMm <= 0)
        {
            return 0;
        }

        var layers = Math.Max(1, (int)Math.Ceiling(geometry.HeightMm / PricingCatalog.ResinLayerHeightMm));
        return (layers * PricingCatalog.ResinPerLayerSeconds) / 60.0;
    }

    private static double InterpolateProfile(IReadOnlyList<double> profile, double fraction, double fallback)
    {
        if (profile.Count == 0)
        {
            return fallback;
        }

        if (profile.Count == 1)
        {
            return Math.Abs(profile[0]);
        }

        var clamped = Math.Clamp(fraction, 0, 1);
        var position = clamped * (profile.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, profile.Count - 1);
        var interpolation = position - lower;
        return (Math.Abs(profile[lower]) * (1 - interpolation))
            + (Math.Abs(profile[upper]) * interpolation);
    }
}
