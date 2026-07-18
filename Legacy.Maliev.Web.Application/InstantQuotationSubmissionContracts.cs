namespace Legacy.Maliev.Web.Application;

public sealed record InstantQuotationCustomerSubmission(
    string FirstName,
    string LastName,
    string Email,
    string? TelephoneNumber,
    string Country,
    string? CompanyName,
    string? TaxIdentification,
    string? Description);

public enum InstantQuotationSubmissionCheckpointStatus
{
    Persisted,
    Completed,
}

public sealed record InstantQuotationSubmissionCheckpoint(
    string SubmissionId,
    int RequestReference,
    InstantQuotationSubmissionCheckpointStatus Status,
    string SnapshotDigest);

public sealed record InstantQuotationSubmissionCheckpointRead(
    bool LeaseValid,
    InstantQuotationSubmissionCheckpoint? Checkpoint);

public interface IInstantQuotationSubmissionLease : IAsyncDisposable
{
    Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(
        CancellationToken cancellationToken);

    Task<bool> TryPutAsync(
        InstantQuotationSubmissionCheckpoint checkpoint,
        InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus,
        CancellationToken cancellationToken);
}

public interface IInstantQuotationSubmissionStore
{
    Task<IInstantQuotationSubmissionLease?> TryAcquireAsync(
        string submissionId,
        string ownerIdentity,
        CancellationToken cancellationToken);

}

public enum InstantQuotationSubmissionOutcome
{
    Rejected,
    Persisted,
    Partial,
    Completed,
}

public sealed record InstantQuotationSubmissionResult(
    InstantQuotationSubmissionOutcome Outcome,
    int? RequestReference,
    InstantQuotationProblemCategory ProblemCategory);

public interface IInstantQuotationSubmissionService
{
    Task<InstantQuotationSubmissionResult> SubmitAsync(
        string sessionId,
        string ownerIdentity,
        InstantQuotationCustomerSubmission customer,
        CancellationToken cancellationToken);
}
