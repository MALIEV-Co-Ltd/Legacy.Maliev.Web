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
    double MotionSeconds)
{
    internal static InstantQuotationPhysicalAnalysisReceipt Create(
        InstantQuotationPhysicalAnalysisBinding binding,
        string configuredMaterialKey,
        SimulationResult physical)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            physical.AnalysisVersion,
            physical.ProfileSha256,
            physical.TotalDepositedMm3.ToString("R", CultureInfo.InvariantCulture),
            physical.SupportDepositedMm3.ToString("R", CultureInfo.InvariantCulture),
            physical.Motion.TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
            string.Join(',', physical.Diagnostics.Order(StringComparer.Ordinal))))));
        return new(binding.SessionId, binding.OwnerIdentity, binding.PartId, binding.FileId,
            binding.UploadSha256, configuredMaterialKey, binding.MaterialKey, binding.BuildPreference,
            binding.Quantity, binding.ProfileVersion, physical.ProfileSha256, physical.AnalysisVersion,
            digest, physical.TotalDepositedMm3, physical.SupportDepositedMm3, physical.Motion.TotalSeconds);
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
