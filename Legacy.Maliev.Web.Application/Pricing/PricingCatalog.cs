namespace Legacy.Maliev.Web.Application.Pricing;

public static class PricingCatalog
{
    public const double FdmLayerHeightMm = 0.2;
    public const double ResinLayerHeightMm = 0.05;
    public const double FdmLineWidthMm = 0.42;
    public const int FdmWallCount = 3;
    public const double FdmInfillDensity = 0.15;
    public const double FdmWallSpeedMmPerSec = 50.0;
    public const double FdmSupportDensity = 0.15;
    public const double FdmSupportReachFactor = 0.5;
    public const double FdmSupportRemovalSecondsPerGram = 8.0;
    public const double ResinPerLayerSeconds = 2.5;
    public const double FdmMachineHourly = 17.0;
    public const double ResinMachineHourly = 29.0;
    public const double MonthlyFixedCost = 311_097.0;
    public const double FdmOverheadAllocation = 0.70;
    public const double ResinOverheadAllocation = 0.30;
    public const int PrinterCount = 2;
    public const double UtilizationRate = 0.50;
    public const double CalendarMinutesPerMonth = 30.0 * 24.0 * 60.0;
    public const double LaborRatePerHour = 156.25;
    public const double FdmWasteAllowance = 0.10;
    public const double ResinSupportAllowance = 0.15;
    public const double ComplexityFactor = 1.0;
    public const double ResinPostProcessingHours = 0.5;
    public const double ResinConsumablesPerPart = 100.0;
    public const double ResinBuildPlateAreaMm2 = 25_000.0;
    public const double ResinPlatePackingFactor = 0.60;
    public const int ResinMaxPartsPerPlate = 64;
    public const double PaymentFeeRate = 0.03;
    public const double VatRate = 0.07;

    public static readonly IReadOnlyList<DiscountTier> DiscountTiers =
    [
        new(1, 0.00, 0.50),
        new(10, 0.05, 0.35),
        new(50, 0.10, 0.25),
        new(100, 0.15, 0.20),
    ];

    public static readonly IReadOnlyDictionary<string, MaterialInfo> Materials = BuildMaterials();

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> MaterialColors = BuildMaterialColors();

    private static readonly IReadOnlySet<string> CustomColorMaterials = new HashSet<string>(
        ["PLA", "PETG", "ABS", "ASA", "TPU", "PC", "PC-FR", "PA6", "PA12", "ABS-FR"],
        StringComparer.OrdinalIgnoreCase);

    public static double AvailablePrinterMinutes => PrinterCount * UtilizationRate * CalendarMinutesPerMonth;

    public static double FdmFlowRateMm3PerSecond(FdmFlowClass flowClass) => flowClass switch
    {
        FdmFlowClass.Standard => 12.0,
        FdmFlowClass.Engineering => 8.0,
        FdmFlowClass.Flexible => 5.0,
        _ => 12.0,
    };

    public static int EstimatePartsPerPlate(double footprintMm2)
    {
        if (footprintMm2 <= 0)
        {
            return 1;
        }

        var usableArea = ResinBuildPlateAreaMm2 * ResinPlatePackingFactor;
        return Math.Clamp((int)Math.Floor(usableArea / footprintMm2), 1, ResinMaxPartsPerPlate);
    }

    public static double SetupHours(PrintProcess process) => process == PrintProcess.Resin ? 0.35 : 0.25;

    public static double FailureReserveRate(PrintProcess process) => process == PrintProcess.Resin ? 0.15 : 0.10;

    public static double MinimumOrderPrice(PrintProcess process) => process == PrintProcess.Resin ? 500.0 : 300.0;

    public static double OverheadPerMinute(PrintProcess process)
    {
        var allocation = process == PrintProcess.Resin ? ResinOverheadAllocation : FdmOverheadAllocation;
        return (MonthlyFixedCost * allocation) / AvailablePrinterMinutes;
    }

    public static double MachineHourly(PrintProcess process) =>
        process == PrintProcess.Resin ? ResinMachineHourly : FdmMachineHourly;

    public static DiscountTier ResolveTier(int quantity) =>
        DiscountTiers.LastOrDefault(tier => quantity >= tier.MinQuantity) ?? DiscountTiers[0];

    public static MaterialInfo? ResolveMaterial(string? key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? null
            : Materials.GetValueOrDefault(key.Trim());
    }

    public static bool IsColorSupported(string? materialKey, string? color)
    {
        if (string.IsNullOrWhiteSpace(materialKey)
            || string.IsNullOrWhiteSpace(color)
            || !Materials.ContainsKey(materialKey.Trim())
            || !MaterialColors.TryGetValue(materialKey.Trim(), out var colors))
        {
            return false;
        }

        return colors.Contains(color, StringComparer.Ordinal)
            || (CustomColorMaterials.Contains(materialKey.Trim()) && IsHexColor(color));
    }

    private static bool IsHexColor(string color) => color.Length == 7
        && color[0] == '#'
        && color[1..].All(character => character is >= '0' and <= '9'
            or >= 'A' and <= 'F'
            or >= 'a' and <= 'f');

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildMaterialColors()
    {
        string[] full = ["Any", "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"];
        string[] neutral = ["Any", "Natural", "Black", "White", "Gray"];
        string[] carbon = ["Black", "Natural"];

        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PLA"] = full,
            ["PETG"] = full,
            ["ABS"] = full,
            ["ASA"] = full,
            ["HIPS"] = ["Black", "White"],
            ["TPU"] = ["Any", "Black", "White", "Clear", "Red", "Blue"],
            ["PC"] = neutral,
            ["PC-FR"] = neutral,
            ["PA6"] = neutral,
            ["PA12"] = neutral,
            ["ABS-FR"] = neutral,
            ["PLA-CF"] = carbon,
            ["PETG-CF"] = carbon,
            ["PET-CF"] = carbon,
            ["PA-CF"] = carbon,
            ["ASA-CF"] = carbon,
            ["PETG-ESD"] = carbon,
            ["PC-ESD"] = ["Black"],
            ["PVA"] = ["Natural"],
            ["M68"] = ["Gray", "Black", "White"],
            ["K"] = ["Gray", "Black"],
            ["G217"] = ["Clear"],
            ["F80"] = ["Black", "Translucent"],
            ["CASTWAX"] = ["Green"],
        };
    }

    private static IReadOnlyDictionary<string, MaterialInfo> BuildMaterials()
    {
        MaterialInfo Fdm(
            string key,
            string name,
            double density,
            double costPerGram,
            FdmFlowClass flow,
            double minLayerSeconds) => new()
            {
                Key = key,
                DisplayName = name,
                Process = PrintProcess.Fdm,
                DensityGramsPerCm3 = density,
                CostPerUnit = costPerGram,
                FlowClass = flow,
                MinLayerSeconds = minLayerSeconds,
            };

        MaterialInfo Resin(string key, string name, double costPerMl) => new()
        {
            Key = key,
            DisplayName = name,
            Process = PrintProcess.Resin,
            CostPerUnit = costPerMl,
        };

        MaterialInfo[] materials =
        [
            Fdm("PLA", "PLA — Polylactic Acid", 1.24, 0.83, FdmFlowClass.Standard, 10),
            Fdm("PLA-CF", "PLA-CF — PLA + Carbon Fiber", 1.29, 1.925, FdmFlowClass.Engineering, 10),
            Fdm("PETG", "PETG — PET Glycol-modified", 1.27, 0.66, FdmFlowClass.Standard, 10),
            Fdm("PETG-CF", "PETG-CF — PETG + Carbon Fiber", 1.29, 1.55, FdmFlowClass.Engineering, 10),
            Fdm("PETG-ESD", "PETG-ESD — Electrostatic Discharge Safe", 1.31, 2.70, FdmFlowClass.Engineering, 10),
            Fdm("PET-CF", "PET-CF — PET + Carbon Fiber", 1.30, 1.70, FdmFlowClass.Engineering, 10),
            Fdm("ABS", "ABS — Acrylonitrile Butadiene Styrene", 1.04, 0.76, FdmFlowClass.Standard, 5),
            Fdm("ABS-FR", "ABS-FR — Flame-Retardant ABS", 1.15, 1.60, FdmFlowClass.Standard, 5),
            Fdm("ASA", "ASA — Acrylonitrile Styrene Acrylate", 1.07, 0.86, FdmFlowClass.Standard, 5),
            Fdm("ASA-CF", "ASA-CF — ASA + Carbon Fiber", 1.11, 1.834, FdmFlowClass.Engineering, 5),
            Fdm("HIPS", "HIPS — High Impact Polystyrene", 1.04, 0.925, FdmFlowClass.Standard, 6),
            Fdm("PC", "PC — Polycarbonate", 1.20, 1.05, FdmFlowClass.Engineering, 4),
            Fdm("PC-FR", "PC-FR — Flame-Retardant Polycarbonate", 1.25, 2.15, FdmFlowClass.Engineering, 4),
            Fdm("PC-ESD", "PC-ESD — ESD-Safe Polycarbonate (3DXTech)", 1.20, 3.80, FdmFlowClass.Engineering, 4),
            Fdm("PA6", "PA6 — Nylon 6", 1.14, 1.95, FdmFlowClass.Engineering, 4),
            Fdm("PA12", "PA12 — Nylon 12", 1.01, 2.034, FdmFlowClass.Engineering, 4),
            Fdm("PA-CF", "PA-CF — Nylon + Carbon Fiber", 1.16, 3.534, FdmFlowClass.Engineering, 4),
            Fdm("TPU", "TPU — Flexible Thermoplastic Polyurethane", 1.21, 1.05, FdmFlowClass.Flexible, 12),
            Fdm("PVA", "PVA — Water-Soluble Support", 1.23, 2.568, FdmFlowClass.Standard, 8),
            Resin("M68", "Standard Resin (M68)", 2.14),
            Resin("K", "Tough Resin (K)", 2.14),
            Resin("G217", "Transparent Resin (G217)", 2.675),
            Resin("F80", "Flexible Resin (F80)", 2.675),
            Resin("CASTWAX", "Castable Wax Resin", 3.50),
        ];

        return materials.ToDictionary(material => material.Key, StringComparer.OrdinalIgnoreCase);
    }
}
