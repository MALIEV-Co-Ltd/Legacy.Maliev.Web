using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal enum CncAuthenticatedProfileLoadOutcome
{
    Anonymous,
    Loaded,
    Rejected,
    Unavailable,
}

internal sealed record CncAuthenticatedProfileLoadResult(
    CncAuthenticatedProfileLoadOutcome Outcome,
    CncAuthenticatedProfile? Profile = null,
    CustomerAccountDetails? Customer = null);

/// <summary>Loads a CNC profile only after the cookie session and Auth self identity agree.</summary>
internal sealed class CncAuthenticatedProfileLoader(
    IAccountSessionManager sessions,
    ICustomerAuthenticationClient authentication,
    ICustomerAccountClient customers,
    ICountryClient countries)
{
    internal async Task<CncAuthenticatedProfileLoadResult> LoadAsync(
        HttpContext context,
        ModelStateDictionary errors,
        CancellationToken cancellationToken)
    {
        string? accessToken = await sessions.GetAccessTokenAsync(context, cancellationToken);
        int? customerId = await sessions.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken) && customerId is null)
        {
            return new(CncAuthenticatedProfileLoadOutcome.Anonymous);
        }

        if (string.IsNullOrWhiteSpace(accessToken) || customerId is null || customerId <= 0)
        {
            errors.AddModelError(string.Empty, "We could not verify your signed-in account. Please sign in again.");
            return new(CncAuthenticatedProfileLoadOutcome.Rejected);
        }

        CustomerSelfIdentityResult self = await authentication.GetSelfIdentityAsync(
            accessToken,
            customerId.Value,
            cancellationToken);
        if (self.Status is CustomerSelfIdentityStatus.Unavailable)
        {
            errors.AddModelError(string.Empty, "Account security is temporarily unavailable. Please try again.");
            return new(CncAuthenticatedProfileLoadOutcome.Unavailable);
        }

        if (self.Status is not CustomerSelfIdentityStatus.Succeeded
            || self.Identity is null
            || self.Identity.CustomerId != customerId.Value)
        {
            errors.AddModelError(string.Empty, "We could not verify your signed-in account. Please sign in again.");
            return new(CncAuthenticatedProfileLoadOutcome.Rejected);
        }

        Task<CustomerAccountProfileResult> customerTask = customers.GetProfileAsync(customerId.Value, cancellationToken);
        Task<ServiceResponse<IReadOnlyList<Country>>> countryTask = countries.GetCountriesAsync(cancellationToken);
        await Task.WhenAll(customerTask, countryTask);
        CustomerAccountProfileResult customer = await customerTask;
        ServiceResponse<IReadOnlyList<Country>> countryList = await countryTask;
        if (!customer.ServiceAvailable || !countryList.ServiceAvailable)
        {
            errors.AddModelError(string.Empty, "We could not load your customer profile. Please try again.");
            return new(CncAuthenticatedProfileLoadOutcome.Unavailable);
        }

        if (!customer.Authorized || customer.Profile is null)
        {
            errors.AddModelError(string.Empty, "We could not load your customer profile. Please sign in again.");
            return new(CncAuthenticatedProfileLoadOutcome.Rejected);
        }

        var identity = new CncAuthenticatedIdentity(
            self.Identity.CustomerId,
            self.Identity.Email,
            self.Identity.Mobile);
        if (!CncAuthenticatedProfile.TryCreate(identity, customer.Profile, countryList.Value, errors, out CncAuthenticatedProfile? profile)
            || profile is null)
        {
            return new(CncAuthenticatedProfileLoadOutcome.Rejected);
        }

        return new(CncAuthenticatedProfileLoadOutcome.Loaded, profile, customer.Profile);
    }
}
