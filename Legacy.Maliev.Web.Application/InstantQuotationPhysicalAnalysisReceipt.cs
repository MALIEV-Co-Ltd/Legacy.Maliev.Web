using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Application;

/// <summary>Immutable server-produced physical evidence for one admitted upload and one FDM configuration.</summary>
public sealed record InstantQuotationPhysicalAnalysisReceipt(
    string SessionId,
    string? OwnerIdentity,
    Guid PartId,
    Guid FileId,
    string UploadSha256,
    string ConfiguredMaterialKey,
    string MaterialKey,
    Pricing.BuildPreference BuildPreference,
    int Quantity,
    string ProfileVersion,
    string ProfileSha256,
    string AnalysisVersion,
    string PhysicalSha256,
    double DepositedMm3,
    double SupportMm3,
    double MotionSeconds,
    double BoundingCm3PerUnit)
{
    /// <summary>Gets pricing-only reconstruction evidence bound into the protected physical digest.</summary>
    public VoxelReconstructionInfo? GeometryReconstruction { get; init; }

    internal static InstantQuotationPhysicalAnalysisReceipt Create(
        InstantQuotationPhysicalAnalysisBinding binding,
        string configuredMaterialKey,
        SimulationResult physical,
        double boundingCm3PerUnit)
    {
        if (physical.GeometryReconstruction is { } reconstruction && !reconstruction.IsValid())
            throw new ArgumentException("Reconstruction provenance is invalid.", nameof(physical));
        string canonical = string.Join('|',
            physical.AnalysisVersion,
            physical.ProfileSha256,
            physical.TotalDepositedMm3.ToString("R", CultureInfo.InvariantCulture),
            physical.SupportDepositedMm3.ToString("R", CultureInfo.InvariantCulture),
            physical.Motion.TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
            boundingCm3PerUnit.ToString("R", CultureInfo.InvariantCulture),
            string.Join(',', physical.Diagnostics.Order(StringComparer.Ordinal)));
        if (physical.GeometryReconstruction is { } provenance)
            canonical += FormattableString.Invariant($"|reconstruction:{provenance.Method}|voxel-mm:{provenance.VoxelSizeMm:R}");
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(binding.SessionId, binding.OwnerIdentity, binding.PartId, binding.FileId,
            binding.UploadSha256, configuredMaterialKey, binding.MaterialKey, binding.BuildPreference,
            binding.Quantity, binding.ProfileVersion, physical.ProfileSha256, physical.AnalysisVersion,
            digest, physical.TotalDepositedMm3, physical.SupportDepositedMm3,
            physical.Motion.TotalSeconds, boundingCm3PerUnit)
        {
            GeometryReconstruction = physical.GeometryReconstruction,
        };
    }

    internal bool Matches(InstantQuotationPhysicalAnalysisBinding binding, string configuredMaterialKey) =>
        string.Equals(SessionId, binding.SessionId, StringComparison.Ordinal)
        && string.Equals(OwnerIdentity, binding.OwnerIdentity, StringComparison.Ordinal)
        && PartId == binding.PartId && FileId == binding.FileId
        && string.Equals(UploadSha256, binding.UploadSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ConfiguredMaterialKey, configuredMaterialKey, StringComparison.Ordinal)
        && string.Equals(MaterialKey, binding.MaterialKey, StringComparison.Ordinal)
        && BuildPreference == binding.BuildPreference && Quantity == binding.Quantity
        && string.Equals(ProfileVersion, binding.ProfileVersion, StringComparison.Ordinal);
}
