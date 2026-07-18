using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class UnavailableInstantQuotationUploadClient : IInstantQuotationUploadClient
{
    public Task<InstantQuotationUploadResult> UploadAsync(
        string sessionId,
        Stream content,
        string fileName,
        string contentType,
        long contentLength,
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InstantQuotationUploadResult.Unavailable(operationId));
    }

    public Task<InstantQuotationRemoveResult> RemoveAsync(
        string sessionId,
        InstantQuotationUploadReference uploadReference,
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InstantQuotationRemoveResult.Unavailable(operationId));
    }

    public Task<InstantQuotationFinalizationResult> FinalizeAsync(
        string sessionId,
        IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
        string operationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(InstantQuotationFinalizationResult.Unavailable(operationId));
    }
}
