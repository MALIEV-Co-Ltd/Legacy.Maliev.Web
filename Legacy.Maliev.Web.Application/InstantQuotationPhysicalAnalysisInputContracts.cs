namespace Legacy.Maliev.Web.Application;

internal enum InstantQuotationPhysicalAnalysisInputFailure
{
    None,
    SessionUnavailable,
    UploadUnavailable,
    UploadMismatch,
    UploadNotClean,
    UploadTooLarge,
    CapabilityUnavailable,
    ContentUnavailable,
}

internal sealed record InstantQuotationPhysicalAnalysisInputResult(
    byte[]? Content,
    InstantQuotationPhysicalAnalysisInputFailure Failure)
{
    internal bool IsReady => Content is not null && Failure == InstantQuotationPhysicalAnalysisInputFailure.None;

    internal static InstantQuotationPhysicalAnalysisInputResult Unavailable(
        InstantQuotationPhysicalAnalysisInputFailure failure) => new(null, failure);
}

internal interface IInstantQuotationPhysicalAnalysisInputReader
{
    Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
        string sessionId,
        string? ownerIdentity,
        Guid partId,
        CancellationToken cancellationToken);
}
