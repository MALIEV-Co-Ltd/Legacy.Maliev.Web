namespace Legacy.Maliev.Web.Application;

public sealed record InstantQuotationCustomerSubmission(
    string FirstName,
    string LastName,
    string Email,
    string? TelephoneNumber,
    string Country,
    string? CompanyName,
    string? TaxIdentification,
    string? Description,
    string? MobileNumber = null,
    string? BillingBuilding = null,
    string? BillingAddressLine1 = null,
    string? BillingAddressLine2 = null,
    string? BillingCity = null,
    string? BillingProvince = null,
    string? BillingPostalCode = null,
    bool ShipToBillingAddress = true,
    string? ShippingBuilding = null,
    string? ShippingAddressLine1 = null,
    string? ShippingAddressLine2 = null,
    string? ShippingCity = null,
    string? ShippingProvince = null,
    string? ShippingPostalCode = null,
    string? ShippingCountry = null);

public enum InstantQuotationSubmissionCheckpointStatus
{
    Persisted,
    FilesLinked,
    CustomerProvisioned,
    OrdersProvisioning,
    OrdersProvisioned,
    IdentityPrepared,
    IdentityProvisioned,
    WelcomePrepared,
    WelcomeNotificationSent,
    Completed,
}

public sealed record InstantQuotationSubmissionCheckpoint(
    string SubmissionId,
    int RequestReference,
    InstantQuotationSubmissionCheckpointStatus Status,
    string SnapshotDigest,
    IReadOnlyList<InstantQuotationFinalizedFile>? FinalizedFiles = null,
    int? CustomerId = null,
    bool CustomerCreated = false,
    string? TemporaryPassword = null,
    bool IdentityCreated = false,
    IReadOnlyList<int>? OrderIds = null,
    string? WelcomeConfirmationToken = null,
    bool CompensationRequired = false);

public sealed record InstantQuotationSubmissionCheckpointRead(
    bool LeaseValid,
    InstantQuotationSubmissionCheckpoint? Checkpoint);

public interface IInstantQuotationSubmissionLease : IAsyncDisposable
{
    Task<bool> RenewAsync(CancellationToken cancellationToken);

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
        string? ownerIdentity,
        InstantQuotationCustomerSubmission customer,
        CancellationToken cancellationToken);
}
