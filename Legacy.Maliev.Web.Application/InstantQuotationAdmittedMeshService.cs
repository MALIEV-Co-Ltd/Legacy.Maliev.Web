namespace Legacy.Maliev.Web.Application;

using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

internal enum InstantQuotationAdmittedMeshFailure
{
    None,
    UploadUnavailable,
    UploadChanged,
    UnsupportedFormat,
    InvalidMesh,
}

/// <summary>Server-only mesh evidence; this does not authorize a customer price or ticket.</summary>
internal sealed record InstantQuotationAdmittedMeshResult(
    NormalizedMesh? Mesh,
    string? UploadSha256,
    InstantQuotationAdmittedMeshFailure Failure)
{
    internal bool IsReady => Mesh is not null && Failure == InstantQuotationAdmittedMeshFailure.None;

    internal static InstantQuotationAdmittedMeshResult Unavailable(InstantQuotationAdmittedMeshFailure failure) =>
        new(null, null, failure);
}

/// <summary>
/// Converts only exact, owner-authorized, clean FileService bytes into the trusted simulation mesh.
/// The caller must match the returned digest to its current part revision before consuming the result.
/// </summary>
internal sealed class InstantQuotationAdmittedMeshService(
    IInstantQuotationPhysicalAnalysisInputReader inputReader)
{
    internal async Task<InstantQuotationAdmittedMeshResult> ReadAsync(
        string sessionId,
        string? ownerIdentity,
        Guid partId,
        Guid expectedFileId,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sessionId)
            || partId == Guid.Empty
            || expectedFileId == Guid.Empty
            || expectedSha256 is not { Length: 64 })
        {
            return InstantQuotationAdmittedMeshResult.Unavailable(InstantQuotationAdmittedMeshFailure.UploadUnavailable);
        }

        InstantQuotationPhysicalAnalysisInputResult input = await inputReader.ReadAsync(
            sessionId,
            ownerIdentity,
            partId,
            cancellationToken);
        if (!input.IsReady)
        {
            return InstantQuotationAdmittedMeshResult.Unavailable(InstantQuotationAdmittedMeshFailure.UploadUnavailable);
        }

        if (input.FileId != expectedFileId
            || !string.Equals(input.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Convert.ToHexString(SHA256.HashData(input.Content!)),
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return InstantQuotationAdmittedMeshResult.Unavailable(InstantQuotationAdmittedMeshFailure.UploadChanged);
        }

        if (!string.Equals(Path.GetExtension(input.FileName), ".stl", StringComparison.OrdinalIgnoreCase))
        {
            return InstantQuotationAdmittedMeshResult.Unavailable(InstantQuotationAdmittedMeshFailure.UnsupportedFormat);
        }

        try
        {
            MeshInput parsed = AdmittedStlMeshReader.Read(input.Content!, cancellationToken);
            NormalizedMesh mesh = MeshNormalizer.Normalize(
                parsed, new GeometryTolerance(0.001, 0.02), cancellationToken);
            return new InstantQuotationAdmittedMeshResult(mesh, expectedSha256, InstantQuotationAdmittedMeshFailure.None);
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException or SimulationGeometryException)
        {
            return InstantQuotationAdmittedMeshResult.Unavailable(InstantQuotationAdmittedMeshFailure.InvalidMesh);
        }
    }
}
