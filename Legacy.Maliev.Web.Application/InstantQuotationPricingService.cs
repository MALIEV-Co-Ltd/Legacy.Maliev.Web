using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Application;

public interface IInstantQuotationPricingService
{
    InstantQuotationOrderQuote Quote(InstantQuotationOrderState state);
}

public sealed class InstantQuotationPricingService : IInstantQuotationPricingService
{
    public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state) => Quote(state, null);

    internal InstantQuotationOrderQuote QuoteWithPhysical(
        InstantQuotationOrderState state,
        IReadOnlyDictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult> physical)
        => QuoteCore(state, null, physical);

    internal InstantQuotationOrderQuote QuoteWithPhysical(
        InstantQuotationOrderState state,
        string? destinationCountry,
        IReadOnlyDictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult> physical)
        => QuoteCore(state, destinationCountry, physical);

    public InstantQuotationOrderQuote Quote(
        InstantQuotationOrderState state,
        string? destinationCountry) => QuoteCore(state, destinationCountry, null);

    private static InstantQuotationOrderQuote QuoteCore(
        InstantQuotationOrderState state,
        string? destinationCountry,
        IReadOnlyDictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult>? physical)
    {
        ArgumentNullException.ThrowIfNull(state);

        var partQuotes = (state.Parts ?? throw new ArgumentException("Parts are required.", nameof(state)))
            .Select(part => QuotePart(part, physical))
            .ToArray();
        var shippingQuote = partQuotes.Length == 0
            ? new ShippingQuote
            {
                DestinationCountryCode = "TH",
                State = ShippingPricingState.DomesticPriced,
                AmountThb = 0,
            }
            : ShippingCalculator.Quote(
                destinationCountry,
                partQuotes.Sum(part => part.WeightGramsPerUnit * part.Quantity),
                partQuotes.Sum(part => part.BoundingCm3PerUnit * part.Quantity));
        // The rounded customer-facing line price is already the commercial price.
        // Rebuilding it from direct physical cost would change the price between the
        // material card, protected line ticket, order summary, and submission.
        var lineSubtotals = partQuotes.Select(part => Convert.ToDecimal(part.Subtotal)).ToArray();
        var commercialSubtotal = lineSubtotals.Sum();
        var minimumOrderPrice = partQuotes.Max(part =>
            Convert.ToDecimal(PricingCatalog.MinimumOrderPrice(part.Process)));
        var printing = Math.Max(commercialSubtotal, minimumOrderPrice);
        var minimumOrderSurcharge = printing - commercialSubtotal;
        var shipping = shippingQuote.AmountThb;
        var priceBeforeVat = printing + shipping;
        var vat = decimal.Round(
            priceBeforeVat * Convert.ToDecimal(PricingCatalog.VatRate),
            2,
            MidpointRounding.AwayFromZero);
        var finalOrderPrice = priceBeforeVat + vat;
        var allocations = AllocateOrderTotal(lineSubtotals, finalOrderPrice);
        var allocatedParts = partQuotes.Select((part, index) => part with
        {
            AllocatedOrderTotal = Convert.ToDouble(allocations[index]),
        }).ToArray();
        var leadTime = AdditiveLeadTimeCalculator.Calculate(partQuotes.Select(part => new AdditiveLeadTimeLine
        {
            MinutesPerUnit = Convert.ToDecimal(part.PrintTimeMinutesPerUnit),
            Quantity = part.Quantity,
        }));

        return new InstantQuotationOrderQuote(
            allocatedParts,
            Convert.ToDouble(commercialSubtotal),
            Convert.ToDouble(printing),
            Convert.ToDouble(minimumOrderPrice),
            Convert.ToDouble(minimumOrderSurcharge),
            Convert.ToDouble(shipping),
            Convert.ToDouble(priceBeforeVat),
            Convert.ToDouble(vat),
            Convert.ToDouble(finalOrderPrice),
            leadTime.MinimumDays,
            leadTime.MaximumDays,
            shippingQuote.State,
            shippingQuote.DestinationCountryCode,
            AllocatedLineTotals: allocations.Select(Convert.ToDouble).ToArray());
    }

    private static decimal[] AllocateOrderTotal(IReadOnlyList<decimal> lineSubtotals, decimal finalOrderPrice)
    {
        var subtotal = lineSubtotals.Sum();
        var candidates = lineSubtotals.Select((lineSubtotal, index) =>
        {
            var share = subtotal == 0m ? 1m / lineSubtotals.Count : lineSubtotal / subtotal;
            var raw = finalOrderPrice * share;
            var floor = decimal.Floor(raw * 100m) / 100m;
            return new { Index = index, Floor = floor, Fraction = raw - floor };
        }).ToArray();
        var remainingSatang = decimal.ToInt32(decimal.Round(
            (finalOrderPrice - candidates.Sum(candidate => candidate.Floor)) * 100m,
            0));
        var increments = candidates
            .OrderByDescending(candidate => candidate.Fraction)
            .ThenBy(candidate => candidate.Index)
            .Take(remainingSatang)
            .Select(candidate => candidate.Index)
            .ToHashSet();

        return candidates
            .Select(candidate => candidate.Floor + (increments.Contains(candidate.Index) ? 0.01m : 0m))
            .ToArray();
    }

    private static InstantQuotationPartQuote QuotePart(
        InstantQuotationPart part,
        IReadOnlyDictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult>? physical)
    {
        var (material, geometryInput, buildPreference) = ValidatePart(part);
        var configuration = part.Configuration;
        InstantQuotationPhysicalAnalysisReceipt? receipt = null;
        ItemQuote item;
        if (material.Process == PrintProcess.Fdm)
        {
            if (physical is null
                || !physical.TryGetValue((part.PartId, material.Key), out var evidence)
                || !evidence.IsReady
                || !MatchesEvidence(part, material.Key, buildPreference, evidence))
            {
                throw new InvalidOperationException("A current server-verified physical simulation is required for FDM pricing.");
            }

            item = PricingEngine.QuoteFdmSimulation(
                evidence.Physical!, material, configuration.Quantity,
                evidence.BoundingCm3PerUnit);
            receipt = InstantQuotationPhysicalAnalysisReceipt.Create(
                evidence.Binding!, configuration.MaterialKey, evidence.Physical!,
                evidence.BoundingCm3PerUnit);
        }
        else
        {
            item = PricingEngine.QuoteItem(geometryInput, material, configuration.Quantity, buildPreference);
        }
        var materialPrices = PricingCatalog.Materials.Values
            .Select(candidate => MaterialPrice(part, candidate, geometryInput, buildPreference, physical))
            .ToArray();

        return new InstantQuotationPartQuote(
            part.PartId,
            material.Key,
            configuration.Color,
            configuration.Quantity,
            item.Process,
            item.PrintTimeMinutesPerUnit,
            item.MaterialPerUnit,
            item.WeightGramsPerUnit,
            item.BoundingCm3PerUnit,
            item.DirectCostPerUnit,
            item.UnitPrice,
            item.Subtotal,
            item.TechnicalFilamentMinimumApplied,
            item.TechnicalFilamentMinimumPrice,
            item.TechnicalFilamentMinimumAdjustment,
            item.Tiers,
            buildPreference,
            materialPrices,
            PhysicalReceipt: receipt)
        {
            ResinProfile = item.ResinProfile,
        };
    }

    // Reuses the exact candidate kernel and validation; never projects its receipt.
    internal double? MaterialUnitPrice(
        InstantQuotationPart part,
        MaterialInfo candidate,
        IReadOnlyDictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult> physical)
    {
        var (_, geometry, build) = ValidatePart(part);
        var amount = MaterialPrice(part, candidate, geometry, build, physical).UnitPrice;
        return amount is { } value && double.IsFinite(value) && value > 0 ? value : null;
    }

    private static (MaterialInfo Material, GeometryInput Geometry, BuildPreference Build) ValidatePart(
        InstantQuotationPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(part.Geometry);
        ArgumentNullException.ThrowIfNull(part.Configuration);

        var configuration = part.Configuration;
        if (configuration.Quantity < 1 || configuration.Quantity > PricingCatalog.MaximumAdditiveQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration.Quantity),
                configuration.Quantity,
                $"Quantity must be between 1 and {PricingCatalog.MaximumAdditiveQuantity}.");
        }

        var material = PricingCatalog.ResolveMaterial(configuration.MaterialKey)
            ?? throw new ArgumentException("The selected material is not supported.", nameof(configuration.MaterialKey));
        if (!PricingCatalog.IsColorSupported(material.Key, configuration.Color))
        {
            throw new ArgumentException(
                "The selected color is not supported for the selected material.",
                nameof(configuration.Color));
        }

        var geometry = part.Geometry;
        var geometryInput = new GeometryInput
        {
            HeightMm = geometry.HeightMm,
            VolumeMm3 = geometry.VolumeMm3,
            FootprintMm2 = geometry.FootprintMm2,
            AreaProfileMm2 = geometry.AreaProfileMm2,
            PerimeterProfileMm = geometry.PerimeterProfileMm,
            UnsupportedAreaProfileMm2 = geometry.UnsupportedAreaProfileMm2,
        };
        var validation = AdditiveGeometryValidator.Validate(geometryInput, configuration.Quantity);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                $"The submitted geometry is not eligible for pricing: {string.Join(',', validation.ReasonCodes)}.",
                nameof(part.Geometry));
        }
        var buildPreference = material.Process == PrintProcess.Resin
            ? BuildPreference.Standard
            : configuration.BuildPreference;
        return (material, geometryInput, buildPreference);
    }

    private static InstantQuotationMaterialPrice MaterialPrice(
        InstantQuotationPart part,
        MaterialInfo candidate,
        GeometryInput geometry,
        BuildPreference buildPreference,
        IReadOnlyDictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult>? physical)
    {
        if (candidate.Process == PrintProcess.Resin)
        {
            try
            {
                return new(candidate.Key, PricingEngine.QuoteItem(
                    geometry, candidate, part.Configuration.Quantity, BuildPreference.Standard).UnitPrice);
            }
            catch (ArgumentException)
            {
                return new(candidate.Key, null);
            }
        }

        if (physical is null
            || !physical.TryGetValue((part.PartId, candidate.Key), out var evidence)
            || !evidence.IsReady
            || !MatchesEvidence(part, candidate.Key, buildPreference, evidence))
        {
            return new(candidate.Key, null);
        }

        try
        {
            var quote = PricingEngine.QuoteFdmSimulation(
                evidence.Physical!, candidate, part.Configuration.Quantity,
                evidence.BoundingCm3PerUnit);
            return new(candidate.Key, quote.UnitPrice,
                InstantQuotationPhysicalAnalysisReceipt.Create(
                    evidence.Binding!, part.Configuration.MaterialKey, evidence.Physical!,
                    evidence.BoundingCm3PerUnit));
        }
        catch (ArgumentException)
        {
            return new(candidate.Key, null);
        }
    }

    private static bool MatchesEvidence(
        InstantQuotationPart part,
        string candidateMaterialKey,
        BuildPreference preference,
        InstantQuotationBoundPhysicalAnalysisResult evidence)
    {
        var binding = evidence.Binding;
        var upload = part.PhysicalAnalysisUpload;
        return binding is not null && upload is not null
            && binding.PartId == part.PartId && binding.FileId == upload.FileId
            && string.Equals(binding.UploadSha256, upload.Sha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(binding.MaterialKey, candidateMaterialKey, StringComparison.Ordinal)
            && string.Equals(binding.ConfiguredMaterialKey ?? binding.MaterialKey,
                part.Configuration.MaterialKey, StringComparison.Ordinal)
            && binding.BuildPreference == preference && binding.Quantity == part.Configuration.Quantity;
    }
}
