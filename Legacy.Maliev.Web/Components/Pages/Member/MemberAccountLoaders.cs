using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

public static class MemberAccountLoaders
{
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
