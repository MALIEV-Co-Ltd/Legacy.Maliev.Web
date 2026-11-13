using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Loads account profile and address view models using the current member's server-side session.
/// </summary>
public static class MemberAccountLoaders
{
    /// <summary>
    /// Loads the current member's profile and projects editable contact and company fields.
    /// </summary>
    /// <param name="context">The current authenticated request.</param>
    /// <param name="sessionManager">The server-side session accessor used to resolve the customer identifier.</param>
    /// <param name="customerClient">The customer account client used to retrieve the profile.</param>
    /// <param name="notification">The optional profile-operation outcome message retained for display.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The profile view, including errors or an empty model when profile data cannot be loaded.</returns>
    public static async Task<MemberProfileDisplayModel> LoadProfileAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerAccountClient customerClient,
        string? notification,
        CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is null) return MemberProfileDisplayModel.Empty;
        var result = await customerClient.GetProfileAsync(customerId.Value, cancellationToken);
        if (result.Profile is null)
        {
            return MemberProfileDisplayModel.Empty with
            {
                Notification = notification,
                Errors = [result.ServiceAvailable ? "Your profile could not be loaded." : "Profile service is temporarily unavailable."],
            };
        }

        var profile = result.Profile;
        return new MemberProfileDisplayModel(
            profile.FirstName,
            profile.LastName,
            profile.Email,
            profile.Telephone,
            profile.Mobile,
            profile.Fax,
            profile.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            profile.Company?.Name,
            profile.Company?.TaxNumber,
            profile.Company?.Registrar,
            notification,
            []);
    }

    /// <summary>
    /// Loads saved billing and shipping addresses alongside available country choices.
    /// </summary>
    /// <param name="context">The current authenticated request.</param>
    /// <param name="sessionManager">The server-side session accessor used to resolve the customer identifier.</param>
    /// <param name="customerClient">The account client used to retrieve saved addresses.</param>
    /// <param name="countryClient">The country lookup client used to populate address choices.</param>
    /// <param name="notification">The optional address-operation outcome message retained for display.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The address view with available fields, countries, and service-failure messages.</returns>
    public static async Task<MemberAddressDisplayModel> LoadAddressAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerAccountClient customerClient,
        ICountryClient countryClient,
        string? notification,
        CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is null) return MemberAddressDisplayModel.Empty;
        var countriesTask = countryClient.GetCountriesAsync(cancellationToken);
        var profileTask = customerClient.GetAddressProfileAsync(customerId.Value, cancellationToken);
        await Task.WhenAll(countriesTask, profileTask);
        var countries = await countriesTask;
        var profile = await profileTask;
        var errors = new List<MemberAddressError>();
        if (!countries.ServiceAvailable || countries.Value is not { Count: > 0 })
        {
            errors.Add(new(null, "Country list is temporarily unavailable."));
        }
        if (profile.Profile is null)
        {
            errors.Add(new(null, profile.ServiceAvailable
                ? "Your address profile could not be loaded."
                : "Address service is temporarily unavailable."));
        }

        return new MemberAddressDisplayModel(
            CreateAddress(profile.Profile?.Customer.BillingAddress),
            CreateAddress(profile.Profile?.Customer.ShippingAddress),
            (countries.Value ?? []).Select(country => new MemberAddressCountryOption(country.Id, country.Name)).ToArray(),
            notification,
            errors);
    }

    /// <summary>
    /// Checks the member session and account email before presenting the email-change form.
    /// </summary>
    /// <param name="context">The current authenticated request.</param>
    /// <param name="sessionManager">The server-side session accessor used to resolve the token and customer identifier.</param>
    /// <param name="customerClient">The account client used to verify that the existing email can be loaded.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A blank email-change view, with account-service errors when the current email is unavailable.</returns>
    public static async Task<MemberChangeEmailDisplayModel> LoadChangeEmailAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerAccountClient customerClient,
        CancellationToken cancellationToken)
    {
        var accessTokenTask = sessionManager.GetAccessTokenAsync(context, cancellationToken);
        var customerIdTask = sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        await Task.WhenAll(accessTokenTask, customerIdTask);
        if (string.IsNullOrWhiteSpace(await accessTokenTask) || await customerIdTask is not { } customerId)
        {
            return MemberChangeEmailDisplayModel.Empty;
        }

        var profile = await customerClient.GetProfileAsync(customerId, cancellationToken);
        return profile.Profile is not null && !string.IsNullOrWhiteSpace(profile.Profile.Email)
            ? MemberChangeEmailDisplayModel.Empty
            : MemberChangeEmailDisplayModel.Empty with
            {
                Errors = [profile.ServiceAvailable
                    ? "Your account email could not be loaded."
                    : "Customer profile service is temporarily unavailable."],
            };
    }

    private static MemberAddressFieldsDisplayModel CreateAddress(CustomerAddress? address) => address is null
        ? MemberAddressFieldsDisplayModel.Empty
        : new MemberAddressFieldsDisplayModel(
            address.Building,
            address.AddressLine1,
            address.AddressLine2,
            address.City,
            address.State,
            address.PostalCode,
            address.CountryId);
}
