using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Infrastructure;

public sealed record ContactTrustedCustomer(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    string Country);

public sealed record ContactTrustedCustomerLoadResult(
    ContactTrustedCustomer? Customer,
    bool IsAuthenticated,
    bool ServiceAvailable);

public interface IContactTrustedCustomerLoader
{
    Task<ContactTrustedCustomerLoadResult> LoadAsync(
        HttpContext context,
        IReadOnlyList<Country> countries,
        CancellationToken cancellationToken);
}

internal sealed class ContactTrustedCustomerLoader(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient customerClient) : IContactTrustedCustomerLoader
{
    public async Task<ContactTrustedCustomerLoadResult> LoadAsync(
        HttpContext context,
        IReadOnlyList<Country> countries,
        CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return new(null, false, true);
        }

        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is not > 0)
        {
            return new(null, true, false);
        }

        var result = await customerClient.GetProfileAsync(customerId.Value, cancellationToken);
        if (result.Profile is null)
        {
            return new(null, true, result.ServiceAvailable);
        }

        var profile = result.Profile;
        var country = profile.BillingAddress is null
            ? null
            : countries.FirstOrDefault(item => item.Id == profile.BillingAddress.CountryId)?.Name;
        var phone = !string.IsNullOrWhiteSpace(profile.Mobile) ? profile.Mobile : profile.Telephone;
        var trusted = new ContactTrustedCustomer(
            profile.FirstName,
            profile.LastName,
            profile.Email,
            phone,
            profile.Company?.Name,
            country ?? string.Empty);
        return new(trusted, true, result.ServiceAvailable);
    }
}
