using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerAccountClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<CustomerAccountClient> logger) : ICustomerAccountClient
{
    public async Task<CustomerAddressProfileResult> GetAddressProfileAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false);
        }

        var result = await GetCustomerAsync(customerId, token, cancellationToken);
        return new(
            result.Customer is null ? null : new CustomerAddressProfile(result.Customer),
            result.ServiceAvailable,
            result.Authorized);
    }

    public async Task<CustomerAddressOperationResult> UpdateAddressesAsync(
        int customerId,
        CustomerAddressUpdate update,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, false, false);
        }

        try
        {
            var loaded = await GetCustomerAsync(customerId, token, cancellationToken);
            if (loaded.Customer is null)
            {
                return new(false, loaded.ServiceAvailable, loaded.Authorized);
            }

            var customer = loaded.Customer;
            var billing = await UpsertAddressAsync(
                customerId,
                customer.BillingAddressId,
                update.Billing,
                token,
                cancellationToken);
            if (!billing.Succeeded)
            {
                return billing.Operation;
            }

            var shipping = await UpsertAddressAsync(
                customerId,
                customer.ShippingAddressId,
                update.Shipping,
                token,
                cancellationToken);
            if (!shipping.Succeeded)
            {
                return shipping.Operation;
            }

            if (billing.AddressId != customer.BillingAddressId
                || shipping.AddressId != customer.ShippingAddressId)
            {
                using var request = Authorized(
                    HttpMethod.Put,
                    $"customers/{customerId}",
                    token,
                    new UpsertCustomerRequest(
                        customer.FirstName,
                        customer.LastName,
                        customer.Telephone,
                        customer.Mobile,
                        customer.Fax,
                        customer.Email,
                        customer.DateOfBirth,
                        customer.CompanyId,
                        billing.AddressId,
                        shipping.AddressId));
                using var response = await Client().SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Failure(response.StatusCode, token);
                }
            }

            return new(true, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable while updating owned addresses.");
            return new(false, false, true);
        }
    }

    public async Task<CustomerAddressOperationResult> UpdateEmailAsync(
        int customerId,
        string email,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, false, false);
        }

        try
        {
            var loaded = await GetCustomerAsync(customerId, token, cancellationToken);
            if (loaded.Customer is null)
            {
                return new(false, loaded.ServiceAvailable, loaded.Authorized);
            }

            var customer = loaded.Customer;
            using var request = Authorized(
                HttpMethod.Put,
                $"customers/{customerId}",
                token,
                new UpsertCustomerRequest(
                    customer.FirstName,
                    customer.LastName,
                    customer.Telephone,
                    customer.Mobile,
                    customer.Fax,
                    email.Trim(),
                    customer.DateOfBirth,
                    customer.CompanyId,
                    customer.BillingAddressId,
                    customer.ShippingAddressId));
            using var response = await Client().SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? new(true, true, true)
                : Failure(response.StatusCode, token);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable while synchronizing an account email.");
            return new(false, false, true);
        }
    }

    public async Task<CustomerAccountProfileResult> GetProfileAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false);
        }

        var result = await GetCustomerAsync(customerId, token, cancellationToken);
        return new(result.Customer, result.ServiceAvailable, result.Authorized);
    }

    public async Task<CustomerAddressOperationResult> UpdateProfileAsync(
        int customerId,
        CustomerProfileUpdate update,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, false, false);
        }

        try
        {
            var loaded = await GetCustomerAsync(customerId, token, cancellationToken);
            if (loaded.Customer is null)
            {
                return new(false, loaded.ServiceAvailable, loaded.Authorized);
            }

            var customer = loaded.Customer;
            var companyId = customer.CompanyId;
            var companyName = update.CompanyName?.Trim();
            var createdCompany = false;
            var updatedCompany = false;
            if (string.IsNullOrWhiteSpace(companyName))
            {
                companyId = null;
            }
            else if (customer.Company is null)
            {
                using var create = Authorized(
                    HttpMethod.Post,
                    "customers/companies",
                    token,
                    new UpsertCompanyRequest(companyName, update.TaxNumber, update.Registrar));
                using var response = await Client().SendAsync(create, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Failure(response.StatusCode, token);
                }

                var company = await response.Content.ReadFromJsonAsync<CustomerCompany>(cancellationToken);
                if (company is null)
                {
                    return new(false, true, true);
                }

                companyId = company.Id;
                createdCompany = true;
            }
            else
            {
                using var companyUpdate = Authorized(
                    HttpMethod.Put,
                    $"customers/companies/{customer.Company.Id}",
                    token,
                    new UpsertCompanyRequest(companyName, update.TaxNumber, update.Registrar));
                using var response = await Client().SendAsync(companyUpdate, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Failure(response.StatusCode, token);
                }

                updatedCompany = true;
            }

            using var customerUpdate = Authorized(
                HttpMethod.Put,
                $"customers/{customerId}",
                token,
                new UpsertCustomerRequest(
                    update.FirstName.Trim(),
                    update.LastName.Trim(),
                    update.Telephone,
                    update.Mobile,
                    update.Fax,
                    customer.Email,
                    update.DateOfBirth,
                    companyId,
                    customer.BillingAddressId,
                    customer.ShippingAddressId));
            using var customerResponse = await Client().SendAsync(customerUpdate, cancellationToken);
            if (!customerResponse.IsSuccessStatusCode)
            {
                if (createdCompany && companyId is not null)
                {
                    await DeleteCompanyBestEffortAsync(companyId.Value, token, cancellationToken);
                }
                else if (updatedCompany && customer.Company is not null)
                {
                    await RestoreCompanyBestEffortAsync(customer.Company, token, cancellationToken);
                }

                return Failure(customerResponse.StatusCode, token);
            }

            if (customer.Company is not null && companyId is null)
            {
                await DeleteCompanyBestEffortAsync(customer.Company.Id, token, cancellationToken);
            }

            return new(true, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable while updating an owned profile.");
            return new(false, false, true);
        }
    }

    private async Task DeleteCompanyBestEffortAsync(
        int companyId,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Delete, $"customers/companies/{companyId}", token, null);
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            logger.LogWarning(
                "Company {CompanyId} requires cleanup after an owned profile update.",
                companyId);
        }
    }

    private async Task RestoreCompanyBestEffortAsync(
        CustomerCompany company,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(
            HttpMethod.Put,
            $"customers/companies/{company.Id}",
            token,
            new UpsertCompanyRequest(company.Name, company.TaxNumber, company.Registrar));
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogCritical(
                "Company {CompanyId} requires manual reconciliation after a customer profile update failed.",
                company.Id);
        }
    }

    private async Task<CustomerLookup> GetCustomerAsync(
        int customerId,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized(HttpMethod.Get, $"customers/{customerId}", token, null);
            using var response = await Client().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var failure = Failure(response.StatusCode, token);
                return new(null, failure.ServiceAvailable, failure.Authorized);
            }

            var customer = await response.Content.ReadFromJsonAsync<CustomerAccountDetails>(
                cancellationToken);
            return new(customer, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable while reading an owned address profile.");
            return new(null, false, true);
        }
    }

    private async Task<AddressUpsert> UpsertAddressAsync(
        int customerId,
        int? existingAddressId,
        CustomerAddressInput input,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(
            existingAddressId is null ? HttpMethod.Post : HttpMethod.Put,
            existingAddressId is null
                ? $"customers/{customerId}/addresses"
                : $"customers/addresses/{existingAddressId.Value}",
            token,
            input);
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new(false, existingAddressId, Failure(response.StatusCode, token));
        }

        if (existingAddressId is not null)
        {
            return new(true, existingAddressId, new(true, true, true));
        }

        var created = await response.Content.ReadFromJsonAsync<CustomerAddress>(cancellationToken);
        return created is null
            ? new(false, null, new(false, true, true))
            : new(true, created.Id, new(true, true, true));
    }

    private CustomerAddressOperationResult Failure(HttpStatusCode statusCode, string token)
    {
        var authorized = statusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        if (!authorized)
        {
            tokenProvider.Invalidate(token);
        }

        return new(false, true, authorized);
    }

    private HttpClient Client() => clientFactory.CreateClient("customers");

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
        exception is HttpRequestException or System.Text.Json.JsonException
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
        int? ShippingAddressId);

    private sealed record UpsertCompanyRequest(
        string Name,
        string? TaxNumber,
        string? Registrar);

    private sealed record CustomerLookup(
        CustomerAccountDetails? Customer,
        bool ServiceAvailable,
        bool Authorized);

    private sealed record AddressUpsert(
        bool Succeeded,
        int? AddressId,
        CustomerAddressOperationResult Operation);
}
