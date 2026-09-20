namespace Legacy.Maliev.Web.Application.Pricing;

public static class PrintTimeCalculator
{
    public static FdmEstimate EstimateFdm(
        GeometryInput? geometry,
        MaterialInfo? material,
        BuildPreference buildPreference = BuildPreference.Standard) =>
        EstimateFdm(geometry, material, PricingCatalog.ResolveFdmBuildProfile(buildPreference));

    public static FdmEstimate EstimateFdm(
        GeometryInput? geometry,
        MaterialInfo? material,
        FdmBuildProfile? profile)
    {
        if (geometry is null || material is null || profile is null || geometry.HeightMm <= 0)
        {
            return new FdmEstimate();
        }

        var height = geometry.HeightMm;
        var layerHeight = profile.LayerHeightMm;
        var layers = Math.Max(1, (int)Math.Ceiling(height / layerHeight));
        var lineWidth = PricingCatalog.FdmLineWidthMm;
        var walls = profile.WallCount;
        var infillDensity = profile.InfillDensity;
        var wallSpeed = profile.WallSpeedMmPerSecond;
        var infillFlow = PricingCatalog.FdmFlowRateMm3PerSecond(material.FlowClass);
        var minLayer = material.MinLayerSeconds;
        var density = material.DensityGramsPerCm3;
        var uniformArea = Math.Abs(geometry.VolumeMm3) / height;

        double totalSeconds = 0;
        double depositedMm3 = 0;
        double supportMm3 = 0;
        var previousArea = -1d;
        var layerAreas = new double[layers];
        var layerPerimeters = new double[layers];

        for (var layer = 0; layer < layers; layer++)
        {
            var fraction = (layer + 0.5) / layers;
            layerAreas[layer] = InterpolateProfile(geometry.AreaProfileMm2, fraction, uniformArea);
            var fallbackPerimeter = 4.0 * Math.Sqrt(Math.Max(0, layerAreas[layer]));
            layerPerimeters[layer] = InterpolateProfile(
                geometry.PerimeterProfileMm,
                fraction,
                fallbackPerimeter);
        }

        var topSkinLayers = profile.TopSkinThicknessMm <= 0
            ? 0
            : Math.Max(1, (int)Math.Ceiling(profile.TopSkinThicknessMm / layerHeight));
        var bottomSkinLayers = profile.BottomSkinThicknessMm <= 0
            ? 0
            : Math.Max(1, (int)Math.Ceiling(profile.BottomSkinThicknessMm / layerHeight));
        var hasUnsupportedAreaProfile = geometry.UnsupportedAreaProfileMm2.Count > 0;

        for (var layer = 0; layer < layers; layer++)
        {
            var fraction = (layer + 0.5) / layers;
            var area = layerAreas[layer];
            var perimeter = layerPerimeters[layer];
            var wallCrossArea = Math.Min(area, perimeter * walls * lineWidth);
            var wallPathLength = wallCrossArea / lineWidth;
            var interiorArea = Math.Max(0, area - wallCrossArea);
            var lowerArea = bottomSkinLayers > 0 && layer >= bottomSkinLayers
                ? layerAreas[layer - bottomSkinLayers]
                : 0;
            var upperArea = topSkinLayers > 0 && layer + topSkinLayers < layers
                ? layerAreas[layer + topSkinLayers]
                : 0;
            var bottomSkinArea = bottomSkinLayers > 0 ? Math.Max(0, area - lowerArea) : 0;
            var topSkinArea = topSkinLayers > 0 ? Math.Max(0, area - upperArea) : 0;
            var solidSkinArea = Math.Min(interiorArea, bottomSkinArea + topSkinArea);
            var sparseInfillArea = Math.Max(0, interiorArea - solidSkinArea);
            var wallDeposit = wallCrossArea * layerHeight;
            var infillDeposit = (solidSkinArea + (sparseInfillArea * infillDensity)) * layerHeight;
            depositedMm3 += wallDeposit + infillDeposit;

            var wallTime = wallPathLength / wallSpeed;
            var infillTime = infillDeposit / infillFlow;
            totalSeconds += Math.Max(minLayer, wallTime + infillTime);

            if (previousArea >= 0)
            {
                var unsupportedArea = hasUnsupportedAreaProfile
                    ? InterpolateProfile(geometry.UnsupportedAreaProfileMm2, fraction, 0)
                    : Math.Max(0, area - previousArea);
                var heightFromBase = fraction * height;
                supportMm3 += unsupportedArea
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
        var bottomLayers = Math.Min(layers, PricingCatalog.ResinBottomLayerCount);
        var seconds = (layers * PricingCatalog.ResinPerLayerSeconds)
            + (bottomLayers * PricingCatalog.ResinBottomLayerExtraSeconds);
        return seconds / 60.0;
    }

    private static double InterpolateProfile(
        IReadOnlyList<double> profile,
        double fraction,
        double fallback)
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
