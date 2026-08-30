namespace Legacy.Maliev.Web.Application;

/// <summary>Result of selecting or provisioning the customer that owns an instant quotation.</summary>
public sealed record InstantQuotationCustomerProvisionResult(
    int? CustomerId,
    bool CustomerCreated,
    bool ServiceAvailable,
    bool Authorized);

/// <summary>Result of selecting or provisioning the customer's authentication identity.</summary>
public sealed record InstantQuotationIdentityProvisionResult(
    bool IdentityCreated,
    bool ServiceAvailable,
    bool Authorized);

/// <summary>Result of replay-safe creation, initial-status assignment, and file linking for one part.</summary>
public sealed record InstantQuotationOrderProvisionResult(
    int? OrderId,
    bool Succeeded,
    bool ServiceAvailable,
    bool Authorized,
    bool Conflict);

/// <summary>Prepared, protected confirmation material used to reproduce an exact welcome email on retry.</summary>
public sealed record InstantQuotationWelcomePreparationResult(
    string? ConfirmationToken,
    bool ServiceAvailable,
    bool Authorized);

/// <summary>Cross-service fulfillment operations owned by the Legacy Web BFF.</summary>
public interface IInstantQuotationFulfillmentClient
{
    /// <summary>Selects the authenticated customer or atomically provisions a guest profile.</summary>
    Task<InstantQuotationCustomerProvisionResult> ProvisionCustomerAsync(
        string? ownerIdentity,
        InstantQuotationCustomerSubmission customer,
        CancellationToken cancellationToken);

    /// <summary>Creates or reuses the customer identity without blocking order fulfillment when Auth is unavailable.</summary>
    Task<InstantQuotationIdentityProvisionResult> ProvisionIdentityAsync(
        int customerId,
        InstantQuotationCustomerSubmission customer,
        string temporaryPassword,
        CancellationToken cancellationToken);

    /// <summary>Creates or replays exactly one order and its initial status/file link for one authoritative part.</summary>
    Task<InstantQuotationOrderProvisionResult> ProvisionOrderAsync(
        string submissionId,
        int partIndex,
        int customerId,
        string? customerDescription,
        InstantQuotationPart part,
        InstantQuotationPartQuote quote,
        int leadTimeDays,
        InstantQuotationFinalizedFile file,
        CancellationToken cancellationToken);

    /// <summary>Deletes or confirms absence of a partially provisioned order.</summary>
    Task<bool> CompensateOrderAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Deletes a guest customer graph only when the fulfillment created it.</summary>
    Task<bool> CompensateCustomerAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>Creates the confirmation challenge before the notification payload is checkpointed.</summary>
    Task<InstantQuotationWelcomePreparationResult> PrepareWelcomeAsync(
        InstantQuotationCustomerSubmission customer,
        CancellationToken cancellationToken);

    /// <summary>Sends the welcome and confirmation email through an idempotent notification operation.</summary>
    Task<NotificationResult> SendWelcomeAsync(
        InstantQuotationCustomerSubmission customer,
        string temporaryPassword,
        string confirmationToken,
        Guid operationId,
        CancellationToken cancellationToken);
}
