using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Legacy.Maliev.Web.Infrastructure")]

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
        string operationId,
        CancellationToken cancellationToken);

    Task<InstantQuotationRemoveResult> RemoveAsync(
        string sessionId,
        InstantQuotationUploadReference uploadReference,
        string operationId,
        CancellationToken cancellationToken);

    Task<InstantQuotationFinalizationResult> FinalizeAsync(
        string sessionId,
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
        AuthoritativeInstantQuotationGeometry? authoritativeGeometry)
    {
        OperationId = operationId;
        ServiceStatus = serviceStatus;
        AuthorizationStatus = authorizationStatus;
        Status = status;
        ProblemCategory = problemCategory;
        UploadReference = uploadReference;
        AuthoritativeGeometry = authoritativeGeometry;
    }

    public string OperationId { get; }

    public InstantQuotationServiceStatus ServiceStatus { get; }

    public InstantQuotationAuthorizationStatus AuthorizationStatus { get; }

    public InstantQuotationOperationStatus Status { get; }

    public InstantQuotationProblemCategory ProblemCategory { get; }

    public InstantQuotationUploadReference? UploadReference { get; }

    public AuthoritativeInstantQuotationGeometry? AuthoritativeGeometry { get; }

    internal static InstantQuotationUploadResult Succeeded(
        string operationId,
        InstantQuotationUploadReference uploadReference,
        InstantQuotationGeometry geometry) => new(
            operationId,
            InstantQuotationServiceStatus.Available,
            InstantQuotationAuthorizationStatus.Authorized,
            InstantQuotationOperationStatus.Succeeded,
            InstantQuotationProblemCategory.None,
            uploadReference,
            AuthoritativeInstantQuotationGeometry.FromSuccessfulUpload(geometry));

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
