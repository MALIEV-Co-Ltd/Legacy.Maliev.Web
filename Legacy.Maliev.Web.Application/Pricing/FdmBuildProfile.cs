namespace Legacy.Maliev.Web.Application.Pricing;

/// <summary>Immutable physical slicer inputs for one versioned FDM build preference.</summary>
public sealed class FdmBuildProfile
{
    public FdmBuildProfile(
        BuildPreference preference,
        double layerHeightMm,
        int wallCount,
        double infillDensity,
        double topSkinThicknessMm,
        double bottomSkinThicknessMm,
        double wallSpeedMmPerSecond)
    {
        if (!double.IsFinite(layerHeightMm) || layerHeightMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layerHeightMm));
        }

        if (wallCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wallCount));
        }

        if (!double.IsFinite(infillDensity) || infillDensity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(infillDensity));
        }

        if (!double.IsFinite(topSkinThicknessMm) || topSkinThicknessMm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topSkinThicknessMm));
        }

        if (!double.IsFinite(bottomSkinThicknessMm) || bottomSkinThicknessMm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bottomSkinThicknessMm));
        }

        if (!double.IsFinite(wallSpeedMmPerSecond) || wallSpeedMmPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wallSpeedMmPerSecond));
        }

        Preference = preference;
        LayerHeightMm = layerHeightMm;
        WallCount = wallCount;
        InfillDensity = infillDensity;
        TopSkinThicknessMm = topSkinThicknessMm;
        BottomSkinThicknessMm = bottomSkinThicknessMm;
        WallSpeedMmPerSecond = wallSpeedMmPerSecond;
    }

    public BuildPreference Preference { get; }

    public double LayerHeightMm { get; }

    public int WallCount { get; }

    public double InfillDensity { get; }

    public double TopSkinThicknessMm { get; }

    public double BottomSkinThicknessMm { get; }

    public double WallSpeedMmPerSecond { get; }
}
