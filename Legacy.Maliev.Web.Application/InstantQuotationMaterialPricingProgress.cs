namespace Legacy.Maliev.Web.Application;

/// <summary>Ephemeral display status; never an order quote or submission authority.</summary>
public enum InstantQuotationMaterialPricingStatus
{
    Pending,
    Completed,
    Unavailable,
}

/// <summary>Material display only. A finite unit amount belongs only to Completed.</summary>
public sealed record InstantQuotationMaterialPricingProgress(
    Guid PartId,
    string MaterialKey,
    InstantQuotationMaterialPricingStatus Status,
    double? UnitPrice);
