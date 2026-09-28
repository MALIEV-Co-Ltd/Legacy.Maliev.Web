using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Application;

/// <summary>Creates owner-scoped server-authoritative prices from admitted uploads.</summary>
public interface IInstantQuotationAuthoritativePricingService
{
    Task<InstantQuotationOrderQuote?> QuoteAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        bool includeComparisons,
        CancellationToken cancellationToken);
}

/// <summary>Prices only current admitted STL evidence; unverified FDM remains unavailable.</summary>
internal sealed class InstantQuotationAuthoritativePricingService(
    InstantQuotationBoundPhysicalAnalysisService analysis,
    FdmRuntimeProfileCatalog profiles,
    InstantQuotationPricingService pricing) : IInstantQuotationAuthoritativePricingService
{
    public async Task<InstantQuotationOrderQuote?> QuoteAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        bool includeComparisons,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Parts.Count == 0)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var evidence = new Dictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult>();
        try
        {
            foreach (var part in session.Parts)
            {
                var selected = PricingCatalog.ResolveMaterial(part.Configuration.MaterialKey);
                if (selected is null)
                {
                    return null;
                }

                if (selected.Process == PrintProcess.Fdm)
                {
                    var result = await AnalyzeAsync(part, selected.Key, session.SessionId, ownerIdentity, timeout.Token);
                    if (!IsCurrentEvidence(result, session.SessionId, ownerIdentity, part, selected.Key))
                    {
                        return null;
                    }

                    evidence[(part.PartId, selected.Key)] = result;
                }
            }

            if (includeComparisons)
            {
                using var comparisonTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                comparisonTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                foreach (var part in session.Parts)
                {
                    if (!IsAdmittedStl(part))
                    {
                        continue;
                    }

                    foreach (var candidate in PricingCatalog.Materials.Values.Where(
                        static material => material.Process == PrintProcess.Fdm))
                    {
                        if (evidence.ContainsKey((part.PartId, candidate.Key)))
                        {
                            continue;
                        }

                        if (comparisonTimeout.IsCancellationRequested)
                        {
                            break;
                        }

                        InstantQuotationBoundPhysicalAnalysisResult result;
                        try
                        {
                            result = await AnalyzeAsync(
                                part, candidate.Key, session.SessionId, ownerIdentity,
                                comparisonTimeout.Token);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                            && comparisonTimeout.IsCancellationRequested)
                        {
                            break;
                        }
                        if (IsCurrentEvidence(result, session.SessionId, ownerIdentity, part, candidate.Key))
                        {
                            evidence[(part.PartId, candidate.Key)] = result;
                        }
                    }
                }
            }

            return pricing.QuoteWithPhysical(session.RequestState, evidence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private Task<InstantQuotationBoundPhysicalAnalysisResult> AnalyzeAsync(
        InstantQuotationPart part,
        string materialKey,
        string sessionId,
        string? ownerIdentity,
        CancellationToken cancellationToken)
    {
        if (!IsAdmittedStl(part))
        {
            return Task.FromResult(InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.UploadUnavailable));
        }

        var upload = part.PhysicalAnalysisUpload!;
        var binding = new InstantQuotationPhysicalAnalysisBinding(
            sessionId, ownerIdentity, part.PartId, upload.FileId, upload.Sha256,
            materialKey, part.Configuration.BuildPreference, part.Configuration.Quantity,
            profiles.ProfileVersion, part.Configuration.MaterialKey);
        return analysis.AnalyzeAsync(binding, cancellationToken);
    }

    private static bool IsCurrentEvidence(
        InstantQuotationBoundPhysicalAnalysisResult result,
        string sessionId,
        string? ownerIdentity,
        InstantQuotationPart part,
        string materialKey) =>
        result is { IsReady: true, Binding: { } binding, Physical: { } physical }
        && physical.Diagnostics.Count == 0
        && string.Equals(binding.SessionId, sessionId, StringComparison.Ordinal)
        && string.Equals(binding.OwnerIdentity, ownerIdentity, StringComparison.Ordinal)
        && binding.PartId == part.PartId
        && string.Equals(binding.MaterialKey, materialKey, StringComparison.Ordinal)
        && string.Equals(binding.ConfiguredMaterialKey, part.Configuration.MaterialKey, StringComparison.Ordinal)
        && binding.Quantity == part.Configuration.Quantity
        && binding.BuildPreference == part.Configuration.BuildPreference
        && binding.FileId == part.PhysicalAnalysisUpload?.FileId
        && string.Equals(binding.UploadSha256, part.PhysicalAnalysisUpload?.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool IsAdmittedStl(InstantQuotationPart part) =>
        part.PhysicalAnalysisUpload is { Status: "clean" } upload
        && Path.GetExtension(upload.FileName).Equals(".stl", StringComparison.OrdinalIgnoreCase)
        && upload.FileId != Guid.Empty
        && string.Equals(upload.Sha256, part.Geometry.Sha256, StringComparison.OrdinalIgnoreCase);
}
