using System.Globalization;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Serves the retained estimate and order-total compatibility handlers using server pricing rules.</summary>
public static class InstantQuotationCalculator
{
    private const int MaximumProfileSamples = 512;

    /// <summary>Builds the material selector from the pricing catalog with PLA initially selected.</summary>
    /// <returns>The ordered material choices for the quotation page.</returns>
    public static InstantQuotationDisplayModel CreateDisplayModel() => new(
        PricingCatalog.Materials.Values
            .OrderBy(material => material.Process)
            .ThenBy(material => material.DisplayName, StringComparer.Ordinal)
            .Select(material => new InstantQuotationMaterialOption(
                material.Key,
                material.DisplayName,
                string.Equals(material.Key, "PLA", StringComparison.Ordinal)))
            .ToArray());

    /// <summary>Validates normalized geometry and produces the retained THB estimate response.</summary>
    /// <param name="material">The pricing-catalog material key.</param>
    /// <param name="dimensionZ">The reported model height in millimetres.</param>
    /// <param name="volume">The reported model volume in cubic millimetres.</param>
    /// <param name="footprint">The reported footprint area in square millimetres.</param>
    /// <param name="areaProfile">Comma-separated cross-sectional areas in square millimetres.</param>
    /// <param name="perimeterProfile">Comma-separated cross-sectional perimeters in millimetres.</param>
    /// <param name="currency">The compatibility currency input; this calculator always returns THB.</param>
    /// <param name="quantity">The requested number of identical parts.</param>
    /// <param name="unsupportedAreaProfile">Optional comma-separated unsupported areas in square millimetres.</param>
    /// <returns>The compatibility success payload with price tiers, or a bounded material or geometry failure.</returns>
    public static object GetEstimate(
        string? material,
        double dimensionZ,
        double volume,
        double footprint,
        string? areaProfile,
        string? perimeterProfile,
        string? currency,
        int quantity,
        string? unsupportedAreaProfile = null)
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
            UnsupportedAreaProfileMm2 = ParseProfile(unsupportedAreaProfile),
        };
        var validation = AdditiveGeometryValidator.Validate(geometry, quantity);
        if (!validation.IsValid)
        {
            return new
            {
                success = false,
                code = "geometry_invalid",
                reasonCodes = validation.ReasonCodes,
            };
        }
        var quote = PricingEngine.QuoteItem(geometry, materialInfo, quantity);

        return new
        {
            success = true,
            process = materialInfo.Process == PrintProcess.Resin ? "resin" : "fdm",
            unitPrice = Math.Round(quote.UnitPrice, 2),
            subtotal = Math.Round(quote.Subtotal, 2),
            subtotalThb = Math.Round(quote.Subtotal, 4),
            technicalFilamentMinimumApplied = quote.TechnicalFilamentMinimumApplied,
            technicalFilamentMinimumPrice = Math.Round(quote.TechnicalFilamentMinimumPrice, 2),
            technicalFilamentMinimumAdjustment = Math.Round(quote.TechnicalFilamentMinimumAdjustment, 2),
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

    /// <summary>Combines bounded line subtotals with destination-aware shipping, minimum-order rules and VAT.</summary>
    /// <param name="processes">Comma-separated process labels paired with the submitted subtotals.</param>
    /// <param name="subtotals">Comma-separated THB line subtotals parsed with invariant culture.</param>
    /// <param name="totalWeightGrams">The combined shipment weight in grams.</param>
    /// <param name="totalBoundingCm3">The combined bounding volume in cubic centimetres.</param>
    /// <param name="currency">The compatibility currency input; this calculator always returns THB.</param>
    /// <param name="destinationCountry">The optional destination used to determine shipping availability.</param>
    /// <returns>The compatibility order-total payload in THB, including shipping state.</returns>
    public static object GetOrderTotal(
        string? processes,
        string? subtotals,
        double totalWeightGrams,
        double totalBoundingCm3,
        string? currency,
        string? destinationCountry = null)
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

        var shippingQuote = ShippingCalculator.Quote(
            destinationCountry,
            AbsoluteFinite(totalWeightGrams),
            AbsoluteFinite(totalBoundingCm3));
        var order = PricingEngine.QuoteOrder(lines, Convert.ToDouble(shippingQuote.AmountThb));
        return new
        {
            success = true,
            printing = Math.Round(order.Printing, 2),
            itemsSubtotal = Math.Round(order.ItemsSubtotal, 2),
            minimumOrderPrice = Math.Round(order.MinimumOrderPrice, 2),
            minimumOrderSurcharge = Math.Round(order.MinimumOrderSurcharge, 2),
            shipping = Math.Round(order.ShippingCost, 2),
            shippingState = shippingQuote.State.ToString(),
            destinationCountryCode = shippingQuote.DestinationCountryCode,
            vat = Math.Round(order.Vat, 2),
            priceBeforeVat = Math.Round(order.PriceBeforeVat, 2),
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
