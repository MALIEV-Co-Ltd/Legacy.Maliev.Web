using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Application;

public sealed record InstantQuotationGeometry(
    double HeightMm,
    double VolumeMm3,
    double FootprintMm2,
    IReadOnlyList<double> AreaProfileMm2,
    IReadOnlyList<double> PerimeterProfileMm,
    int FacetCount,
    int BodyCount,
    bool IsManifold);

public sealed record InstantQuotationGeometryClaim(
    int Version,
    string Sha256,
    double DimensionXmm,
    double DimensionYmm,
    double DimensionZmm,
    double VolumeMm3,
    double SurfaceAreaMm2,
    IReadOnlyList<double>? AreaProfileMm2,
    IReadOnlyList<double>? PerimeterProfileMm,
    int FacetCount,
    int BodyCount,
    bool TopologyChecked,
    bool NonWatertight,
    bool NonManifold,
    double MinThicknessMm)
{
    public bool IsValid()
    {
        var footprint = DimensionXmm * DimensionYmm;
        var boundingVolume = footprint * DimensionZmm;
        var expectedProfileCount = FacetCount > 250_000 ? 24 : 64;
        var profilesValid = NonWatertight
            && AreaProfileMm2 is null
            && PerimeterProfileMm is null
            || AreaProfileMm2 is { Count: > 0 }
                && PerimeterProfileMm is { Count: > 0 }
                && AreaProfileMm2.Count == expectedProfileCount
                && PerimeterProfileMm.Count == expectedProfileCount
                && AreaProfileMm2.All(value => double.IsFinite(value) && value >= 0)
                && PerimeterProfileMm.All(value => double.IsFinite(value) && value >= 0)
                && AreaProfileMm2.Any(value => value > 0)
                && PerimeterProfileMm.Any(value => value > 0);
        var topologyValid = FacetCount <= 200_000
            ? TopologyChecked
            : !TopologyChecked && BodyCount == 1 && !NonWatertight && !NonManifold;
        return Version == 1
            && IsLowerSha256(Sha256)
            && IsFinitePositive(DimensionXmm)
            && IsFinitePositive(DimensionYmm)
            && IsFinitePositive(DimensionZmm)
            && IsFinitePositive(footprint)
            && IsFinitePositive(boundingVolume)
            && IsFinitePositive(VolumeMm3)
            && VolumeMm3 <= boundingVolume * 1.02
            && IsFinitePositive(SurfaceAreaMm2)
            && profilesValid
            && FacetCount > 0
            && BodyCount > 0
            && BodyCount <= FacetCount
            && topologyValid
            && double.IsFinite(MinThicknessMm)
            && MinThicknessMm >= 0;
    }

    public InstantQuotationGeometryClaim Snapshot() => this with
    {
        AreaProfileMm2 = AreaProfileMm2 is null ? null : new ImmutableValueList<double>(AreaProfileMm2),
        PerimeterProfileMm = PerimeterProfileMm is null ? null : new ImmutableValueList<double>(PerimeterProfileMm),
    };

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

    private static bool IsLowerSha256(string value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class AuthoritativeInstantQuotationGeometry
{
    internal AuthoritativeInstantQuotationGeometry(
        double heightMm,
        double volumeMm3,
        double footprintMm2,
        IReadOnlyList<double> areaProfileMm2,
        IReadOnlyList<double> perimeterProfileMm,
        int facetCount,
        int bodyCount,
        bool isManifold)
        : this(
            0,
            string.Empty,
            Math.Sqrt(footprintMm2),
            Math.Sqrt(footprintMm2),
            heightMm,
            volumeMm3,
            0,
            areaProfileMm2,
            perimeterProfileMm,
            facetCount,
            bodyCount,
            true,
            !isManifold,
            !isManifold,
            0)
    {
    }

    private AuthoritativeInstantQuotationGeometry(
        int claimVersion,
        string sha256,
        double dimensionXmm,
        double dimensionYmm,
        double dimensionZmm,
        double volumeMm3,
        double surfaceAreaMm2,
        IReadOnlyList<double> areaProfileMm2,
        IReadOnlyList<double> perimeterProfileMm,
        int facetCount,
        int bodyCount,
        bool topologyChecked,
        bool nonWatertight,
        bool nonManifold,
        double minThicknessMm)
    {
        ClaimVersion = claimVersion;
        Sha256 = sha256;
        DimensionXmm = dimensionXmm;
        DimensionYmm = dimensionYmm;
        DimensionZmm = dimensionZmm;
        HeightMm = dimensionZmm;
        VolumeMm3 = volumeMm3;
        SurfaceAreaMm2 = surfaceAreaMm2;
        FootprintMm2 = dimensionXmm * dimensionYmm;
        AreaProfileMm2 = new ImmutableValueList<double>(areaProfileMm2);
        PerimeterProfileMm = new ImmutableValueList<double>(perimeterProfileMm);
        FacetCount = facetCount;
        BodyCount = bodyCount;
        TopologyChecked = topologyChecked;
        NonWatertight = nonWatertight;
        NonManifold = nonManifold;
        MinThicknessMm = minThicknessMm;
    }

    internal int ClaimVersion { get; }

    internal string Sha256 { get; }

    public double DimensionXmm { get; }

    public double DimensionYmm { get; }

    public double DimensionZmm { get; }

    public double HeightMm { get; }

    public double VolumeMm3 { get; }

    public double SurfaceAreaMm2 { get; }

    public double FootprintMm2 { get; }

    public IReadOnlyList<double> AreaProfileMm2 { get; }

    public IReadOnlyList<double> PerimeterProfileMm { get; }

    public int FacetCount { get; }

    public int BodyCount { get; }

    public bool TopologyChecked { get; }

    public bool NonWatertight { get; }

    public bool NonManifold { get; }

    public double MinThicknessMm { get; }

    public bool IsManifold => TopologyChecked && !NonWatertight && !NonManifold;

    internal static AuthoritativeInstantQuotationGeometry? FromCompletedLegacyUpload(
        InstantQuotationUploadResult upload,
        InstantQuotationGeometryClaim claim)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(claim);
        if (upload.ServiceStatus is not InstantQuotationServiceStatus.Available
            || upload.AuthorizationStatus is not InstantQuotationAuthorizationStatus.Authorized
            || upload.Status is not InstantQuotationOperationStatus.Succeeded
            || upload.ProblemCategory is not InstantQuotationProblemCategory.None
            || upload.UploadReference is null
            || !string.Equals(upload.ContentSha256, claim.Sha256, StringComparison.Ordinal)
            || !claim.IsValid())
        {
            return null;
        }

        return new AuthoritativeInstantQuotationGeometry(
            claim.Version,
            claim.Sha256,
            claim.DimensionXmm,
            claim.DimensionYmm,
            claim.DimensionZmm,
            claim.VolumeMm3,
            claim.SurfaceAreaMm2,
            claim.AreaProfileMm2 ?? [],
            claim.PerimeterProfileMm ?? [],
            claim.FacetCount,
            claim.BodyCount,
            claim.TopologyChecked,
            claim.NonWatertight,
            claim.NonManifold,
            claim.MinThicknessMm);
    }

    internal static AuthoritativeInstantQuotationGeometry RestoreFromProtectedSession(
        double heightMm,
        double volumeMm3,
        double footprintMm2,
        IReadOnlyList<double> areaProfileMm2,
        IReadOnlyList<double> perimeterProfileMm,
        int facetCount,
        int bodyCount,
        bool isManifold) => new(
            heightMm,
            volumeMm3,
            footprintMm2,
            areaProfileMm2,
            perimeterProfileMm,
            facetCount,
            bodyCount,
            isManifold);

    internal static AuthoritativeInstantQuotationGeometry RestoreFromProtectedSession(
        int claimVersion,
        string sha256,
        double dimensionXmm,
        double dimensionYmm,
        double dimensionZmm,
        double volumeMm3,
        double surfaceAreaMm2,
        IReadOnlyList<double> areaProfileMm2,
        IReadOnlyList<double> perimeterProfileMm,
        int facetCount,
        int bodyCount,
        bool topologyChecked,
        bool nonWatertight,
        bool nonManifold,
        double minThicknessMm) => new(
            claimVersion,
            sha256,
            dimensionXmm,
            dimensionYmm,
            dimensionZmm,
            volumeMm3,
            surfaceAreaMm2,
            areaProfileMm2,
            perimeterProfileMm,
            facetCount,
            bodyCount,
            topologyChecked,
            nonWatertight,
            nonManifold,
            minThicknessMm);
}

public sealed record InstantQuotationPartConfiguration(
    string MaterialKey,
    string Color,
    int Quantity);

public sealed record InstantQuotationPart(
    Guid PartId,
    string DisplayFileName,
    InstantQuotationUploadReference UploadReference,
    AuthoritativeInstantQuotationGeometry Geometry,
    InstantQuotationPartConfiguration Configuration);

public sealed record InstantQuotationPartQuote(
    Guid PartId,
    string MaterialKey,
    string Color,
    int Quantity,
    PrintProcess Process,
    double PrintTimeMinutesPerUnit,
    double MaterialPerUnit,
    double WeightGramsPerUnit,
    double BoundingCm3PerUnit,
    double UnitPrice,
    double Subtotal,
    IReadOnlyList<BulkTier> Tiers);

public sealed record InstantQuotationOrderQuote(
    IReadOnlyList<InstantQuotationPartQuote> Parts,
    double ItemsSubtotal,
    double Printing,
    double ShippingCost,
    double PriceBeforeVat,
    double Vat,
    double FinalOrderPrice,
    int LeadTimeMinimumDays,
    int LeadTimeMaximumDays);

public sealed record InstantQuotationOrderState(IReadOnlyList<InstantQuotationPart> Parts);

public sealed record InstantQuotationSessionState(
    string SessionId,
    string SubmissionId,
    InstantQuotationOrderState RequestState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public IReadOnlyList<InstantQuotationPart> Parts => RequestState.Parts;
}

public interface IInstantQuotationSessionStore
{
    Task<InstantQuotationSessionState> CreateAsync(
        string? ownerIdentity,
        InstantQuotationOrderState requestState,
        CancellationToken cancellationToken);

    Task<InstantQuotationSessionState?> GetAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken);

    Task<bool> PutAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken);
}

internal sealed class ImmutableValueList<T>(IEnumerable<T> values) : IReadOnlyList<T>
{
    private readonly T[] values = values.ToArray();

    public int Count => values.Length;

    public T this[int index] => values[index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => values.GetEnumerator();
}
