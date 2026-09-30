using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.Web.Application;

internal sealed class InstantQuotationAuthenticatedPreparationService(
    IInstantQuotationSessionStore sessionStore, IInstantQuotationSubmissionStore submissionStore,
    IInstantQuotationProfileCompletionClient profiles, ICountryClient countryClient)
    : IInstantQuotationAuthenticatedPreparationService
{
    public async Task<InstantQuotationAuthenticatedPreparation> PrepareAsync(string sessionId, string ownerIdentity,
        InstantQuotationProfileDetails posted, string? description, CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessionStore.GetAsync(sessionId, ownerIdentity, cancellationToken);
            if (session is null) return Failure(InstantQuotationProblemCategory.Authorization, safe: true);
            await using var lease = await submissionStore.TryAcquireAsync(session.SubmissionId, ownerIdentity, cancellationToken);
            if (lease is null) return Failure(InstantQuotationProblemCategory.Conflict, safe: false);
            var read = await lease.ReadAsync(cancellationToken);
            if (!read.LeaseValid) return Failure(InstantQuotationProblemCategory.Conflict, safe: false);
            if (read.Checkpoint is { } checkpoint)
            {
                var terminal = new InstantQuotationSubmissionResult(
                    checkpoint.Status == InstantQuotationSubmissionCheckpointStatus.Completed
                        ? InstantQuotationSubmissionOutcome.Completed : InstantQuotationSubmissionOutcome.Partial,
                    checkpoint.RequestReference, InstantQuotationProblemCategory.DependencyUnavailable,
                    checkpoint.TransactionId, checkpoint.JourneyId);
                if (checkpoint.Status == InstantQuotationSubmissionCheckpointStatus.Completed || checkpoint.FrozenCustomer?.ProfileCompletion is null)
                    return new(null, null, terminal, terminal.ProblemCategory, false);
                // A durable retry MUST retain the originally frozen request, key and graph version.
                // Never refresh/merge against the now-mutated producer graph and generate a new digest.
                return new(checkpoint.FrozenCustomer, InstantQuotationProfileDetails.FromSubmission(checkpoint.FrozenCustomer),
                    terminal, InstantQuotationProblemCategory.None, false);
            }

            var loaded = await profiles.ReadAsync(ownerIdentity, cancellationToken);
            if (loaded.Graph is null) return Failure(loaded.ProblemCategory, safe: true);
            var countries = await countryClient.GetCountriesAsync(cancellationToken);
            if (!countries.ServiceAvailable || countries.Value is null) return Failure(InstantQuotationProblemCategory.DependencyUnavailable, safe: true);
            InstantQuotationAuthenticatedProfile profile;
            try
            {
                profile = InstantQuotationAuthenticatedProfile.Create(loaded.Graph.Customer, countries.Value,
                    loaded.Graph.TrustedEmail, loaded.Graph.TrustedMobile);
            }
            catch (ArgumentException)
            {
                return Failure(InstantQuotationProblemCategory.DependencyUnavailable, safe: true);
            }
            var details = profile.MergeMissing(posted);
            if (details.TaxBranch == "branch" && details.TaxBranchCode.Length is >= 1 and <= 5
                && details.TaxBranchCode.All(character => character is >= '0' and <= '9'))
                details = details with { TaxBranchCode = details.TaxBranchCode.PadLeft(5, '0') };
            var billingId = CountryId(details.Country, countries.Value);
            var shippingId = details.ShipToBillingAddress ? billingId : CountryId(details.ShippingCountry, countries.Value);
            if (billingId is null || shippingId is null) return Failure(InstantQuotationProblemCategory.Validation, safe: true);
            var body = new InstantQuotationProfileCompletionBody(
                Null(details.FirstName), Null(details.LastName), Null(details.Telephone), Null(details.Mobile), Null(details.Company),
                Null(details.TaxNumber),
                new(Null(details.BillingBuilding), details.BillingStreet1, Null(details.BillingStreet2), Null(details.BillingCity),
                    Null(details.BillingProvince), Null(details.BillingPostalCode), billingId.Value),
                details.ShipToBillingAddress ? null : new(Null(details.ShippingBuilding), details.ShippingStreet1,
                    Null(details.ShippingStreet2), Null(details.ShippingCity), Null(details.ShippingProvince),
                    Null(details.ShippingPostalCode), shippingId.Value), details.ShipToBillingAddress,
                string.IsNullOrWhiteSpace(details.TaxNumber) ? null : details.TaxBranch,
                !string.IsNullOrWhiteSpace(details.TaxNumber) && details.TaxBranch == "branch" ? Null(details.TaxBranchCode) : null);
            var key = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"quotation-profile-completion-v2:{ownerIdentity}:{session.SubmissionId}"))[..16]);
            var customer = details.ToSubmission(description) with
            {
                ProfileCompletion = new(loaded.Graph.Customer.Id, key, loaded.Graph.EntityTag, body),
            };
            return new(customer, details, null, InstantQuotationProblemCategory.None, true) { LockedFields = profile.LockedFields };
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Unknown checkpoint state cannot be represented as an editable pre-durable retry.
            return Failure(InstantQuotationProblemCategory.DependencyUnavailable, safe: false);
        }
    }

    private static InstantQuotationAuthenticatedPreparation Failure(InstantQuotationProblemCategory category, bool safe) => new(null, null, null, category, safe);
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int? CountryId(string name, IReadOnlyList<Country> countries) =>
        countries.Where(country => string.Equals(country.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray() is [var match]
            && match.Id > 0 ? match.Id : null;
}
