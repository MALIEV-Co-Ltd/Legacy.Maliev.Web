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

    /// <summary>Reports awaited display-only progress without changing the final quote contract.</summary>
    Task<InstantQuotationOrderQuote?> QuoteAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        bool includeComparisons,
        Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
        CancellationToken cancellationToken) =>
        QuoteAsync(session, ownerIdentity, includeComparisons, cancellationToken);
}

/// <summary>Prices only current admitted STL evidence; unverified FDM remains unavailable.</summary>
internal sealed class InstantQuotationAuthoritativePricingService(
    InstantQuotationBoundPhysicalAnalysisService analysis,
    FdmRuntimeProfileCatalog profiles,
    InstantQuotationPricingService pricing,
    TimeProvider? timeProvider = null) : IInstantQuotationAuthoritativePricingService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<InstantQuotationOrderQuote?> QuoteAsync(
        InstantQuotationSessionState session,
        string? ownerIdentity,
        bool includeComparisons,
        Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(observer);
        cancellationToken.ThrowIfCancellationRequested();
        if (session.Parts.Count == 0 || session.Parts.Any(part => part.PartId == Guid.Empty)
            || session.Parts.Select(part => part.PartId).Distinct().Count() != session.Parts.Count)
        {
            return null;
        }

        using var absoluteDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(33), clock);
        using var absolute = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, absoluteDeadline.Token);
        using var selectedDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var selectedToken = CancellationTokenSource.CreateLinkedTokenSource(absolute.Token, selectedDeadline.Token);
        var evidence = new Dictionary<(Guid PartId, string MaterialKey), InstantQuotationBoundPhysicalAnalysisResult>();
        bool selectedPhase = true;
        bool callbackDeadlineExpired = false;
        try
        {
            foreach (var part in session.Parts)
            {
                var selected = PricingCatalog.ResolveMaterial(part.Configuration.MaterialKey);
                if (selected is null) return null;
                await PublishAsync(part, selected, InstantQuotationMaterialPricingStatus.Pending, null, selectedToken.Token);
                if (selected.Process == PrintProcess.Fdm)
                {
                    var result = await AnalyzeForProgressAsync(part, selected.Key, selectedToken.Token);
                    if (IsCurrentEvidence(result, session.SessionId, ownerIdentity, part, selected.Key))
                    {
                        evidence[(part.PartId, selected.Key)] = result;
                    }
                }
                var amount = CandidatePrice(part, selected);
                await PublishAsync(part, selected, amount is null
                    ? InstantQuotationMaterialPricingStatus.Unavailable : InstantQuotationMaterialPricingStatus.Completed,
                    amount, selectedToken.Token);
                if (amount is null) return null;
            }

            selectedPhase = false;
            if (includeComparisons)
            {
                using var comparisonDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3), clock);
                using var comparison = CancellationTokenSource.CreateLinkedTokenSource(absolute.Token, comparisonDeadline.Token);
                foreach (var part in session.Parts)
                {
                    var selectedKey = PricingCatalog.ResolveMaterial(part.Configuration.MaterialKey)!.Key;
                    foreach (var candidate in PricingCatalog.Materials.Values)
                    {
                        if (string.Equals(candidate.Key, selectedKey, StringComparison.Ordinal)) continue;
                        // The final kernel includes resin cards even without an admitted STL.
                        if (candidate.Process == PrintProcess.Fdm && !IsAdmittedStl(part)) continue;
                        if (comparison.IsCancellationRequested) break;
                        // Display delivery keeps its original absolute budget; only optional
                        // comparison analysis owns the shorter budget. Avoid comparison-budget
                        // cancellation between the display owner's check and state application.
                        await PublishAsync(part, candidate, InstantQuotationMaterialPricingStatus.Pending, null, absolute.Token);
                        if (candidate.Process == PrintProcess.Fdm)
                        {
                            InstantQuotationBoundPhysicalAnalysisResult result;
                            try
                            {
                                result = await AnalyzeForProgressAsync(part, candidate.Key, comparison.Token);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                && comparisonDeadline.IsCancellationRequested && !absoluteDeadline.IsCancellationRequested)
                            {
                                // Close the attempted card under the original absolute deadline,
                                // not a fresh analysis or callback budget.
                                await PublishAsync(part, candidate, InstantQuotationMaterialPricingStatus.Unavailable, null, absolute.Token);
                                break;
                            }
                            if (IsCurrentEvidence(result, session.SessionId, ownerIdentity, part, candidate.Key))
                                evidence[(part.PartId, candidate.Key)] = result;
                        }
                        var amount = CandidatePrice(part, candidate);
                        await PublishAsync(part, candidate, amount is null
                            ? InstantQuotationMaterialPricingStatus.Unavailable : InstantQuotationMaterialPricingStatus.Completed,
                            amount, absolute.Token);
                    }
                }
            }

            absolute.Token.ThrowIfCancellationRequested();
            try { return pricing.QuoteWithPhysical(session.RequestState, evidence); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return null; }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
            && ((selectedPhase && selectedDeadline.IsCancellationRequested)
                || absoluteDeadline.IsCancellationRequested || callbackDeadlineExpired))
        {
            return null;
        }

        double? CandidatePrice(InstantQuotationPart part, MaterialInfo candidate)
        {
            try { return pricing.MaterialUnitPrice(part, candidate, evidence); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return null; }
        }

        async Task<InstantQuotationBoundPhysicalAnalysisResult> AnalyzeForProgressAsync(
            InstantQuotationPart part, string materialKey, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try { return await AnalyzeAsync(part, materialKey, session.SessionId, ownerIdentity, token); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            { return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(InstantQuotationBoundPhysicalAnalysisFailure.AnalysisUnavailable); }
        }

        async Task PublishAsync(InstantQuotationPart part, MaterialInfo material,
            InstantQuotationMaterialPricingStatus status, double? amount, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Task callback = Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return observer(new(part.PartId, material.Key, status, amount), token).AsTask();
            }, token);
            // Observe a noncooperative callback's eventual failure after our bounded wait
            // ends. No continuation may resume analysis or create a final quote.
            _ = callback.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try { await callback.WaitAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { callbackDeadlineExpired = true; throw; }
            token.ThrowIfCancellationRequested();
        }
    }

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
        && double.IsFinite(result.BoundingCm3PerUnit) && result.BoundingCm3PerUnit > 0
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
