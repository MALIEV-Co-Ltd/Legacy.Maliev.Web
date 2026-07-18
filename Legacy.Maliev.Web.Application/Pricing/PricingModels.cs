namespace Legacy.Maliev.Web.Application.Pricing;

public enum PrintProcess
{
    Fdm,
    Resin,
}

public enum FdmFlowClass
{
    Standard,
    Engineering,
    Flexible,
}

public sealed class MaterialInfo
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public PrintProcess Process { get; init; }

    public double DensityGramsPerCm3 { get; init; }

    public double CostPerUnit { get; init; }

    public FdmFlowClass FlowClass { get; init; }

    public double MinLayerSeconds { get; init; }
}

public sealed class FdmEstimate
{
    public double PrintMinutes { get; init; }

    public double MaterialGrams { get; init; }

    public double SupportGrams { get; init; }
}

public sealed class GeometryInput
{
    public double HeightMm { get; init; }

    public double VolumeMm3 { get; init; }

    public IReadOnlyList<double> AreaProfileMm2 { get; init; } = [];

    public IReadOnlyList<double> PerimeterProfileMm { get; init; } = [];

    public double FootprintMm2 { get; init; }
}

public sealed class BulkTier
{
    public int MinQuantity { get; init; }

    public double UnitPrice { get; init; }

    public bool Active { get; init; }
}

public sealed class ItemQuote
{
    public PrintProcess Process { get; init; }

    public double PrintTimeMinutesPerUnit { get; init; }

    public double MaterialPerUnit { get; init; }

    public double WeightGramsPerUnit { get; init; }

    public double BoundingCm3PerUnit { get; init; }

    public double UnitPrice { get; init; }

    public double Subtotal { get; init; }

    public IReadOnlyList<BulkTier> Tiers { get; init; } = [];
}

public sealed class OrderLine
{
    public PrintProcess Process { get; init; }

    public double Subtotal { get; init; }
}

public sealed class OrderQuote
{
    public double ItemsSubtotal { get; init; }

    public double Printing { get; init; }

    public double ShippingCost { get; init; }

    public double PriceBeforeVat { get; init; }

    public double Vat { get; init; }

    public double FinalOrderPrice { get; init; }
}

public sealed record DiscountTier(int MinQuantity, double BulkDiscount, double TargetMargin);
