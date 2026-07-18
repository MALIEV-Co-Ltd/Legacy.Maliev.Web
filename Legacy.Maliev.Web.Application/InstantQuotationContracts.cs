using System.Text.Json.Serialization;
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
    [JsonConstructor]
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
        AreaProfileMm2 = areaProfileMm2;
        PerimeterProfileMm = perimeterProfileMm;
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
            geometry.AreaProfileMm2.ToArray(),
            geometry.PerimeterProfileMm.ToArray(),
            geometry.FacetCount,
            geometry.BodyCount,
            geometry.IsManifold);
    }
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
