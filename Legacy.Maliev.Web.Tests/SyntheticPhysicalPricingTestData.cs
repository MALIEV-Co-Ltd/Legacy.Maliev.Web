using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

/// <summary>
/// Supplies synthetic verified ledgers to legacy order-math tests. This is test-only evidence;
/// production obtains its ledgers independently from admitted server-side STL bytes.
/// </summary>
internal static class SyntheticPhysicalPricingTestData
{
    internal static InstantQuotationOrderQuote Quote(
        InstantQuotationOrderState state,
        string? destinationCountry = null)
    {
        var parts = state.Parts.Select(part => part with
        {
            PhysicalAnalysisUpload = part.PhysicalAnalysisUpload ?? new InstantQuotationPhysicalAnalysisUpload(
                part.PartId, part.DisplayFileName, "model/stl", 100, part.Geometry.Sha256, "clean"),
        }).ToArray();
        var evidence = new Dictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult>();
        foreach (var part in parts)
        {
            foreach (var material in PricingCatalog.Materials.Values.Where(
                static material => material.Process == PrintProcess.Fdm))
            {
                var geometry = part.Geometry;
                var input = new GeometryInput
                {
                    HeightMm = geometry.HeightMm,
                    VolumeMm3 = geometry.VolumeMm3,
                    FootprintMm2 = geometry.FootprintMm2,
                    AreaProfileMm2 = geometry.AreaProfileMm2,
                    PerimeterProfileMm = geometry.PerimeterProfileMm,
                    UnsupportedAreaProfileMm2 = geometry.UnsupportedAreaProfileMm2,
                };
                var estimate = PrintTimeCalculator.EstimateFdm(
                    input, material, part.Configuration.BuildPreference);
                double totalMm3 = estimate.MaterialGrams * 1_000d
                    / material.DensityGramsPerCm3 * part.Configuration.Quantity;
                double supportMm3 = estimate.SupportGrams * 1_000d
                    / material.DensityGramsPerCm3 * part.Configuration.Quantity;
                double seconds = estimate.PrintMinutes * 60d * part.Configuration.Quantity;
                var physical = SimulationResult.Create(
                    FdmSimulationEngine.AnalysisVersion,
                    new string('B', 64),
                    [
                        new RoleDeposition(ExtrusionRole.OuterWall, material.Key,
                            Math.Max(0, totalMm3 - supportMm3), seconds),
                        new RoleDeposition(ExtrusionRole.SupportBody, material.Key,
                            supportMm3, 0),
                    ],
                    new MotionLedger(new Dictionary<ExtrusionRole, double>
                    {
                        [ExtrusionRole.OuterWall] = seconds,
                    }, 0, 0, 0),
                    [], []);
                var binding = new InstantQuotationPhysicalAnalysisBinding(
                    "test-session", null, part.PartId, part.PhysicalAnalysisUpload!.FileId,
                    part.Geometry.Sha256, material.Key, part.Configuration.BuildPreference,
                    part.Configuration.Quantity, FdmRuntimeProfileCatalog.LoadEmbedded().ProfileVersion,
                    part.Configuration.MaterialKey);
                evidence[(part.PartId, material.Key)] = new(
                    binding, physical, InstantQuotationBoundPhysicalAnalysisFailure.None,
                    Math.Abs(geometry.FootprintMm2 * geometry.HeightMm) / 1_000d);
            }
        }

        return new InstantQuotationPricingService().QuoteWithPhysical(
            new InstantQuotationOrderState(parts), destinationCountry, evidence);
    }
}

internal sealed class SyntheticAuthoritativePricingTestService : IInstantQuotationAuthoritativePricingService
{
    internal static readonly SyntheticAuthoritativePricingTestService Instance = new();

    public Task<InstantQuotationOrderQuote?> QuoteAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        bool includeComparisons,
        CancellationToken cancellationToken) => Task.FromResult<InstantQuotationOrderQuote?>(
            SyntheticPhysicalPricingTestData.Quote(session.RequestState));
}
