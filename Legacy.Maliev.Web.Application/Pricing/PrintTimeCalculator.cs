namespace Legacy.Maliev.Web.Application.Pricing;

public static class PrintTimeCalculator
{
    // Retained server policy: capped projected support and 15% handling waste.
    // Provisional pricing estimate, not a manufacturing receipt.
    internal static ResinEstimate EstimateResin(
        GeometryInput? geometry,
        ResinBuildProfileEvidence? profile)
    {
        const int maximumModelLayers = 20_000;
        const int maximumRaftLayers = 20_000;
        if (geometry is null || profile is null
            || !AdditiveGeometryValidator.Validate(geometry, 1).IsValid
            || !ResinBuildProfileEvidenceValidator.Assess(profile).IsReady
            || !ResinBuildProfileEvidenceValidator.HasCompleteSupportComposition(profile))
        {
            return new();
        }

        var layerHeight = (double)profile.LayerHeightMm!.Value;
        var modelLayerCount = Math.Ceiling(geometry.HeightMm / layerHeight);
        if (!double.IsFinite(modelLayerCount) || modelLayerCount < 1 || modelLayerCount > maximumModelLayers)
        {
            return new();
        }

        var layers = (int)modelLayerCount;
        var footprint = geometry.FootprintMm2;
        var unsupportedProfile = geometry.UnsupportedAreaProfileMm2 ?? [];
        var areaProfile = geometry.AreaProfileMm2 ?? [];
        var demands = new double[layers];
        var explicitDemand = unsupportedProfile.Count > 0;
        if (explicitDemand)
        {
            var samples = unsupportedProfile;
            for (var index = 1; index < samples.Count; index++)
            {
                var layer = (int)Math.Round(index / (double)(samples.Count - 1) * (layers - 1));
                if (layer > 0)
                {
                    demands[layer] = Math.Min(footprint, demands[layer] + samples[index]);
                }
            }
        }
        else
        {
            // Named provisional area-growth policy, not fabricated zero demand.
            var uniformArea = geometry.VolumeMm3 / geometry.HeightMm;
            var previousArea = InterpolateResinAreaProfile(areaProfile, .5 / layers, uniformArea);
            for (var layer = 1; layer < layers; layer++)
            {
                var area = InterpolateResinAreaProfile(areaProfile, (layer + .5) / layers, uniformArea);
                demands[layer] = Math.Min(footprint, Math.Max(0, area - previousArea));
                previousArea = area;
            }
        }

        // Every positive demand contributes an interval. Prefix accumulation applies
        // the same overlap cap as the source, in O(samples + modelLayers) work.
        var intervalDeltas = new double[layers + 1];
        for (var layer = 1; layer < layers; layer++)
        {
            var start = explicitDemand ? 0 : layer - (int)Math.Ceiling(layer * .5);
            intervalDeltas[start] += demands[layer];
            intervalDeltas[layer] -= demands[layer];
        }

        double activeArea = 0;
        double envelopeAreaSum = 0;
        for (var layer = 0; layer < layers; layer++)
        {
            activeArea += intervalDeltas[layer];
            envelopeAreaSum += Math.Min(footprint, Math.Max(0, activeArea));
        }

        var modelMl = geometry.VolumeMm3 / 1000;
        var supportMl = envelopeAreaSum * (double)profile.SupportEnvelopeDensity!.Value * layerHeight / 1000;
        var supported = supportMl > 0;
        var raftThickness = supported ? (double)profile.RaftThicknessMm!.Value : 0;
        var finalLayerCount = Math.Ceiling((geometry.HeightMm + raftThickness) / layerHeight);
        if (!double.IsFinite(finalLayerCount) || finalLayerCount < modelLayerCount
            || finalLayerCount - modelLayerCount > maximumRaftLayers)
        {
            return new();
        }

        var raftMl = supported ? footprint * (double)profile.RaftAreaRatio!.Value * raftThickness / 1000 : 0;
        var wasteMl = (modelMl + supportMl + raftMl) * .15;
        var totalMl = modelMl + supportMl + raftMl + wasteMl;
        var cycleSeconds = (double)profile.NormalExposureSeconds!.Value
            + (double)profile.RestSecondsPerLayer!.Value
            + (double)profile.LiftDistanceMm!.Value / (double)profile.LiftSpeedMmPerSecond!.Value
            + (double)profile.RetractDistanceMm!.Value / (double)profile.RetractSpeedMmPerSecond!.Value;
        var bottomExtra = (double)profile.BottomExposureSeconds!.Value - (double)profile.NormalExposureSeconds.Value;
        var minutes = (finalLayerCount * cycleSeconds
            + Math.Min(finalLayerCount, profile.BottomLayerCount!.Value) * bottomExtra) / 60;
        if (!double.IsFinite(totalMl) || !double.IsFinite(minutes) || totalMl <= 0 || minutes <= 0)
        {
            return new();
        }

        return new ResinEstimate
        {
            ModelResinMl = modelMl,
            SupportResinMl = supportMl,
            RaftResinMl = raftMl,
            WasteResinMl = wasteMl,
            TotalResinMl = totalMl,
            PrintMinutes = minutes,
        };
    }

    private static double InterpolateResinAreaProfile(IReadOnlyList<double> profile, double fraction, double fallback)
    {
        if (profile.Count == 0)
        {
            return fallback;
        }

        if (profile.Count == 1)
        {
            return profile[0];
        }

        var position = Math.Clamp(fraction, 0, 1) * (profile.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, profile.Count - 1);
        // Inputs are already nonnegative/finite. Preserve constant sampled areas
        // exactly so interpolation noise cannot invent support and a chargeable raft.
        return profile[lower] + (profile[upper] - profile[lower]) * (position - lower);
    }

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
