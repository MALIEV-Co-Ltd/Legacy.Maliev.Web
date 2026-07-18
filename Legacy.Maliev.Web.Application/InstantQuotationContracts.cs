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
    bool IsManifold,
    bool IsFileServiceAuthoritative);

public sealed record InstantQuotationPartConfiguration(
    string MaterialKey,
    string Color,
    int Quantity);

public sealed record InstantQuotationPart(
    Guid PartId,
    string DisplayFileName,
    string UploadReference,
    InstantQuotationGeometry Geometry,
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
