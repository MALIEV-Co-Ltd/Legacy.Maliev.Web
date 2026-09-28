using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationPhysicalAnalysisInputReader(
    IInstantQuotationSessionStore sessionStore,
    IInstantQuotationFileCapabilityStore capabilityStore,
    InstantQuotationFileServiceTransport transport) : IInstantQuotationPhysicalAnalysisInputReader
{
    private const long MaximumPhysicalAnalysisBytes = 32L * 1024 * 1024;

    public async Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
        string sessionId,
        string? ownerIdentity,
        Guid partId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sessionId) || partId == Guid.Empty)
        {
            return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable);
        }

        try
        {
            var session = await sessionStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            if (session is null)
            {
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable);
            }

            var part = session.Parts.SingleOrDefault(candidate => candidate.PartId == partId);
            if (part?.PhysicalAnalysisUpload is not { } upload)
            {
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable);
            }

            if (upload.FileId == Guid.Empty
                || !string.Equals(upload.FileId.ToString("D"), part.UploadReference.Value, StringComparison.Ordinal)
                || !string.Equals(upload.Sha256, part.Geometry.Sha256, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(upload.FileName)
                || string.IsNullOrWhiteSpace(upload.ContentType))
            {
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.UploadMismatch);
            }

            if (!string.Equals(upload.Status, "clean", StringComparison.Ordinal))
            {
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.UploadNotClean);
            }

            if (upload.SizeBytes is <= 0 or > MaximumPhysicalAnalysisBytes)
            {
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.UploadTooLarge);
            }

            var capability = await capabilityStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            if (capability is null)
            {
                return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.CapabilityUnavailable);
            }

            var file = new InstantQuotationFileServiceUploadedFile(
                upload.FileId,
                upload.FileName,
                upload.ContentType,
                upload.SizeBytes,
                upload.Sha256,
                upload.Status);
            var read = await transport.ReadCleanForAnalysisAsync(capability, file, cancellationToken);
            return read.Status == InstantQuotationOperationStatus.Succeeded && read.Content is not null
                ? new InstantQuotationPhysicalAnalysisInputResult(
                    read.Content,
                    InstantQuotationPhysicalAnalysisInputFailure.None,
                    upload.FileId,
                    upload.FileName,
                    upload.Sha256)
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                    InstantQuotationPhysicalAnalysisInputFailure.ContentUnavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return InstantQuotationPhysicalAnalysisInputResult.Unavailable(
                InstantQuotationPhysicalAnalysisInputFailure.ContentUnavailable);
        }
    }
}
