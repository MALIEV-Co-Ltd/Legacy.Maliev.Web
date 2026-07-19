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

public sealed record InstantQuotationUploadReference(string Value);

public interface IInstantQuotationUploadClient
{
    Task<InstantQuotationUploadResult> UploadAsync(
        string sessionId,
        Stream content,
        string fileName,
        string contentType,
        long contentLength,
        InstantQuotationGeometryClaim geometryClaim,
        string operationId,
        CancellationToken cancellationToken);

    Task<InstantQuotationRemoveResult> RemoveAsync(
        string sessionId,
        InstantQuotationUploadReference uploadReference,
        string operationId,
        CancellationToken cancellationToken);

    Task<InstantQuotationFinalizationResult> FinalizeAsync(
        string sessionId,
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
        string? contentSha256)
    {
        OperationId = operationId;
        ServiceStatus = serviceStatus;
        AuthorizationStatus = authorizationStatus;
        Status = status;
        ProblemCategory = problemCategory;
        UploadReference = uploadReference;
        ContentSha256 = contentSha256;
    }

    public string OperationId { get; }

    public InstantQuotationServiceStatus ServiceStatus { get; }

    public InstantQuotationAuthorizationStatus AuthorizationStatus { get; }

    public InstantQuotationOperationStatus Status { get; }

    public InstantQuotationProblemCategory ProblemCategory { get; }

    public InstantQuotationUploadReference? UploadReference { get; }

    public string? ContentSha256 { get; }

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
            contentSha256);

    public static InstantQuotationUploadResult Unavailable(string operationId) => new(
        operationId,
        InstantQuotationServiceStatus.Unavailable,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationOperationStatus.Failed,
        InstantQuotationProblemCategory.DependencyUnavailable,
        null,
        null);
}

public sealed record InstantQuotationRemoveResult(
    string OperationId,
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory)
{
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
    public static InstantQuotationFinalizationResult Unavailable(string operationId) => new(
        operationId,
        InstantQuotationServiceStatus.Unavailable,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationOperationStatus.Failed,
        InstantQuotationProblemCategory.DependencyUnavailable);
}
