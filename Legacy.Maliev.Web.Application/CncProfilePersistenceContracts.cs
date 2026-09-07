namespace Legacy.Maliev.Web.Application;

/// <summary>Address completion values from admitted CNC fields; country authority is resolved separately.</summary>
internal sealed record CncProfileAddressFields(string? Building, string AddressLine1, string? AddressLine2,
    string? City, string? State, string? PostalCode);

/// <summary>Server-side merged and validated contact values, never a browser-bound persistence command.</summary>
internal sealed record CncProfileCompletion(string FirstName, string LastName, string Email, string Mobile,
    string? Telephone, string Company, string FormattedTaxNumber, CncProfileAddressFields Billing,
    string Country, bool ShipToBillingAddress, CncProfileAddressFields? Shipping, string? ShippingCountry);

/// <summary>
/// Prepared claim-bound account snapshot and completion for an already-created quotation.
/// The coordinator must verify session/self-identity ownership and admission before construction.
/// This snapshot is not a concurrency token.
/// </summary>
internal sealed record CncProfilePersistenceRequest(int QuotationRequestId, int AuthenticatedCustomerId,
    CustomerAccountDetails PreparedCustomer, CncProfileCompletion Completion);

internal enum CncProfilePersistenceOutcome
{
    Completed,
    Failed,
    Unknown,
}

internal enum CncProfilePersistenceStage
{
    Preparation,
    Company,
    BillingCountry,
    BillingAddress,
    ShippingCountry,
    ShippingAddress,
    Customer,
    Complete,
}

/// <summary>
/// Every result is terminal for the existing quotation: never replay it, restore upload claims,
/// or compensate profile records. Confirmed IDs permit reconciliation without exposing contact data.
/// </summary>
internal sealed record CncProfilePersistenceResult(int QuotationRequestId, CncProfilePersistenceOutcome Outcome,
    CncProfilePersistenceStage Stage, bool HasConfirmedChanges = false, int? CreatedCompanyId = null,
    int? CreatedBillingAddressId = null, int? CreatedShippingAddressId = null, int? HttpStatusCode = null);

internal interface ICncProfilePersistenceClient
{
    Task<CncProfilePersistenceResult> CompleteAsync(CncProfilePersistenceRequest request, CancellationToken cancellationToken);
}
