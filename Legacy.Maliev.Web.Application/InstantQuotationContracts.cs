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
    {
        HeightMm = heightMm;
        VolumeMm3 = volumeMm3;
        FootprintMm2 = footprintMm2;
        AreaProfileMm2 = new ImmutableValueList<double>(areaProfileMm2);
        PerimeterProfileMm = new ImmutableValueList<double>(perimeterProfileMm);
        FacetCount = facetCount;
        BodyCount = bodyCount;
        IsManifold = isManifold;
    }

    public double HeightMm { get; }

    public double VolumeMm3 { get; }

    public double FootprintMm2 { get; }

    public IReadOnlyList<double> AreaProfileMm2 { get; }

    public IReadOnlyList<double> PerimeterProfileMm { get; }

    public int FacetCount { get; }

    public int BodyCount { get; }

    public bool IsManifold { get; }

    internal static AuthoritativeInstantQuotationGeometry FromSuccessfulUpload(InstantQuotationGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return new AuthoritativeInstantQuotationGeometry(
            geometry.HeightMm,
            geometry.VolumeMm3,
            geometry.FootprintMm2,
            geometry.AreaProfileMm2,
            geometry.PerimeterProfileMm,
            geometry.FacetCount,
            geometry.BodyCount,
            geometry.IsManifold);
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
