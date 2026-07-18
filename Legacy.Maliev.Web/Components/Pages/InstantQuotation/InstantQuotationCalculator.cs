using System.Globalization;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public static class InstantQuotationCalculator
{
    private const int MaximumProfileSamples = 512;

    public static InstantQuotationDisplayModel CreateDisplayModel() => new(
        PricingCatalog.Materials.Values
            .OrderBy(material => material.Process)
            .ThenBy(material => material.DisplayName, StringComparer.Ordinal)
            .Select(material => new InstantQuotationMaterialOption(
                material.Key,
                material.DisplayName,
                string.Equals(material.Key, "PLA", StringComparison.Ordinal)))
            .ToArray());

    public static object GetEstimate(
        string? material,
        double dimensionZ,
        double volume,
        double footprint,
        string? areaProfile,
        string? perimeterProfile,
        string? currency,
        int quantity)
    {
        var materialInfo = PricingCatalog.ResolveMaterial(material);
        if (materialInfo is null)
        {
            return new
            {
                success = false,
                message = "The selected material is not supported for instant pricing.",
            };
        }

        var geometry = new GeometryInput
        {
            HeightMm = AbsoluteFinite(dimensionZ),
            VolumeMm3 = AbsoluteFinite(volume),
            FootprintMm2 = AbsoluteFinite(footprint),
            AreaProfileMm2 = ParseProfile(areaProfile),
            PerimeterProfileMm = ParseProfile(perimeterProfile),
        };
        var quote = PricingEngine.QuoteItem(geometry, materialInfo, quantity);

        return new
        {
            success = true,
            process = materialInfo.Process == PrintProcess.Resin ? "resin" : "fdm",
            unitPrice = Math.Round(quote.UnitPrice, 2),
            subtotal = Math.Round(quote.Subtotal, 2),
            subtotalThb = Math.Round(quote.Subtotal, 4),
            weightGrams = Math.Round(quote.WeightGramsPerUnit, 2),
            boundingCm3 = Math.Round(quote.BoundingCm3PerUnit, 2),
            printTimeMinutes = Math.Round(quote.PrintTimeMinutesPerUnit, 1),
            materialPerUnit = Math.Round(quote.MaterialPerUnit, 1),
            tiers = quote.Tiers.Select(tier => new
            {
                minQuantity = tier.MinQuantity,
                unitPrice = Math.Round(tier.UnitPrice, 2),
                active = tier.Active,
            }),
            currency = NormalizeCurrency(currency),
        };
    }

    public static object GetOrderTotal(
        string? processes,
        string? subtotals,
        double totalWeightGrams,
        double totalBoundingCm3,
        string? currency)
    {
        var processList = SplitValues(processes);
        var subtotalList = SplitValues(subtotals);
        var lineCount = Math.Min(Math.Min(processList.Length, subtotalList.Length), 100);
        var lines = new List<OrderLine>(lineCount);
        for (var index = 0; index < lineCount; index++)
        {
            if (double.TryParse(subtotalList[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var subtotal)
                && double.IsFinite(subtotal))
            {
                lines.Add(new OrderLine
                {
                    Process = string.Equals(processList[index], "resin", StringComparison.OrdinalIgnoreCase)
                        ? PrintProcess.Resin
                        : PrintProcess.Fdm,
                    Subtotal = Math.Max(0, subtotal),
                });
            }
        }

        var shipping = ShippingCalculator.CustomerShippingThb(
            AbsoluteFinite(totalWeightGrams),
            AbsoluteFinite(totalBoundingCm3));
        var order = PricingEngine.QuoteOrder(lines, shipping);
        return new
        {
            success = true,
            printing = Math.Round(order.Printing, 2),
            shipping = Math.Round(order.ShippingCost, 2),
            vat = Math.Round(order.Vat, 2),
            finalOrderPrice = Math.Round(order.FinalOrderPrice, 2),
            currency = NormalizeCurrency(currency),
        };
    }

    private static string[] SplitValues(string? values) =>
        (values ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<double> ParseProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return [];
        }

        return profile.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaximumProfileSamples)
            .Select(token => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value)
                    ? Math.Abs(value)
                    : (double?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
    }

    private static double AbsoluteFinite(double value) => double.IsFinite(value) ? Math.Abs(value) : 0;

    private static string NormalizeCurrency(string? _) => "THB";
}
