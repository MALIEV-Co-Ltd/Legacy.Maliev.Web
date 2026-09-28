namespace Legacy.Maliev.Web.Application;

using System.Numerics;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

internal enum InstantQuotationBoundPhysicalAnalysisFailure
{
    None,
    SessionUnavailable,
    StalePart,
    ProfileUnavailable,
    UploadUnavailable,
    AnalysisUnavailable,
}

/// <summary>Server-only identity of one current upload and FDM configuration.</summary>
internal sealed record InstantQuotationPhysicalAnalysisBinding(
    string SessionId,
    string? OwnerIdentity,
    Guid PartId,
    Guid FileId,
    string UploadSha256,
    string MaterialKey,
    BuildPreference BuildPreference,
    int Quantity,
    string ProfileVersion,
    string? ConfiguredMaterialKey = null);

/// <summary>Physical evidence only. This result does not authorize money or a quote ticket.</summary>
internal sealed record InstantQuotationBoundPhysicalAnalysisResult(
    InstantQuotationPhysicalAnalysisBinding? Binding,
    SimulationResult? Physical,
    InstantQuotationBoundPhysicalAnalysisFailure Failure)
{
    internal bool IsReady => Binding is not null && Physical is not null
        && Failure == InstantQuotationBoundPhysicalAnalysisFailure.None;

    internal static InstantQuotationBoundPhysicalAnalysisResult Unavailable(
        InstantQuotationBoundPhysicalAnalysisFailure failure) => new(null, null, failure);
}

/// <summary>Runs bounded physical analysis only for the exact current, owner-scoped admitted STL.</summary>
internal sealed class InstantQuotationBoundPhysicalAnalysisService(
    IInstantQuotationSessionStore sessionStore,
    InstantQuotationAdmittedMeshService admittedMeshService,
    FdmRuntimeProfileCatalog profileCatalog)
{
    private const int MaximumTriangles = 100_000;
    private const int MaximumLayers = 2_000;
    private const int MaximumPathSegments = 1_000_000;
    private static readonly SemaphoreSlim AnalysisSlots = new(2, 2);

    internal async Task<InstantQuotationBoundPhysicalAnalysisResult> AnalyzeAsync(
        InstantQuotationPhysicalAnalysisBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(binding.SessionId) || binding.PartId == Guid.Empty)
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.SessionUnavailable);
        }

        InstantQuotationSessionState? before;
        try
        {
            before = await sessionStore.GetAsync(
                binding.SessionId, binding.OwnerIdentity, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.SessionUnavailable);
        }
        if (before is null)
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.SessionUnavailable);
        }

        if (!MatchesCurrentPart(before, binding))
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.StalePart);
        }

        if (!string.Equals(binding.ProfileVersion, profileCatalog.ProfileVersion, StringComparison.Ordinal)
            || !profileCatalog.TryResolveTrustedProfile(
                binding.MaterialKey, binding.BuildPreference, out ResolvedSimulationProfile? profile))
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.ProfileUnavailable);
        }

        InstantQuotationAdmittedMeshResult admitted = await admittedMeshService.ReadAsync(
            binding.SessionId, binding.OwnerIdentity, binding.PartId, binding.FileId,
            binding.UploadSha256, cancellationToken);
        if (!admitted.IsReady)
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.UploadUnavailable);
        }

        if (!await AnalysisSlots.WaitAsync(0, cancellationToken))
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.AnalysisUnavailable);
        }

        SimulationResult physical;
        try
        {
            physical = await Task.Run(() => FdmSimulationEngine.Estimate(new SimulationRequest(
                admitted.Mesh!, new Pose("source", Matrix4x4.Identity), profile!, binding.Quantity,
                new AnalysisBudget(MaximumTriangles, MaximumLayers, MaximumPathSegments, cancellationToken))),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.AnalysisUnavailable);
        }
        finally
        {
            AnalysisSlots.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();
        InstantQuotationSessionState? after;
        try
        {
            after = await sessionStore.GetAsync(
                binding.SessionId, binding.OwnerIdentity, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.SessionUnavailable);
        }
        if (after is null || after.UpdatedAt != before.UpdatedAt || !MatchesCurrentPart(after, binding))
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.StalePart);
        }

        if (!string.Equals(physical.ProfileSha256, profile!.ResolvedProfileSha256, StringComparison.Ordinal)
            || !string.Equals(physical.AnalysisVersion, FdmSimulationEngine.AnalysisVersion, StringComparison.Ordinal))
        {
            return InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                InstantQuotationBoundPhysicalAnalysisFailure.AnalysisUnavailable);
        }

        return new(binding, physical, InstantQuotationBoundPhysicalAnalysisFailure.None);
    }

    private static bool MatchesCurrentPart(
        InstantQuotationSessionState session,
        InstantQuotationPhysicalAnalysisBinding binding)
    {
        InstantQuotationPart[] matches = session.Parts.Where(candidate => candidate.PartId == binding.PartId)
            .Take(2).ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        InstantQuotationPart part = matches[0];
        InstantQuotationPhysicalAnalysisUpload? upload = part.PhysicalAnalysisUpload;
        return upload is not null
            && binding.FileId != Guid.Empty
            && binding.UploadSha256 is { Length: 64 }
            && binding.Quantity is >= 1 and <= PricingCatalog.MaximumAdditiveQuantity
            && upload.FileId == binding.FileId
            && string.Equals(part.UploadReference.Value, binding.FileId.ToString("D"), StringComparison.Ordinal)
            && string.Equals(upload.Status, "clean", StringComparison.Ordinal)
            && string.Equals(upload.Sha256, binding.UploadSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(part.Geometry.Sha256, binding.UploadSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(part.Configuration.MaterialKey,
                binding.ConfiguredMaterialKey ?? binding.MaterialKey, StringComparison.Ordinal)
            && part.Configuration.BuildPreference == binding.BuildPreference
            && part.Configuration.Quantity == binding.Quantity;
    }
}
