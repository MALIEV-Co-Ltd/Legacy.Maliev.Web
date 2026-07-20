using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Legacy.Maliev.Web.Infrastructure")]
[assembly: InternalsVisibleTo("Legacy.Maliev.Web")]

namespace Legacy.Maliev.Web.Application;

public enum InstantQuotationServiceStatus
{
    Available,
    Unavailable,
}

public enum InstantQuotationAuthorizationStatus
{
    NotEvaluated,
    Authorized,
    Denied,
}

public enum InstantQuotationOperationStatus
{
    Succeeded,
    Failed,
}

public enum InstantQuotationProblemCategory
{
    None,
    DependencyUnavailable,
    Authorization,
    Validation,
    Conflict,
    Unexpected,
}

internal enum InstantQuotationUploadRetryDisposition
{
    None,
    RetryIdentical,
    RetryWithBackoff,
}

public sealed record InstantQuotationUploadReference(string Value);

internal sealed record InstantQuotationFinalizedFile(
    Guid FileId,
    string Bucket,
    string ObjectName,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256);

internal sealed record InstantQuotationRequestFileLinkResult(
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory);

internal interface IInstantQuotationRequestFileClient
{
    Task<InstantQuotationRequestFileLinkResult> LinkAsync(
        int quotationRequestId,
        InstantQuotationFinalizedFile file,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public interface IInstantQuotationUploadClient
{
    Task<InstantQuotationUploadResult> UploadAsync(
        string sessionId,
        string? ownerIdentity,
        Stream content,
        string fileName,
        string contentType,
        long contentLength,
        InstantQuotationGeometryClaim geometryClaim,
        string operationId,
        CancellationToken cancellationToken);

    Task<InstantQuotationRemoveResult> RemoveAsync(
        string sessionId,
        string? ownerIdentity,
        InstantQuotationUploadReference uploadReference,
        string operationId,
        CancellationToken cancellationToken);

    Task<InstantQuotationFinalizationResult> FinalizeAsync(
        string sessionId,
        string? ownerIdentity,
        int quotationRequestId,
        IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
        string operationId,
        CancellationToken cancellationToken);
}

public sealed record InstantQuotationUploadResult
{
    private InstantQuotationUploadResult(
        string operationId,
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationOperationStatus status,
        InstantQuotationProblemCategory problemCategory,
        InstantQuotationUploadReference? uploadReference,
        string? contentSha256,
        InstantQuotationUploadRetryDisposition retryDisposition)
    {
        OperationId = operationId;
        ServiceStatus = serviceStatus;
        AuthorizationStatus = authorizationStatus;
        Status = status;
        ProblemCategory = problemCategory;
        UploadReference = uploadReference;
        ContentSha256 = contentSha256;
        RetryDisposition = retryDisposition;
    }

    public string OperationId { get; }

    public InstantQuotationServiceStatus ServiceStatus { get; }

    public InstantQuotationAuthorizationStatus AuthorizationStatus { get; }

    public InstantQuotationOperationStatus Status { get; }

    public InstantQuotationProblemCategory ProblemCategory { get; }

    public InstantQuotationUploadReference? UploadReference { get; }

    public string? ContentSha256 { get; }

    internal InstantQuotationUploadRetryDisposition RetryDisposition { get; }

    internal static InstantQuotationUploadResult Succeeded(
        string operationId,
        InstantQuotationUploadReference uploadReference,
        string contentSha256) => new(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.Authorized,
            InstantQuotationOperationStatus.Succeeded,
            InstantQuotationProblemCategory.None,
            uploadReference,
            contentSha256,
            InstantQuotationUploadRetryDisposition.None);

    internal static InstantQuotationUploadResult Failed(
        string operationId,
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationProblemCategory problemCategory,
        InstantQuotationUploadRetryDisposition retryDisposition = InstantQuotationUploadRetryDisposition.None) => new(
            operationId,
            serviceStatus,
            authorizationStatus,
            InstantQuotationOperationStatus.Failed,
            problemCategory,
            null,
            null,
            retryDisposition);

    public static InstantQuotationUploadResult Unavailable(string operationId) => new(
        operationId,
        InstantQuotationServiceStatus.Unavailable,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationOperationStatus.Failed,
        InstantQuotationProblemCategory.DependencyUnavailable,
        null,
        null,
        InstantQuotationUploadRetryDisposition.None);
}

public sealed record InstantQuotationRemoveResult(
    string OperationId,
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory)
{
    internal static InstantQuotationRemoveResult Failed(
        string operationId,
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationProblemCategory problemCategory) => new(
            operationId,
            serviceStatus,
            authorizationStatus,
            InstantQuotationOperationStatus.Failed,
            problemCategory);

    public static InstantQuotationRemoveResult Unavailable(string operationId) => new(
        operationId,
        InstantQuotationServiceStatus.Unavailable,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationOperationStatus.Failed,
        InstantQuotationProblemCategory.DependencyUnavailable);
}

public sealed record InstantQuotationFinalizationResult(
    string OperationId,
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory)
{
    internal IReadOnlyList<InstantQuotationFinalizedFile> Files { get; init; } = [];

    internal static InstantQuotationFinalizationResult Succeeded(
        string operationId,
        IReadOnlyList<InstantQuotationFinalizedFile> files) => new(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.Authorized,
            InstantQuotationOperationStatus.Succeeded,
            InstantQuotationProblemCategory.None)
        {
            Files = files,
        };

    internal static InstantQuotationFinalizationResult Failed(
        string operationId,
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationProblemCategory problemCategory) => new(
            operationId,
            serviceStatus,
            authorizationStatus,
            InstantQuotationOperationStatus.Failed,
            problemCategory);

    public static InstantQuotationFinalizationResult Unavailable(string operationId) => new(
        operationId,
        InstantQuotationServiceStatus.Unavailable,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationOperationStatus.Failed,
        InstantQuotationProblemCategory.DependencyUnavailable);
}
