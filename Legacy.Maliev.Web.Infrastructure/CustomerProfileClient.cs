using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerProfileClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<CustomerProfileClient> logger) : ICustomerProfileClient
{
    public async Task<CustomerProfileResult> CreateAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false);
        }

        try
        {
            using var request = Authorized(
                HttpMethod.Post,
                "customers",
                token,
                new UpsertCustomerRequest(firstName, lastName, email));
            using var response = await clientFactory.CreateClient("customers").SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
                return new(null, true, false);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new(null, true, true);
            }

            return new(
                await response.Content.ReadFromJsonAsync<CustomerProfile>(cancellationToken),
                true,
                true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable during account registration.");
            return new(null, false, true);
        }
    }

    public async Task<bool> DeleteAsync(int customerId, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            using var request = Authorized(HttpMethod.Delete, $"customers/{customerId}", token, null);
            using var response = await clientFactory.CreateClient("customers").SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable during registration compensation.");
            return false;
        }
    }

    public async Task<InstantQuotationCustomerProvisionResult> ProvisionInstantQuotationAsync(
        InstantQuotationCustomerSubmission customer,
        int billingCountryId,
        int shippingCountryId,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false, false);
        }

        var payload = new InstantQuotationProfileRequest(
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.TelephoneNumber,
            customer.MobileNumber,
            customer.CompanyName,
            customer.TaxIdentification,
            new InstantQuotationAddressRequest(
                customer.BillingBuilding,
                customer.BillingAddressLine1!,
                customer.BillingAddressLine2,
                customer.BillingCity,
                customer.BillingProvince,
                customer.BillingPostalCode,
                billingCountryId),
            customer.ShipToBillingAddress
                ? null
                : new InstantQuotationAddressRequest(
                    customer.ShippingBuilding,
                    customer.ShippingAddressLine1!,
                    customer.ShippingAddressLine2,
                    customer.ShippingCity,
                    customer.ShippingProvince,
                    customer.ShippingPostalCode,
                    shippingCountryId),
            customer.ShipToBillingAddress);

        try
        {
            using var request = Authorized(
                HttpMethod.Post,
                "customers/instant-quotation-profile",
                token,
                payload);
            using var response = await clientFactory.CreateClient("customers").SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
                return new(null, false, true, false);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new(null, false, (int)response.StatusCode < 500, true);
            }

            var result = await response.Content.ReadFromJsonAsync<InstantQuotationProfileResponse>(cancellationToken);
            return result?.CustomerId > 0
                ? new(result.CustomerId, result.CustomerCreated, true, true)
                : new(null, false, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("Customer service was unavailable during instant quotation profile provisioning.");
            return new(null, false, false, true);
        }
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string token,
        object? content)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content);
        }

        return request;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record UpsertCustomerRequest(
        string FirstName,
        string LastName,
        string? Telephone,
        string? Mobile,
        string? Fax,
        string Email,
        DateTime? DateOfBirth,
        int? CompanyId,
        int? BillingAddressId,
        int? ShippingAddressId)
    {
        public UpsertCustomerRequest(string firstName, string lastName, string email)
            : this(firstName, lastName, null, null, null, email, null, null, null, null)
        {
        }
    }

    private sealed record InstantQuotationAddressRequest(
        string? Building,
        string AddressLine1,
        string? AddressLine2,
        string? City,
        string? State,
        string? PostalCode,
        int CountryId);

    private sealed record InstantQuotationProfileRequest(
        string FirstName,
        string LastName,
        string Email,
        string? Telephone,
        string? Mobile,
        string? Company,
        string? TaxNumber,
        InstantQuotationAddressRequest Billing,
        InstantQuotationAddressRequest? Shipping,
        bool ShipToBillingAddress);

    private sealed record InstantQuotationProfileResponse(int CustomerId, bool CustomerCreated);
}
