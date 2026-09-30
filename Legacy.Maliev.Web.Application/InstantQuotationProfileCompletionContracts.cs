namespace Legacy.Maliev.Web.Application;

public sealed record InstantQuotationProfileGraph(
    CustomerAccountDetails Customer, string EntityTag, string? TrustedEmail = null, string? TrustedMobile = null);

public sealed record InstantQuotationProfileGraphResult(
    InstantQuotationProfileGraph? Graph, InstantQuotationProblemCategory ProblemCategory);

// Deliberately excludes posted customer/identity/email authority. PascalCase producer wire contract.
public sealed record InstantQuotationProfileCompletionBody(
    string? FirstName, string? LastName, string? Telephone, string? Mobile, string? Company,
    string? TaxNumber, CustomerAddressInput? Billing, CustomerAddressInput? Shipping,
    bool ShipToBillingAddress, string? TaxBranch = null, string? TaxBranchCode = null);

public sealed record InstantQuotationProfileCompletionOperation(
    int CustomerId, Guid Key, string EntityTag, InstantQuotationProfileCompletionBody Body);

public sealed record InstantQuotationProfileCompletionResult(
    bool Succeeded, InstantQuotationProblemCategory ProblemCategory);

public interface IInstantQuotationProfileCompletionClient
{
    Task<InstantQuotationProfileGraphResult> ReadAsync(string ownerIdentity, CancellationToken cancellationToken);
    Task<InstantQuotationProfileCompletionResult> CompleteAsync(
        string ownerIdentity, InstantQuotationProfileCompletionOperation operation, CancellationToken cancellationToken);
}

public sealed record InstantQuotationAuthenticatedPreparation(
    InstantQuotationCustomerSubmission? Customer, InstantQuotationProfileDetails? Details,
    InstantQuotationSubmissionResult? TerminalResult, InstantQuotationProblemCategory ProblemCategory,
    bool SafeToRetry)
{
    public IReadOnlySet<string> LockedFields { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public interface IInstantQuotationAuthenticatedPreparationService
{
    Task<InstantQuotationAuthenticatedPreparation> PrepareAsync(string sessionId, string ownerIdentity,
        InstantQuotationProfileDetails posted, string? description, CancellationToken cancellationToken);
}
