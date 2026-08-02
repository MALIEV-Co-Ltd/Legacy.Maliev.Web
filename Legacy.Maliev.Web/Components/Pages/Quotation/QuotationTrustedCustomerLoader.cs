using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Quotation;

internal sealed record QuotationTrustedCustomerState(
    bool IsAuthenticated,
    bool ProfileAvailable,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    string? TaxNumber,
    string Country)
{
    public static QuotationTrustedCustomerState Anonymous { get; } = new(
        false,
        true,
        string.Empty,
        string.Empty,
        string.Empty,
        null,
        null,
        null,
        string.Empty);

    public static QuotationTrustedCustomerState Unavailable { get; } = Anonymous with
    {
        IsAuthenticated = true,
        ProfileAvailable = false
    };
}

internal static class QuotationTrustedCustomerLoader
{
    public static async Task<QuotationTrustedCustomerState> LoadAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerAccountClient customerClient,
        IReadOnlyList<Country> countries,
        CancellationToken cancellationToken)
    {
        try
        {
            var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
            if (context.User.Identity?.IsAuthenticated != true && customerId is null)
            {
                return QuotationTrustedCustomerState.Anonymous;
            }

            if (customerId is null or <= 0)
            {
                return QuotationTrustedCustomerState.Unavailable;
            }

            var result = await customerClient.GetProfileAsync(customerId.Value, cancellationToken);
            if (!result.ServiceAvailable || !result.Authorized || result.Profile is null)
            {
                return QuotationTrustedCustomerState.Unavailable;
            }

            var profile = result.Profile;
            var country = profile.BillingAddress is null
                ? null
                : countries.SingleOrDefault(item => item.Id == profile.BillingAddress.CountryId)?.Name;
            var state = new QuotationTrustedCustomerState(
                true,
                true,
                profile.FirstName.Trim(),
                profile.LastName.Trim(),
                profile.Email.Trim(),
                NormalizeOptional(profile.Mobile) ?? NormalizeOptional(profile.Telephone),
                NormalizeOptional(profile.Company?.Name),
                NormalizeOptional(profile.Company?.TaxNumber),
                country?.Trim() ?? string.Empty);

            return string.IsNullOrWhiteSpace(state.FirstName)
                || string.IsNullOrWhiteSpace(state.LastName)
                || string.IsNullOrWhiteSpace(state.Email)
                || string.IsNullOrWhiteSpace(state.Country)
                    ? QuotationTrustedCustomerState.Unavailable
                    : state;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return QuotationTrustedCustomerState.Unavailable;
        }
        catch (HttpRequestException)
        {
            return QuotationTrustedCustomerState.Unavailable;
        }
        catch (TimeoutException)
        {
            return QuotationTrustedCustomerState.Unavailable;
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
